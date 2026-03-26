using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Options;
using TicketBooking.Application.Dtos;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Common;
using TicketBooking.Domain.Constants;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Api.Endpoints;

//TODO: Dto
public record ReservationRequest(string EventId, int TicketId, bool IsVip, string UserId);

//TODO: Dto
public record ConfirmationRequest(string EventId, int TicketId, string UserId);

public static class TicketApi
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.Tickets.ReserveTicket, ReserveTicket).RequireAuthorization();
        app.MapPost(ApiRoutes.Tickets.ConfirmTicket, ConfirmTicket).RequireAuthorization();

        app.MapGet($"{ApiRoutes.Tickets.GetTickets}{{eventId}}", GetTickets).RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> GetTickets(string eventId, HttpContext context, ITicketRepository repository,
        ITicketCacheService cache, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TicketApi");
        logger.LogDebug("GetTickets: {eventId}", eventId);
        //logger.LogDebug("GetTickets: {eventId} :: Token: {authHeader}", eventId, context.Request.Headers["Authorization"]);

        // cache
        var cachedData = await cache.GetEventCache(eventId);
        if (!string.IsNullOrEmpty(cachedData))
            return Results.Ok(JsonSerializer.Deserialize<List<Ticket>>(cachedData));

        // db
        logger.LogDebug("CacheMiss: GetTickets {eventId}", eventId);
        var tickets = await repository.GetTickets(eventId);
        if (tickets.Count == 0)
            return Results.NotFound();
        await cache.SetEventCache(eventId, JsonSerializer.Serialize(tickets));
        return Results.Ok(tickets);
    }

    private static async Task<IResult> ConfirmTicket(ConfirmationRequest request, ITicketRepository repository,
        ITicketCacheService cache, ILoggerFactory loggerFactory, IServiceBus serviceBus, IOptions<SettingsUrls> settingsUrls
    )
    {
        var logger = loggerFactory.CreateLogger("TicketApi");

        var ticket = await repository.GetTicket(request.EventId, request.TicketId);
        if (ticket is not { Status: TicketStatus.Reserved })
            return Results.NotFound("Ticket not found");

        ticket.Status = TicketStatus.Confirmed;
        ticket.UpdatedAt = DateTime.UtcNow;
        var success = await repository.ConfirmTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        await NotifyQueue(ticket, TicketStatus.Confirmed, serviceBus, logger);

        return success
            ? Results.Ok(new { message = "Confirmation saved" })
            : Results.BadRequest("Error saving Confirmation");
    }

    private static async Task<IResult> ReserveTicket(ReservationRequest request, ITicketRepository repository,
        IEventRepository eventRepository, IAmazonStepFunctions stepFunctions, ITicketCacheService cache,
        IServiceBus serviceBus, IOptions<SettingsAws> settingsAws, IOptions<SettingsUrls> settingsUrls,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TicketApi");

        var evt = await eventRepository.GetEvent(request.EventId);
        if (evt is null)
            return Results.BadRequest("Event invalid");
        if (request.TicketId >= evt.TotalTickets)
            return Results.BadRequest("Ticket invalid");

        var ticket = new Ticket
        {
            EventId = request.EventId,
            TicketId = request.TicketId,
            UserId = request.UserId,
            Status = TicketStatus.Reserved,
            IsVip = request.IsVip,
            UpdatedAt = DateTime.UtcNow
        };

        var success = await repository.ReserveTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        await NotifyQueue(ticket, TicketStatus.Reserved, serviceBus, logger);
        var cancelFlow = await StartReservationFlow(stepFunctions, ticket, settingsAws, logger);

        return success
            ? Results.Ok(new { message = "Reservation saved" })
            : Results.BadRequest("Error saving Reservation");
    }

    private static async Task NotifyQueue(Ticket ticket, TicketStatus status, IServiceBus serviceBus, ILogger logger)
    {
        var message = new TicketMessageDto
        (
            $"EVENT#{ticket.EventId}",
            $"TICKET#{ticket.TicketId}",
            status
        );
        await serviceBus.Publish(message);

        logger.LogDebug("NotifyQueue {ticket.EventId}.{ticket.TicketId} = {status}", ticket.EventId, ticket.TicketId,
            status);
    }

    private static async Task<StartExecutionResponse> StartReservationFlow(IAmazonStepFunctions stepFunctions, Ticket ticket,
        IOptions<SettingsAws> settingsAws, ILogger logger)
    {
        var startRequest = new StartExecutionRequest
        {
            StateMachineArn = settingsAws.Value.TicketWorkflowArn,
            Input = JsonSerializer.Serialize(new
            {
                PK = $"EVENT#{ticket.EventId}",
                SK = $"TICKET#{ticket.TicketId}",
                status = "Canceled"
            }, JsonDefaults.Options) ?? "{}"
        };
        logger.LogDebug("StartReservationFlow {arn}", startRequest.StateMachineArn);
        return await stepFunctions.StartExecutionAsync(startRequest);
    }
}
