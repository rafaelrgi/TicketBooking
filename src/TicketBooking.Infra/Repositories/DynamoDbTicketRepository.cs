using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;

namespace TicketBooking.Infra.Repositories;

public class DynamoDbTicketRepository : ITicketRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "Tickets";

    public DynamoDbTicketRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<bool> ReserveTicket(Ticket ticket)
    {
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
        {
          { "PK", new AttributeValue { S = $"EVENT#{ticket.EventId}" } },
          { "SK", new AttributeValue { S = $"TICKET#{ticket.TicketId}" } },
          { "UserId", new AttributeValue { S = ticket.UserId } },
          { "Status", new AttributeValue { S = "Reserved" } },
          { "UpdatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } }
        }
        };

        var response = await _dynamoDb.PutItemAsync(request);
        return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
    }

    public async Task<bool> ConfirmTicket(Ticket ticket)
    {
        var request = new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{ticket.EventId}" } },
                { "SK", new AttributeValue { S = $"TICKET#{ticket.TicketId}" } },
                { "UserId", new AttributeValue { S = ticket.UserId } },
                { "Status", new AttributeValue { S = "Confirmed" } },
                { "UpdatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } }
            }
        };

        var response = await _dynamoDb.PutItemAsync(request);
        return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
    }

    public async Task<List<Ticket>> GetTickets(string eventId)
    {
        var request = new QueryRequest
        {
            TableName = TableName,
            KeyConditionExpression = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
            { ":pk", new AttributeValue { S = $"EVENT#{eventId}" } }
        }
        };

        var response = await _dynamoDb.QueryAsync(request);

        var tickets = response.Items.Select(item => new Ticket
        {
            EventId = eventId,
            TicketId = item["SK"].S.Replace("TICKET#", ""),
            Status = item["Status"].S,
            // DateTimeOffset or DateTime just to make life easier on Blazor
            UpdatedAt = item.ContainsKey("UpdatedAt")
                ? DateTime.Parse(item["UpdatedAt"].S)
                : DateTime.UtcNow
        }).ToList();

        return tickets;
    }

    public async Task<Ticket?> GetTicket(string eventId, string ticketId)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
                { "SK", new AttributeValue { S = $"TICKET#{ticketId}" } }
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        if (!response.IsItemSet) return null;

        var item = response.Item;

        return new Ticket
        {
            EventId = eventId,
            TicketId = ticketId,
            Status = item.TryGetValue("Status", out var status) ? status.S : "Unknown",
            UpdatedAt = item.TryGetValue("UpdatedAt", out var updated)
                ? DateTime.Parse(updated.S)
                : DateTime.UtcNow
        };
    }

    public async Task<List<string>> GetEventIds()
    {
        var request = new ScanRequest
        {
            TableName = TableName,
            ProjectionExpression = "PK"
        };
        var response = await _dynamoDb.ScanAsync(request);

        return response.Items
            .Select(i => i["PK"].S.Replace("EVENT#", ""))
            .Distinct()
            .ToList();
    }
}
