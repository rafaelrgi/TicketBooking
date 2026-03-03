using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.SignalR;
using TicketBooking.Api.Hubs;
using TicketBooking.Domain.Interfaces;

namespace TicketBooking.Api;

public class TicketUpdateWorker : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IHubContext<TicketHub> _hubContext;
    private readonly ITicketRepository _ticketRepository;
    private const string QueueUrl = "http://localhost:4566/000000000000/TicketUpdatesQueue";

    public TicketUpdateWorker(IAmazonSQS sqs, IHubContext<TicketHub> hubContext, ITicketRepository ticketRepository)
    {
        _sqs = sqs ?? throw new ArgumentNullException(nameof(sqs));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _ticketRepository = ticketRepository;
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
        var ticketId = GetTicketIdFromJson(message.Body);

        if (! await IsTicketCancelled(eventId, ticketId))
            await _hubContext.Clients.All.SendAsync("TicketUpdated", eventId, stoppingToken);

        await _sqs.DeleteMessageAsync(QueueUrl, message.ReceiptHandle, stoppingToken);
    }

    private async Task<bool> IsTicketCancelled(string eventId, string ticketId)
    {
        var ticket = await _ticketRepository.GetTicket(eventId, ticketId);
        var result  = (ticket?.Status == "Cancelled");
        return result;
    }

    public static string GetEventIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var pk = doc.RootElement.GetProperty("PK").GetString() ?? "";
        return pk.Replace("EVENT#", "");
    }

    public static string GetTicketIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var pk = doc.RootElement.GetProperty("SK").GetString() ?? "";
        return pk.Replace("TICKET#", "");
    }
}
