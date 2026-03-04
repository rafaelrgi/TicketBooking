using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using TicketBooking.Domain.Entities;
using TicketBooking.Tests.Integration;

public class TicketApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly TicketApiFactory _factory;

    public TicketApiTests(TicketApiFactory factory)
    {
        _client = factory.CreateClient();
        _dynamoDb = factory.Services.GetRequiredService<IAmazonDynamoDB>();
        _factory = factory;
    }

    [Fact]
    public async Task GetEvents_ShouldReturnEvents()
    {
        // Arrange:
        const string eventId1 = "rock-in-rio-1985";
        const string eventId2 = "lollapalooza";
        const string eventId3 = "woodstock";
        const string eventId4 = "monsters-of-rock";
        const string eventId5 = "rock-grande-do-sul";

        await ResetDatabase();
        await SeedDatabase(eventId1);
        await SeedDatabase(eventId1);
        await SeedDatabase(eventId2);
        await SeedDatabase(eventId3);
        await SeedDatabase(eventId4);
        await SeedDatabase(eventId5);

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
    public async Task GetTickets_ShouldReturnTickets_WhenEventExists()
    {
        // Arrange:
        const string eventId = "rock-in-rio-1985";

        await ResetDatabase();
        await SeedDatabase(eventId);

        // Act:
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
        const string eventId = "rock-in-rio";
        const string status = "Confirmed";

        var ticket = new Ticket
        {
            EventId = eventId,
            TicketId = "T-800",
            UserId = "Hall-900",
            Status = status,
            UpdatedAt = DateTime.UtcNow
        };
        var content = new StringContent(JsonSerializer.Serialize(ticket), Encoding.UTF8, "application/json");
        await ResetDatabase();

        // Act:
        var response = await _client.PostAsync(
            $"/api/tickets/reserve",
             content,
            TestContext.Current.CancellationToken);

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

    private async Task SeedDatabase(string eventId = "rock-in-rio", string ticketId = "T-800", string status = "Reserved")
    {
        var ticket = new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
            { "SK", new AttributeValue { S = $"TICKET#{ticketId}" } },
            { "EventId", new AttributeValue { S = eventId } },
            { "TicketId", new AttributeValue { S = ticketId } },
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
        //var client = Services.GetRequiredService<IAmazonDynamoDB>();

        // Deleta se existir (para não dar erro na primeira vez)
        try { await _dynamoDb.DeleteTableAsync("Tickets"); } catch { }

        // Recria a tabela do zero
        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = "Tickets",
            AttributeDefinitions = [new("PK", ScalarAttributeType.S), new("SK", ScalarAttributeType.S)],
            KeySchema = [new("PK", KeyType.HASH), new("SK", KeyType.RANGE)],
            ProvisionedThroughput = new ProvisionedThroughput(5, 5)
        });
    }
}
