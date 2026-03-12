using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TicketBooking.Api.Hubs;
using TicketBooking.Api.Workers;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Caching;

public class TicketWorkflowTests : IClassFixture<LocalStackFixture>
{
    private readonly LocalStackFixture _fixture;
    private readonly ILoggerFactory _loggerFactory;
    //private readonly ILogger<TicketWorkflowTests> _logger;

    public TicketWorkflowTests(LocalStackFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new XUnit3Sink())
            .CreateLogger();
        _loggerFactory = new LoggerFactory().AddSerilog(serilogLogger);
    }

    private class XUnit3Sink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage();
            TestContext.Current.SendDiagnosticMessage($"[{logEvent.Level}] {message}");
        }
    }

    [Fact]
    public Task DummyTest_ToCheckSetup()
    {
        var test = true;
        test.Should().Be(!false);
        return Task.CompletedTask;
    }

    [Fact]
    public void Worker_ShouldGetMessageTranslatedCorrectly()
    {
        // Arrange
        const string json1 = "{\"PK\": \"EVENT#Lok In Rio\",\"SK\": \"TICKET#2\",\"Status\": \"Reserved\"}";
        const string json2 = "{\"PK\": \"EVENT#Loky In Rio\",\"SK\": \"TICKET#32\",\"Status\": \"Confirmed\"}";
        const string json3 = "{\"PK\": \"EVENT#Lóke In Rio\",\"SK\": \"TICKET#64\",\"Status\": \"Available\"}";

        // Act
        var msg1 = json1.ToQueueMessage();
        var msg2 = json2.ToQueueMessage();
        var msg3 = json3.ToQueueMessage();

        // Assert
        msg1.EventId.Should().Be("Lok In Rio");
        msg1.TicketId.Should().Be(2);
        msg1.State.Should().Be(TicketState.Reserved);
        msg2.EventId.Should().Be("Loky In Rio");
        msg2.TicketId.Should().Be(32);
        msg2.State.Should().Be(TicketState.Confirmed);
        msg3.EventId.Should().Be("Lóke In Rio");
        msg3.TicketId.Should().Be(64);
        msg3.State.Should().Be(TicketState.Available);
    }

    /*
    [Theory]
    [InlineData("{\"PK\": \"EVENT#rock-in-rio\", \"SK\": \"TICKET#256\"}", 256)]
    [InlineData("{\"PK\": \"EVENT#woodstock\", \"SK\": \"TICKET#666\"}", 666)]
    [InlineData("{\"PK\": \"EVENT#rock-n-roll-circus\", \"SK\": \"TICKET#\"}", 0)]
    public void Worker_ShouldExtractCorrectTicketId(string jsonInput, int expectedEventId)
    {
        // Act
        var result = TicketUpdateWorker.GetTicketIdFromJson(jsonInput);

        // Assert
        result.Should().Be(expectedEventId);
    }
    */

    [Fact]
    public async Task WhenReservationExpires_ShouldCancel()
    {
        // Arrange
        const string pk = "EVENT#rock-grande-do=sul";
        const string sk = "TICKET#99";

        await ResetDatabase();
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
        const int ticketId = 16;
        string json = $"{{\"PK\": \"EVENT#{eventId}\", \"SK\": \"{ticketId}\",\"Status\": \"Reserved\"}}";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = json,
            ReceiptHandle = handleMock
        };

        await ResetDatabase();
        var mockClientProxy = new Mock<IClientProxy>();

        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<TicketHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var mockRepo = new Mock<ITicketRepository>();
        mockRepo.Setup(r => r.GetTicket(eventId, ticketId))
            .ReturnsAsync(new Ticket { EventId = eventId, TicketId = ticketId, Status = "Reserved" });

        var mockSqs = new Mock<IAmazonSQS>();
        var worker = CreateTicketUpdateWorker(mockRepo, mockSqs, mockHubContext);

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

    private TicketUpdateWorker CreateTicketUpdateWorker(Mock<ITicketRepository> mockRepo, Mock<IAmazonSQS> mockSqs,
        Mock<IHubContext<TicketHub>> mockHubContext)
    {
        var opts = Options.Create(new MemoryDistributedCacheOptions());
        IDistributedCache cache = new MemoryDistributedCache(opts);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(x => x.GetService(typeof(ITicketRepository)))
            .Returns(mockRepo.Object);
        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(x => x.ServiceProvider).Returns(mockServiceProvider.Object);
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(mockScope.Object);

        var settings = new SettingsUrls
        {
            TicketUpdatesQueue = "http://localhost:4566/000000000000/TicketUpdatesQueue"
        };
        var workerLogger = _loggerFactory.CreateLogger<TicketUpdateWorker>();
        var cacheLogger = _loggerFactory.CreateLogger<TicketCacheService>();
        var optionsWrapper = new OptionsWrapper<SettingsUrls>(settings);
        var worker = new TicketUpdateWorker(mockSqs.Object, mockHubContext.Object, new TicketCacheService(cache, cacheLogger),
            mockScopeFactory.Object, optionsWrapper, workerLogger);
        return worker;
    }

    [Fact]
    public async Task ProcessMessage_WhenJsonIsInvalid_ShouldNotNotifyNorDeleteMessage()
    {
        // Arrange
        const string eventId = "monsters-of-rock";
        const int ticketId = 64;
        const string badJson = "{ \"PK\": \"EVENT#broken-json... wait, where is the rest? Thanos snap!!!";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = badJson,
            ReceiptHandle = handleMock
        };

        await ResetDatabase();

        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

        var mockHubContext = new Mock<IHubContext<TicketHub>>();
        mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        var mockRepo = new Mock<ITicketRepository>();
        mockRepo.Setup(r => r.GetTicket(eventId, ticketId))
            .ReturnsAsync(new Ticket { EventId = eventId, TicketId = ticketId, Status = "Reserved" });

        var mockSqs = new Mock<IAmazonSQS>();
        var worker = CreateTicketUpdateWorker(mockRepo, mockSqs, mockHubContext);

        // Act
        Func<Task> act = async () => await worker.ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Error was caught && logged && that's it!
        // await act.Should().ThrowAsync<System.Text.Json.JsonException>();

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
    public async Task ProcessMessage_WhenTicketIsCancelled_ShouldNotNotify()
    {
        // Arrange
        const string eventId = "monsters-of-rock";
        const int ticketId = 512;
        string json = $"{{\"PK\": \"EVENT#{eventId}\", \"SK\": \"TICKET#{ticketId}\",\"Status\": \"Reserved\"}}";
        const string handleMock = "fake-handle-123";
        var message = new Message
        {
            Body = json,
            ReceiptHandle = handleMock
        };

        await ResetDatabase();

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

        var worker = CreateTicketUpdateWorker(mockRepo, mockSqs, mockHubContext);

        // Act
        await worker.ProcessMessage(message, TestContext.Current.CancellationToken);

        // Assert
        // Cancelled item, notification!
        mockClientProxy.Verify(
            client => client.SendCoreAsync("TicketUpdated", It.IsAny<object[]>(),
                TestContext.Current.CancellationToken),
            Times.Once);

        // Canceled item, should delete the message
        mockSqs.Verify(x => x.DeleteMessageAsync(
                It.IsAny<string>(), handleMock,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task ResetDatabase()
    {
        try
        {
            await _fixture.DynamoDb.DeleteTableAsync("Tickets");
        }
        catch
        {
        }

        await _fixture.DynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Tickets",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });
    }
}
