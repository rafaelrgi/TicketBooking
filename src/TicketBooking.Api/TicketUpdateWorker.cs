using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.SignalR;
using TicketBooking.Api.Hubs;

namespace TicketBooking.Api;

public class TicketUpdateWorker : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IHubContext<TicketHub> _hubContext;
    private const string QueueUrl = "http://localhost:4566/000000000000/TicketUpdatesQueue";

    public TicketUpdateWorker(IAmazonSQS sqs, IHubContext<TicketHub> hubContext)
    {
        _sqs = sqs ?? throw new ArgumentNullException(nameof(sqs));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = QueueUrl,
                WaitTimeSeconds = 15,
                MaxNumberOfMessages = 1
            };

            try
            {
                var response = await _sqs.ReceiveMessageAsync(request, stoppingToken);
                if (response?.Messages != null && response.Messages.Count == 0) continue;

                foreach (var message in response!.Messages!)
                {
                    Console.WriteLine($">>> {message.Body}");
                    await ProcessMessage(message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> Worker Error: {ex.Message}");
            }
        }
    }

    public async Task ProcessMessage(Message message, CancellationToken stoppingToken)
    {
        var eventId = GetEventIdFromJson(message.Body);
        await _hubContext.Clients.All.SendAsync("TicketUpdated", eventId, stoppingToken);
        await _sqs.DeleteMessageAsync(QueueUrl, message.ReceiptHandle, stoppingToken);
    }

    public static string GetEventIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var pk = doc.RootElement.GetProperty("PK").GetString() ?? "";
        return pk.Replace("EVENT#", "");
    }
}