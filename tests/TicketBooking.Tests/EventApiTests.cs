using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Tests.Integration;

public class EventApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly ApiFactory _collection;

    public EventApiTests(ApiFactory collection)
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
    public async Task GetEvents_ShouldReturnEvents()
    {
        // Arrange:
        await ResetDatabase();

        const string eventId1 = "rock-in-rio-1985";
        const string eventId2 = "lollapalooza";
        const string eventId3 = "woodstock";
        const string eventId4 = "monsters-of-rock";
        const string eventId5 = "rock-grande-do-sul";

        await SeedDatabase(eventId1, 50000);
        await SeedDatabase(eventId1, 1000);
        await SeedDatabase(eventId2, 300000);
        await SeedDatabase(eventId3, 1000);
        await SeedDatabase(eventId4, 600);
        await SeedDatabase(eventId5, 666);

        // Act:
        var response = await _client.GetAsync($"/api/events/", TestContext.Current.CancellationToken);

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
        await ResetDatabase();

        var evt = new Event
        {
            EventId = "Loki in rio",
            TotalTickets = 1024,
        };
        var content = new StringContent(JsonSerializer.Serialize(evt), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PutAsync($"/api/events", content, TestContext.Current.CancellationToken);
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
        await ResetDatabase();

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
        var response = await _client.GetAsync($"/api/events/stats/{eventId}", TestContext.Current.CancellationToken);
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

    private async Task ResetDatabase()
    {
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
