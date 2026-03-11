using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Api;

public class TicketUpdateWorker : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IHubContext<TicketHub> _hubContext;
    private readonly ITicketCacheService _cache;

    private readonly IServiceScopeFactory _scopeFactory;

    //private const string QueueUrl = "http://localhost:4566/000000000000/TicketUpdatesQueue";
    private readonly string _queueUrl;

    public TicketUpdateWorker(IAmazonSQS sqs, IHubContext<TicketHub> hubContext, ITicketCacheService cache,
        IServiceScopeFactory scopeFactory, IOptions<SettingsUrls> settingsUrls)
    {
        _sqs = sqs ?? throw new ArgumentNullException(nameof(sqs));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _cache = cache;
        _scopeFactory = scopeFactory;
        _queueUrl = settingsUrls.Value.TicketUpdatesQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($">>> Worker Started at: {DateTimeOffset.Now}");
        while (!stoppingToken.IsCancellationRequested)
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 1,
                WaitTimeSeconds = 20
            };

            try
            {
                var response = await _sqs.ReceiveMessageAsync(request, CancellationToken.None);
                if (response?.Messages != null && response.Messages.Count > 0)
                {
                    foreach (var message in response!.Messages!)
                    {
                        Console.WriteLine($">>> {message.Body}");
                        await ProcessMessage(message, stoppingToken);
                    }
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
        Console.WriteLine($">>> Worker processing message: {message.Body}");
        var eventId = GetEventIdFromJson(message.Body);
        var ticketId = GetTicketIdFromJson(message.Body);

        await _cache.InvalidateEventCache(eventId);
        using var scope = _scopeFactory.CreateScope();
        var ticketRepository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        await _hubContext.Clients.All.SendAsync("TicketUpdated", eventId, stoppingToken);
        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
    }

    public static string GetEventIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var pk = doc.RootElement.GetProperty("PK").GetString() ?? "";
        return pk.Replace("EVENT#", "");
    }

    //TODO: int?
    public static string GetTicketIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sk = doc.RootElement.GetProperty("SK").GetString() ?? "";
        return sk.Replace("TICKET#", "");
    }
}
