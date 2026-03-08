using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Tests.Integration;

public class TicketApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ApiFactory _collection;

    public TicketApiTests(ApiFactory collection)
    {
        _client = collection.CreateClient();
        _dynamoDb = collection.Services.GetRequiredService<IAmazonDynamoDB>();
        _collection = collection;
    }

    [Fact]
    public Task DummyTest_ToCheckSetup()
    {
        const bool test = false;
        Assert.True(!test);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetTickets_ShouldReturnTickets_WhenEventExists()
    {
        // Arrange:
        await ResetDatabase();
        const string eventId = "Loki in rio";
        await SeedDatabase(eventId, 128, "Reserved");

        // Act
        var response = await _client.GetAsync($"/api/tickets/{eventId}", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var tickets =
            await response.Content.ReadFromJsonAsync<List<Ticket>>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tickets);
        Assert.NotEmpty(tickets);
        Assert.All(tickets, t => Assert.Equal(eventId, t.EventId));
    }

    [Fact]
    public async Task ReserveTicket_ShouldReserveTicket()
    {
        // Arrange:
        await ResetDatabase();
        const string eventId = "Loki in rio";
        const string status = "Confirmed";
        const int eventQuota = 1024;

        var evt = await CreateEventApi(eventId, eventQuota);
        Assert.NotNull(evt);

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = 128,
            UserId = "Hall-900",
            IsVip = true,
            Status = status,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync($"/api/tickets/reserve", content, TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        response = await _client.GetAsync($"/api/tickets/{eventId}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var tickets =
            await response.Content.ReadFromJsonAsync<List<Ticket>>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(tickets);
        Assert.NotEmpty(tickets);
        Assert.All(tickets, t => Assert.Equal(eventId, t.EventId));
        Assert.All(tickets, t => Assert.Equal("Reserved", t.Status));
    }

    [Fact]
    public async Task ConfirmPayment_ShouldUpdateStatusAndNotify()
    {
        await ResetDatabase();
        // Arrange
        const string eventId = "Loki in rio";
        const int ticketId = 256;

        await SeedDatabase(eventId, ticketId, "Reserved");
        var request = new { EventId = eventId, TicketId = ticketId, userId = "Skynet" };

        bool notified = false;
        var hubConnection = _collection.CreateHubConnection();
        hubConnection?.On<object>("TicketUpdated", (data) => notified = true);
        await hubConnection?.StartAsync(TestContext.Current.CancellationToken)!;
        // Act
        var response = await _client.PostAsJsonAsync("/api/tickets/confirm", request,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();

        var getResponse = await _client.GetAsync($"/api/tickets/{eventId}", TestContext.Current.CancellationToken);
        var tickets =
            await getResponse.Content.ReadFromJsonAsync<List<Ticket>>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tickets);
        var confirmedTicket = tickets.FirstOrDefault(t => t.TicketId == ticketId);

        Assert.Equal("Confirmed", confirmedTicket?.Status);

        // buy some time for the worker
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        Assert.True(notified, "Worker should have read the queue and send the notification");
    }

    [Fact]
    public async Task ConfirmPayment_ShouldFailWhenTicketNotReserved()
    {
        // Arrange
        await ResetDatabase();
        const string eventId = "Loki in rio";
        const int ticketIdReserve = 2;
        const int ticketIdConfirm = 8;
        const int eventQuota = 64;

        var evt = await CreateEventApi(eventId, eventQuota);
        Assert.NotNull(evt);

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = ticketIdReserve,
            UserId = "Gort",
            IsVip = true,
            Status = "xxx",
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseReserve = await _client.PostAsync($"/api/tickets/reserve", content, TestContext.Current.CancellationToken);
        Assert.True(responseReserve.IsSuccessStatusCode);

        // Act
        ticket.TicketId = ticketIdConfirm;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseConfirm = await _client.PostAsync($"/api/tickets/confirm", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseConfirm.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenEventDoesNotExit()
    {
        // Arrange:
        await ResetDatabase();
        const string eventId = "Loki in rio";

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = 1,
            UserId = "Gort",
            IsVip = true,
            Status = "xxx",
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");

        // Act
        var responseReserve = await _client.PostAsync(
            $"/api/tickets/reserve",
            content,
            TestContext.Current.CancellationToken);

        var responseGet = await _client.GetAsync($"/api/tickets/{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseReserve.IsSuccessStatusCode);
        Assert.False(responseGet.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenTicketOverEventQuota()
    {
        // Arrange:
        await ResetDatabase();
        const string eventId = "Loki in rio";
        const int eventQuota = 512;

        var evt = await CreateEventApi(eventId, eventQuota);
        Assert.NotNull(evt);

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = eventQuota + 1,
            UserId = "Gort",
            IsVip = true,
            Status = "xxx",
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");

        // Act
        var responseReserve = await _client.PostAsync(
            $"/api/tickets/reserve",
            content,
            TestContext.Current.CancellationToken);

        var responseGet = await _client.GetAsync($"/api/tickets/{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseReserve.IsSuccessStatusCode);
        Assert.False(responseGet.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenTicketNotAvailable()
    {
        await ResetDatabase();
        const string eventId = "Loki in rio";
        const int eventQuota = 128;
        const int freeTicket = 16;
        const int reservedTicket = 8;
        const int confirmedTicket = 32;

        var evt = await CreateEventApi(eventId, eventQuota);
        Assert.NotNull(evt);

        await SeedDatabase(eventId, reservedTicket, "Reserved");
        await SeedDatabase(eventId, confirmedTicket, "Confirmed");

        // Act
        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = freeTicket,
            UserId = "Gort",
            IsVip = true,
            Status = "xxx",
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseFree = await _client.PostAsync($"/api/tickets/reserve", content, TestContext.Current.CancellationToken);

        ticket.TicketId = reservedTicket;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseReserved = await _client.PostAsync($"/api/tickets/reserve", content, TestContext.Current.CancellationToken);

        ticket.TicketId = confirmedTicket;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseConfirmed =
            await _client.PostAsync($"/api/tickets/reserve", content, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(responseFree.IsSuccessStatusCode);
        Assert.False(responseReserved.IsSuccessStatusCode);
        Assert.False(responseConfirmed.IsSuccessStatusCode);
    }

    private async Task<Event?> CreateEventApi(string eventId, int eventQuota)
    {
        var row = new Event
        {
            EventId = eventId,
            TotalTickets = eventQuota
        };
        var content = new StringContent(JsonSerializer.Serialize(row), Encoding.UTF8, "application/json");
        var response = await _client.PutAsync(
            $"/api/events",
            content,
            TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        response = await _client.GetAsync($"/api/events/{eventId}", TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        row = await response.Content.ReadFromJsonAsync<Event>(TestContext.Current.CancellationToken);
        return row;
    }

    private async Task SeedDatabase(string eventId, int ticketId, string status)
    {
        var ticket = new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
            { "SK", new AttributeValue { S = $"TICKET#{ticketId}" } },
            { "EventId", new AttributeValue { S = eventId } },
            { "Status", new AttributeValue { S = status } }
        };

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = "Tickets",
            Item = ticket
        });
    }

    private async Task ResetDatabase()
    {
        try
        {
            await _dynamoDb.DeleteTableAsync("Events");
        }
        catch
        {
        }

        try
        {
            await _dynamoDb.DeleteTableAsync("Tickets");
        }
        catch
        {
        }

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Tickets",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });

        //Yes, Ticket depends upon Events sometimes
        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Events",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });
    }
}
