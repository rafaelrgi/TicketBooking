using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketBooking.Domain.Constants;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Tests.Integration;

public class EventApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ApiFactory _factory;

    public EventApiTests(ApiFactory factory)
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
    public async Task GetEvent_ShouldReturn401WhenNotAuth()
    {
        // Arrange
        await ResetDatabaseAndCache();
        const string eventId = "Look in rio";
        await SeedDatabase(eventId, 512);
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.GetAsync($"{ApiRoutes.Events.GetEvent}{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_ShouldNotReturn401WhenAuth()
    {
        // Arrange
        await ResetDatabaseAndCache();
        const string eventId = "Lok in rio";
        await SeedDatabase(eventId, 512);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");

        // Act
        var response = await _client.GetAsync($"{ApiRoutes.Events.GetEvent}{eventId}", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Request_ShouldReturn401_WhenTokenExpired()
    {
        // Arrange
        await ResetDatabaseAndCache();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        client.DefaultRequestHeaders.Add("X-Test-Expired", "true");

        // Act
        var response = await client.GetAsync(ApiRoutes.Events.GetEvents, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SaveEvent_ShouldReturn200_WhenUserIsAdmin()
    {
        // Arrange
        await ResetDatabaseAndCache();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        client.DefaultRequestHeaders.Add("X-Role", "Admin");
        var content = JsonContent.Create(new { eventId = "Lóke In Rio", totalTickets = 512 });

        // Act
        var response = await client.PutAsync(ApiRoutes.Events.GetEvents, content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SaveEvent_ShouldReturn403_WhenUserIsNotAdmin()
    {
        // Arrange
        await ResetDatabaseAndCache();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        client.DefaultRequestHeaders.Add("X-Role", "User");
        var content = JsonContent.Create(new { eventId = "Loky en Rio", totalTickets = 32 });

        // Act
        var response = await client.PutAsync(ApiRoutes.Events.GetEvents, content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEvents_ShouldReturnSuccess_WhenLoggedUserIsNotAdmin()
    {
        // Arrange
        await ResetDatabaseAndCache();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        client.DefaultRequestHeaders.Add("X-Role", "User");

        // Act
        var response = await client.GetAsync(ApiRoutes.Events.GetEvents, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task GetEvents_ShouldReturnEvents()
    {
        // Arrange:
        await ResetDatabaseAndCache();

        const string eventId1 = "Lok in rio-1985";
        const string eventId2 = "lollapalooza";
        const string eventId3 = "woodstock";
        const string eventId4 = "monsters-of-rock";
        const string eventId5 = "rock-grande-do-sul";

        await SeedDatabase(eventId1, 1024);
        await SeedDatabase(eventId1, 256);
        await SeedDatabase(eventId2, 4096);
        await SeedDatabase(eventId3, 512);
        await SeedDatabase(eventId4, 256);
        await SeedDatabase(eventId5, 128);

        // Act:
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.GetAsync(ApiRoutes.Events.GetEvents, TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        var events =
            await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(events);
        Assert.NotEmpty(events);
        Assert.Equal(5, events.Count);
        Assert.Distinct(events);
        Assert.Contains(eventId1, events);
        Assert.Contains(eventId2, events);
        Assert.Contains(eventId5, events);
        Assert.DoesNotContain("avocado", events);
    }

    [Fact]
    public async Task CreateEvent_ShouldCreateEvent()
    {
        // Arrange
        await ResetDatabaseAndCache();

        var evt = new Event
        {
            EventId = "Loki in rio",
            TotalTickets = 1024,
        };
        var content = new StringContent(JsonSerializer.Serialize(evt), Encoding.UTF8, "application/json");

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.PutAsync(ApiRoutes.Events.GetEvents, content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var row = await GetEventFromDb(evt.EventId);

        // Assert
        Assert.NotNull(row);
        Assert.Equal(evt.TotalTickets, row.TotalTickets);
    }

    [Fact]
    public async Task GetDashboardStats_ShouldReturnCorrectMath()
    {
        // Arrange
        await ResetDatabaseAndCache();

        const string eventId = "Loki in rio";
        const int totalCapacity = 50000;
        const int confirmedCount = 1;
        const int reservedCount = 3;

        await SeedDatabase(eventId, totalCapacity);

        // "Reserve" 3 tickets
        for (int i = 1; i <= reservedCount; i++)
        {
            await _dynamoDb.PutItemAsync("Tickets", new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
                { "SK", new AttributeValue { S = $"TICKET#{i:D3}" } },
                { "Status", new AttributeValue { S = "Reserved" } }
            }, TestContext.Current.CancellationToken);
        }

        // "Confirm" 1 ticket
        await _dynamoDb.PutItemAsync("Tickets", new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
            { "SK", new AttributeValue { S = $"TICKET#{666:D3}" } },
            { "Status", new AttributeValue { S = "Confirmed" } }
        }, TestContext.Current.CancellationToken);

        // Act
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestScheme");
        var response = await _client.GetAsync($"{ApiRoutes.Events.GetStats}{eventId}", TestContext.Current.CancellationToken);
        var stats = await response.Content.ReadFromJsonAsync<EventStats>(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(totalCapacity, stats.Total);
        Assert.Equal(reservedCount, stats.Reserved);
        Assert.Equal(confirmedCount, stats.Confirmed);
        Assert.Equal(totalCapacity - reservedCount - confirmedCount, stats.Available);
    }

    private async Task<Event?> GetEventFromDb(string eventId)
    {
        var getRequest = new GetItemRequest
        {
            TableName = "Events",
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{eventId}" } }
            }
        };
        var dbResponse = await _dynamoDb.GetItemAsync(getRequest);
        if (!dbResponse.IsItemSet)
            return null;

        var item = dbResponse.Item;
        string GetS(string key) => item.TryGetValue(key, out var attr) ? attr.S : string.Empty;
        var evt = new Event
        {
            EventId = GetS("EventId"),
            TotalTickets = int.TryParse(GetS("TotalTickets"), out var total) ? total : 0,
        };

        return evt;
    }

    private async Task SeedDatabase(string eventId, int totalTickets)
    {
        var evt = new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
            { "EventId", new AttributeValue { S = eventId } },
            { "TotalTickets", new AttributeValue { S = totalTickets.ToString() } },
        };

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = "Events",
            Item = evt
        });
    }

    private async Task ResetDatabaseAndCache()
    {
        await _factory.ClearCache();
        try
        {
            await _dynamoDb.DeleteTableAsync("Events");
        }
        catch
        {
        }

        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Events",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });
    }
}
