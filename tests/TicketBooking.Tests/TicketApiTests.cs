using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketBooking.Domain.Common;
using TicketBooking.Domain.Constants;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Tests.Integration;

public class TicketApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ApiFactory _factory;

    public TicketApiTests(ApiFactory factory)
    {
        var urls = factory.Services.GetRequiredService<IOptions<SettingsUrls>>().Value;
        _client = factory.CreateClient();
        _client.BaseAddress = new Uri(urls.ApiBase);

        _dynamoDb = factory.Services.GetRequiredService<IAmazonDynamoDB>();
        _factory = factory;
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
        await ResetDatabaseAndCache();
        const string eventId = "Loki in rio";
        await SeedDatabase(eventId, 128, "Reserved");

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response =
            await _client.GetAsync($"{ApiRoutes.Tickets.GetTickets}{eventId}", TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var tickets = await response.Content.ReadFromJsonAsync<List<Ticket>>(
            cancellationToken: TestContext.Current.CancellationToken, options: JsonDefaults.Options);

        Assert.NotNull(tickets);
        Assert.NotEmpty(tickets);
        Assert.All(tickets, t => Assert.Equal(eventId, t.EventId));
    }

    [Fact]
    public async Task ReserveTicket_ShouldReserveTicket()
    {
        // Arrange:
        await ResetDatabaseAndCache();
        const string eventId = "Loki in rio";
        const TicketStatus status = TicketStatus.Confirmed;
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
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.PostAsync($"{ApiRoutes.Tickets.ReserveTicket}", content,
            TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        response = await _client.GetAsync($"{ApiRoutes.Tickets.GetTickets}{eventId}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var tickets = await response.Content.ReadFromJsonAsync<List<Ticket>>(
            cancellationToken: TestContext.Current.CancellationToken, options: JsonDefaults.Options);

        Assert.NotNull(tickets);
        Assert.NotEmpty(tickets);
        Assert.All(tickets, t => Assert.Equal(eventId, t.EventId));
        Assert.All(tickets, t => Assert.Equal(TicketStatus.Reserved, t.Status));
    }

    [Fact]
    public async Task ConfirmPayment_ShouldUpdateStatusAndNotify()
    {
        await ResetDatabaseAndCache();
        // Arrange
        const string eventId = "Loki in rio";
        const int ticketId = 256;

        await SeedDatabase(eventId, ticketId, "Reserved");
        var request = new { EventId = eventId, TicketId = ticketId, userId = "Skynet" };

        bool notified = false;
        var hubConnection = _factory.CreateHubConnection();
        hubConnection?.On<object>("TicketUpdated", (data) => notified = true);
        await hubConnection?.StartAsync(TestContext.Current.CancellationToken)!;

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.PostAsJsonAsync(ApiRoutes.Tickets.ConfirmTicket, request,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();

        var getResponse =
            await _client.GetAsync($"{ApiRoutes.Tickets.GetTickets}{eventId}", TestContext.Current.CancellationToken);
        var tickets =
            await getResponse.Content.ReadFromJsonAsync<List<Ticket>>(cancellationToken: TestContext.Current.CancellationToken,
                options: JsonDefaults.Options
            );

        Assert.NotNull(tickets);
        var confirmedTicket = tickets.FirstOrDefault(t => t.TicketId == ticketId);

        Assert.Equal(TicketStatus.Confirmed, confirmedTicket?.Status);

        // buy some time for the worker
        await Task.Delay(2000, TestContext.Current.CancellationToken);
        Assert.True(notified, "Worker should have read the queue and send the notification");
    }

    [Fact]
    public async Task ConfirmPayment_ShouldFailWhenTicketNotReserved()
    {
        // Arrange
        await ResetDatabaseAndCache();
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
            Status = TicketStatus.Confirmed,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseReserve =
            await _client.PostAsync(ApiRoutes.Tickets.ReserveTicket, content, TestContext.Current.CancellationToken);
        Assert.True(responseReserve.IsSuccessStatusCode);

        // Act
        ticket.TicketId = ticketIdConfirm;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseConfirm =
            await _client.PostAsync(ApiRoutes.Tickets.ConfirmTicket, content, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseConfirm.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenEventDoesNotExit()
    {
        // Arrange:
        await ResetDatabaseAndCache();
        const string eventId = "Loki in rio";

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = 1,
            UserId = "Gort",
            IsVip = true,
            Status = TicketStatus.Reserved,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var responseReserve = await _client.PostAsync(
            ApiRoutes.Tickets.ReserveTicket,
            content,
            TestContext.Current.CancellationToken);

        var responseGet =
            await _client.GetAsync($"{ApiRoutes.Tickets.GetTickets}{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseReserve.IsSuccessStatusCode);
        Assert.False(responseGet.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenTicketOverEventQuota()
    {
        // Arrange:
        await ResetDatabaseAndCache();
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
            Status = TicketStatus.Confirmed,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var responseReserve = await _client.PostAsync(
            ApiRoutes.Tickets.ReserveTicket,
            content,
            TestContext.Current.CancellationToken);

        var responseGet =
            await _client.GetAsync($"{ApiRoutes.Tickets.GetTickets}{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(responseReserve.IsSuccessStatusCode);
        Assert.False(responseGet.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ReserveTicket_ShouldFailWhenTicketNotAvailable()
    {
        await ResetDatabaseAndCache();
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
            Status = TicketStatus.Confirmed,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseFree =
            await _client.PostAsync(ApiRoutes.Tickets.ReserveTicket, content, TestContext.Current.CancellationToken);

        ticket.TicketId = reservedTicket;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseReserved =
            await _client.PostAsync(ApiRoutes.Tickets.ReserveTicket, content, TestContext.Current.CancellationToken);

        ticket.TicketId = confirmedTicket;
        content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        var responseConfirmed =
            await _client.PostAsync(ApiRoutes.Tickets.ReserveTicket, content, TestContext.Current.CancellationToken);

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
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.PutAsync(
            ApiRoutes.Events.GetEvents,
            content,
            TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        response = await _client.GetAsync($"{ApiRoutes.Events.GetEvent}{eventId}", TestContext.Current.CancellationToken);
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

    private async Task ResetDatabaseAndCache()
    {
        await _factory.ClearCache();
        await ClearTable("Tickets", "PK", "SK");
        await ClearTable("Events", "PK");
    }

    private async Task ClearTable(string tableName, string partitionKey, string? sortKey = null)
    {
        var scan = await _dynamoDb.ScanAsync(new ScanRequest { TableName = tableName });

        foreach (var item in scan.Items)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                { partitionKey, item[partitionKey] }
            };
            if (sortKey != null) key.Add(sortKey, item[sortKey]);

            await _dynamoDb.DeleteItemAsync(tableName, key);
        }
    }
}
