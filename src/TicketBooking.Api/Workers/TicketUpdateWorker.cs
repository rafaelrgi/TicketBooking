using System.Diagnostics;
using System.Diagnostics.Metrics;
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

public enum TicketState
{
    Available,
    Reserved,
    Confirmed
}

//TODO: dto
public record QueueMessage(string EventId, int TicketId, TicketState State);

public static class QueueMessageMapper
{
    private record SqsContract(string PK, string SK, string Status);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static QueueMessage ToQueueMessage(this string jsonMsg)
    {
        var raw = JsonSerializer.Deserialize<SqsContract>(jsonMsg, Options);
        if (raw == null) throw new JsonException($"Invalid queue message: {jsonMsg}");

        return new QueueMessage(
            raw.PK.Replace("EVENT#", ""),
            int.Parse(raw.SK.Replace("TICKET#", "")),
            Enum.Parse<TicketState>(raw.Status)
        );
    }
}

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
        _logger.LogDebug("Worker Message: {message}", message.Body);

        try
        {
            var msg = message.Body.ToQueueMessage();

            using var scope = _scopeFactory.CreateScope();
            var ticketRepository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
            Task.WaitAll
            (
                _cache.InvalidateEventCache(msg.EventId),
                NotifyHub("TicketUpdated", msg.EventId, stoppingToken),
                _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken)
            );
            NotifyTelemetry(msg);
        }
        catch (Exception e)
        {
            _logger.LogError("Error processing message: {message} :: {error}", message.Body, e.Message);
            //UNDONE: DLQ
        }
    }

    private void NotifyTelemetry(QueueMessage msg)
    {
        Counter<long> counter = msg.State switch
        {
            TicketState.Reserved => TelemetryConfig.ReservedCounter,
            TicketState.Confirmed => TelemetryConfig.ConfirmedCounter,
            TicketState.Available => TelemetryConfig.CanceledCounter
        };
        counter.Add(1, new TagList { { "event.name", msg.EventId }, { "status", "success" } });
        /*
        TelemetryConfig.TicketMeter.CreateObservableGauge("sqs.queue.size",
            () => GetQueueSizeFromLocalStack(), // Função que consulta o LocalStack
            "Mensagens",
            "Quantidade de mensagens pendentes na fila");
        */
    }

    private async Task NotifyHub(string message, string eventId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("NotifyHub: {message} {eventId}", message, eventId);
        await _hubContext.Clients.All.SendAsync(message, eventId, stoppingToken);
    }

}
