using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Amazon.SQS.Model;
using Microsoft.AspNetCore.SignalR;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Application.Dtos;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Api.Workers;

public class TicketUpdateWorker : BackgroundService
{
    private readonly IServiceBus _serviceBus;
    private readonly IHubContext<TicketHub> _hubContext;
    private readonly ITicketCacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TicketUpdateWorker> _logger;

    public TicketUpdateWorker(IServiceBus serviceBus, IHubContext<TicketHub> hubContext, ITicketCacheService cache,
        IServiceScopeFactory scopeFactory, ILogger<TicketUpdateWorker> logger)
    {
        _serviceBus = serviceBus;
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _cache = cache;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _logger.LogInformation("Starting Worker {queueUrl}", _serviceBus.QueueUrl);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _serviceBus.Subscribe<TicketMessageDto>(async (message, cancelToken) =>
        {
            try
            {
                if (message.Message != "Ticket")
                    return false;
                return await ProcessMessage(message, cancelToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Worker Error: {error}", ex.Message);
                return false;
            }
        }, cancellationToken);
    }

    public async Task<bool> ProcessMessage(TicketMessageDto message, CancellationToken cancelToken)
    {
        _logger.LogDebug("Worker Message: {message}", message.ToString());

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ticketRepository = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
            await Task.WhenAll
            (
                _cache.InvalidateEventCache(message.EventId),
                NotifyHub("TicketUpdated", message.EventId, cancelToken)
            );
            NotifyTelemetry(message);

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError("Error processing message: {message} :: {error}", message.ToString(), e.Message);
            return false;
            //UNDONE: DLQ
        }
    }

    private void NotifyTelemetry(TicketMessageDto message)
    {
        Counter<long> counter = message.Status switch
        {
            TicketStatus.Reserved => TelemetryConfig.ReservedCounter,
            TicketStatus.Confirmed => TelemetryConfig.ConfirmedCounter,
            TicketStatus.Available => TelemetryConfig.CanceledCounter,
            _ => throw new ArgumentOutOfRangeException()
        };
        counter.Add(1, new TagList { { "event.name", message.EventId }, { "status", "success" } });
        /*
        TelemetryConfig.TicketMeter.CreateObservableGauge("sqs.queue.size",
            () => GetQueueSizeFromLocalStack()
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
