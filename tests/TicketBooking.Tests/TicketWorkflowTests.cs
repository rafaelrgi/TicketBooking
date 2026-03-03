using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using TicketBooking.Api;
using TicketBooking.Api.Hubs;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;

public class TicketWorkflowTests : IClassFixture<LocalStackFixture>
{
    private readonly LocalStackFixture _fixture;

    public TicketWorkflowTests(LocalStackFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DummyTest_ToCheckSetup()
    {
        var test = true;
        test.Should().Be(!false);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("{\"PK\": \"EVENT#rock-in-rio\", \"SK\": \"TICKET#1\"}", "rock-in-rio")]
    [InlineData("{\"PK\": \"EVENT#woodstock\"}", "woodstock")]
    [InlineData("{\"PK\": \"EVENT#\"}", "")]
    public void Worker_ShouldExtractCorrectEventId(string jsonInput, string expectedEventId)
    {
        // Act
        var result = TicketUpdateWorker.GetEventIdFromJson(jsonInput);

        // Assert
        result.Should().Be(expectedEventId);
    }

    [Theory]
    [InlineData("{\"PK\": \"EVENT#rock-in-rio\", \"SK\": \"TICKET#A1\"}", "A1")]
    [InlineData("{\"PK\": \"EVENT#woodstock\", \"SK\": \"TICKET#X-666-LONG_NAME\"}", "X-666-LONG_NAME")]
    [InlineData("{\"PK\": \"EVENT#rock-n-roll-circus\", \"SK\": \"TICKET#\"}", "")]
    public void Worker_ShouldExtractCorrectTicketId(string jsonInput, string expectedEventId)
    {
        // Act
        var result = TicketUpdateWorker.GetTicketIdFromJson(jsonInput);

        // Assert
        result.Should().Be(expectedEventId);
    }

    [Fact]
    public async Task WhenReservationExpires_ShouldCancel()
    {
        // Arrange
        const string pk = "EVENT#rock-grande-do=sul";
        const string sk = "TICKET#99";
        await _fixture.DynamoDb.PutItemAsync("Tickets", new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue(pk) },
            { "SK", new AttributeValue(sk) },
            { "Status", new AttributeValue("Reserved") }
        }, TestContext.Current.CancellationToken);

        // Act
        await _fixture.StepFunctions.StartExecutionAsync(new StartExecutionRequest
        {
            StateMachineArn = _fixture.StateMachineArn,
            Input = "{\"PK\": \"EVENT#rock-grande-do=sul\", \"SK\": \"TICKET#99\"}"
        }, TestContext.Current.CancellationToken);

        // Assert
        await Task.Delay(2000, TestContext.Current.CancellationToken);

        var result = await _fixture.DynamoDb.GetItemAsync("Tickets", new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue(pk) },
            { "SK", new AttributeValue(sk) }
        }, TestContext.Current.CancellationToken);

        var queueUrl = await _fixture.Sqs.GetQueueUrlAsync("TicketUpdatesQueue", TestContext.Current.CancellationToken);
        var messages = await _fixture.Sqs.ReceiveMessageAsync(queueUrl.QueueUrl, TestContext.Current.CancellationToken);

        result.Item["Status"].S.Should().Be("Cancelled");
        messages.Messages.Should().NotBeEmpty();
        messages.Messages[0].Body.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task ProcessMessage_WhenSuccessful_ShouldNotifyFrontendAndDeleteFromQueue()
    {
        // Arrange
        const string eventId = "rock-in-rio-1985";
        const string ticketId = "X1";
        const string json = $"{{\"PK\": \"EVENT#{eventId}\", \"SK\": \"TICKET#{ticketId}\"}}";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = json,
            ReceiptHandle = handleMock
        };

        var mockClientProxy = new Mock<IClientProxy>();

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<TicketHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var mockRepo = new Mock<ITicketRepository>();
        mockRepo.Setup(r => r.GetTicket(eventId, ticketId))
            .ReturnsAsync(new Ticket { EventId = eventId, TicketId = ticketId, Status = "Reserved" });

        var mockSqs = new Mock<IAmazonSQS>();
        var worker = new TicketUpdateWorker(mockSqs.Object, mockHubContext.Object, mockRepo.Object);

        // Act
        await worker.ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Check the "TicketUpdated" notification
        mockClientProxy.Verify(
            client => client.SendCoreAsync(
                "TicketUpdated",
                It.Is<object[]>(args => args[0].ToString() == eventId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // Check if message was deleted from queue
        mockSqs.Verify(x => x.DeleteMessageAsync(
                It.IsAny<string>(),
                handleMock,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMessage_WhenJsonIsInvalid_ShouldNotNotifyNorDeleteMessage()
    {
        // Arrange
        var eventId = "monsters-of-rock";
        var ticketId = "TICKET#123";
        var badJson = "{ \"PK\": \"EVENT#broken-json... wait, where is the rest? Thanos snap!!!";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = badJson,
            ReceiptHandle = handleMock
        };

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<TicketHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var mockRepo = new Mock<ITicketRepository>();
        mockRepo.Setup(r => r.GetTicket(eventId, ticketId))
            .ReturnsAsync(new Ticket { EventId = eventId, TicketId = ticketId, Status = "Reserved" });

        var mockSqs = new Mock<IAmazonSQS>();
        var worker = new TicketUpdateWorker(mockSqs.Object, mockHubContext.Object, mockRepo.Object);

        // Act
        Func<Task> act = async () => await worker.ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Error was caught?
        await act.Should().ThrowAsync<System.Text.Json.JsonException>();

        // Message should not be deleted on error
        mockSqs.Verify(x => x.DeleteMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Notification of change should not be sent on error
        mockClientProxy.Verify(x => x.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMessage_WhenTicketIsAlreadyCancelled_ShouldNotNotifyAgain()
    {
        // Arrange
        var eventId = "monsters-of-rock";
        var ticketId = "123";
        var json = $"{{\"PK\": \"EVENT#{eventId}\", \"SK\": \"TICKET#{ticketId}\"}}";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = json,
            ReceiptHandle = handleMock
        };

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        var mockHubContext = new Mock<IHubContext<TicketHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var mockSqs = new Mock<IAmazonSQS>();

        // Mock Repo with canceled item
        var mockRepo = new Mock<ITicketRepository>();
        mockRepo.Setup(r => r.GetTicket(eventId, ticketId))
            .ReturnsAsync(new Ticket { EventId = eventId, TicketId = ticketId, Status = "Cancelled" });

        var worker = new TicketUpdateWorker(mockSqs.Object, mockHubContext.Object, mockRepo.Object);

        // Act
        await worker.ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Cancelled item, no notification!
        mockClientProxy.Verify(
            client => client.SendCoreAsync("TicketUpdated", It.IsAny<object[]>(),
                TestContext.Current.CancellationToken),
            Times.Never);

        // Canceled item, should delete the message
        mockSqs.Verify(x => x.DeleteMessageAsync(
                It.IsAny<string>(), handleMock,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
