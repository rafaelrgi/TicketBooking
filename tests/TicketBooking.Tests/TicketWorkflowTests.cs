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

public class TicketWorkflowTests : IClassFixture<LocalStackFixture>
{
    private readonly LocalStackFixture _fixture;

    public TicketWorkflowTests(LocalStackFixture fixture) => _fixture = fixture;

    [Fact]
    public Task DummyTest_ToCheckSetup()
    {
        var test  = true;
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
        const string json = $"{{\"PK\": \"EVENT#{eventId}\", \"SK\": \"TICKET#69\"}}";
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
        var worker = new TicketUpdateWorker(mockSqs.Object, mockHubContext.Object);

        // Act
        await worker. ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Check the "TicketUpdated" notification
        mockClientProxy.Verify(
            client => client.SendCoreAsync(
                "TicketUpdated", 
                It.Is<object[]>(args => args[0].ToString() == eventId), 
                TestContext.Current.CancellationToken), 
            Times.Once);
        // Check if message was deleted from queue
        mockSqs.Verify(x => x.DeleteMessageAsync(
                It.IsAny<string>(), 
                handleMock, 
                It.IsAny<CancellationToken>()), 
            Times.Once);
    }
    
}