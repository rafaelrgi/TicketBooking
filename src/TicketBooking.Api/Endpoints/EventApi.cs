using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Constants;

namespace TicketBooking.Api.Endpoints;

//TODO: Dto
public record CreateRequest(string EventId, int TotalTickets);

public static class EventApi
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Events.GetEvents, GetEventIds).RequireAuthorization();
        app.MapGet($"{ApiRoutes.Events.GetEvent}{{eventId}}", GetEvent).RequireAuthorization();
        app.MapGet($"{ApiRoutes.Events.GetStats}{{eventId}}", GetStats).RequireAuthorization();

        app.MapPut(ApiRoutes.Events.SaveEvent, SaveEvent).RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> GetEvent(string eventId, IEventRepository repository, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("EventApi");
        logger.LogDebug(">>> GetEvent: {eventId}", eventId);

        var row =  await repository.GetEvent(eventId);
        return (row == null) ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> SaveEvent(CreateRequest request, IEventRepository repository, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("EventApi");
        logger.LogDebug(">>> SaveEvent: {event}", request);

        var evt = new Event
        {
            EventId = request.EventId,
            TotalTickets = request.TotalTickets
        };
        var success = await repository.CreateEvent(evt);
        return success ? Results.NoContent() : Results.BadRequest(request.EventId);
    }

    private static async Task<IResult> GetEventIds(IEventRepository repository, ILoggerFactory loggerFactory)
    {
        //var logger = loggerFactory.CreateLogger("EventApi");

        //TODO: cache
        var eventIds = await repository.GetEventIds();
        return Results.Ok(eventIds);
    }

    private static async Task<IResult> GetStats(string eventId, IEventRepository repository, ILoggerFactory loggerFactory)
    {
        //var logger = loggerFactory.CreateLogger("EventApi");

        // db
        var stats = await repository.GetDashboardStats(eventId);
        return Results.Ok(stats);
    }
}
