using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;

namespace TicketBooking.Infra.Repositories;

public class DynamoDbEventRepository : IEventRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private const string TableName = "Events";

    public DynamoDbEventRepository(IAmazonDynamoDB dynamoDb)
    {
        _dynamoDb = dynamoDb;
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

    public async Task<Event?> GetEvent(string eventId)
    {
        var request = new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{eventId}" } },
            }
        };

        var response = await _dynamoDb.GetItemAsync(request);
        if (!response.IsItemSet) return null;

        var item = response.Item;

        return new Event
        {
            EventId = eventId,
            TotalTickets = int.Parse(item["TotalTickets"].S),
        };
    }

    public async Task<EventStats?> GetDashboardStats(string eventId)
    {
        var totalTask = QueryCountTotal(eventId);
        var reservedTask =  QueryCountByStatus(eventId, "Reserved");
        var confirmedTask =  QueryCountByStatus(eventId, "Confirmed");
        await Task.WhenAll(totalTask, reservedTask, confirmedTask);

        int total = totalTask.Result;
        int reserved = reservedTask.Result;
        int confirmed = confirmedTask.Result;

        return new EventStats
        (
            eventId,
            total,
            confirmed,
            reserved,
            total - confirmed - reserved
        );
    }

    public async Task<bool> CreateEvent(Event evt)
    {
        var row = new Dictionary<string, AttributeValue>
        {
            { "PK", new AttributeValue { S = $"EVENT#{evt.EventId}" } },
            { "EventId", new AttributeValue { S = evt.EventId } },
            { "TotalTickets", new AttributeValue { S = evt.TotalTickets.ToString() } },
        };

        var response = await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = "Events",
            Item = row
        });

        return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
    }

    private async Task<int> QueryCountTotal(string eventId)
    {
        var request = new GetItemRequest
        {
            TableName = "Events",
            Key = new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue { S = $"EVENT#{eventId}" } }
            }
        };
        var response = await _dynamoDb.GetItemAsync(request);
        if (!response.IsItemSet) return 0;

        var item = response.Item;
        if (!item.TryGetValue("TotalTickets", out var attr))
            return 0;

        var literalValue = attr.N ?? attr.S;
        if (int.TryParse(literalValue, out int total))
            return total;
        return 0;
    }

    private async Task<int> QueryCountByStatus(string eventId, string status)
    {
        var countRequest = new QueryRequest
        {
            TableName = "Tickets",
            KeyConditionExpression = "PK = :pk",
            FilterExpression = "#s = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { { "#s", "Status" } },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                { ":pk", new AttributeValue { S = $"EVENT#{eventId}" } },
                { ":status", new AttributeValue { S = status } }
            },
            Select = Select.COUNT
        };
        var countResponse = await _dynamoDb.QueryAsync(countRequest);
        return countResponse.Count ?? 0;
    }
}
