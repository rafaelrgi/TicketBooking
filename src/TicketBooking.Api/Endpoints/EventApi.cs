using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Api.Endpoints;

//TODO: Dto
public record CreateRequest(string EventId, int TotalTickets);

public static class EventApi
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", GetEventIds);
        app.MapGet("/api/events/{eventId}", GetEvent);
        app.MapGet("/api/events/stats/{eventId}", GetStats);

        app.MapPut("/api/events", SaveEvent);
    }

    private static async Task<IResult> GetEvent(string eventId, IEventRepository repository)
    {
        var row =  await repository.GetEvent(eventId);
        return (row == null) ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> SaveEvent(CreateRequest request, IEventRepository repository)
    {
        var evt = new Event
        {
            EventId = request.EventId,
            TotalTickets = request.TotalTickets
        };
        var success = await repository.CreateEvent(evt);
        return success ? Results.NoContent() : Results.BadRequest(request.EventId);
    }

    private static async Task<IResult> GetEventIds(IEventRepository repository)
    {
        //TODO: cache
        var eventIds = await repository.GetEventIds();
        return Results.Ok(eventIds);
    }

    private static async Task<IResult> GetStats(string eventId, IEventRepository repository)
    {
        // db
        var stats = await repository.GetDashboardStats(eventId);
        return Results.Ok(stats);
    }
}
