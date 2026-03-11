using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Api.Workers;

public class TicketUpdateWorker : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IHubContext<TicketHub> _hubContext;
    private readonly ITicketCacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketUpdateWorker> _logger;

    private readonly string _queueUrl;

    public TicketUpdateWorker(IAmazonSQS sqs, IHubContext<TicketHub> hubContext, ITicketCacheService cache,
        IServiceScopeFactory scopeFactory, IOptions<SettingsUrls> settingsUrls, ILogger<TicketUpdateWorker> logger)
    {
        _sqs = sqs ?? throw new ArgumentNullException(nameof(sqs));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queueUrl = settingsUrls.Value.TicketUpdatesQueue;
        _logger.LogInformation("Starting Worker {queueUrl}", _queueUrl);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                        _logger.LogDebug("Message: {message}", message.Body);
                        await ProcessMessage(message, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Worker Error: {error}", ex.Message);
            }
        }
    }

    public async Task ProcessMessage(Message message, CancellationToken stoppingToken)
    {
        var eventId = GetEventIdFromJson(message.Body);
        //var ticketId = GetTicketIdFromJson(message.Body);

        await _cache.InvalidateEventCache(eventId);
        using var scope = _scopeFactory.CreateScope();
        var ticketRepository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        await NotifyHub("TicketUpdated", eventId, stoppingToken);
        await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
    }

    private async Task NotifyHub(string message, string eventId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("NotifyHub: {message} {eventId}", message, eventId);
        await _hubContext.Clients.All.SendAsync(message, eventId, stoppingToken);
    }

    public static string GetEventIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var pk = doc.RootElement.GetProperty("PK").GetString() ?? "";
        return pk.Replace("EVENT#", "");
    }

    public static int GetTicketIdFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sk = doc.RootElement.GetProperty("SK").GetString() ?? "";
        return int.TryParse(sk.Replace("TICKET#", ""), out var result)? result : 0;
    }
}
