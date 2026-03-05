using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using TicketBooking.Api.Hubs;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Api.Endpoints;

public record ReservationRequest(string EventId, string TicketId, string UserId);

public record ConfirmationRequest(string EventId, string TicketId, string UserId);

public static class TicketApi
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tickets/reserve", ReserveTicket);
        app.MapPost("/api/tickets/confirm", ConfirmTicket);
        app.MapGet("/api/tickets/{eventId}", GetTickets);

        app.MapGet("/api/events", GetEventIds);
    }

    private static async Task<IResult> GetEventIds(ITicketRepository repository)
    {
        var eventIds = await repository.GetEventIds();
        return Results.Ok(eventIds);
    }

    private static async Task<IResult> GetTickets(string eventId, ITicketRepository repository, ITicketCacheService cache)
    {
        Console.WriteLine($">>> GetTickets: {eventId}");
        // cache
        var cachedData = await cache.GetEventCache(eventId);
        if (!string.IsNullOrEmpty(cachedData))
            return Results.Ok(JsonSerializer.Deserialize<List<Ticket>>(cachedData));

        // db
        Console.WriteLine($">>> CacheMiss: GetTickets {eventId}");
        var tickets = await repository.GetTickets(eventId);
        await cache.SetEventCache(eventId, JsonSerializer.Serialize(tickets));
        return Results.Ok(tickets);
    }

    private static async Task<IResult> ConfirmTicket(ConfirmationRequest request,
        ITicketRepository repository, IHubContext<TicketHub>? hubContext, ITicketCacheService cache)
    {
        var ticket = new Ticket
        {
            EventId = request.EventId,
            TicketId = request.TicketId,
            UserId = request.UserId,
            Status = "Confirmed",
            UpdatedAt = DateTime.UtcNow
        };

        var success = await repository.ConfirmTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);

        if (hubContext != null)
        {
            await hubContext.Clients.All.SendAsync("TicketUpdated", ticket.EventId);
            Console.WriteLine(">>> Sinal enviado para o Hub");
        }

        return success
            ? Results.Ok(new { message = "Confirmation saved" })
            : Results.BadRequest("Error saving Confirmation");
    }

    private static async Task<IResult> ReserveTicket(ReservationRequest request,
        ITicketRepository repository, IAmazonStepFunctions stepFunctions,
        IHubContext<TicketHub>? hubContext, ITicketCacheService cache)
    {
        var ticket = new Ticket
        {
            EventId = request.EventId,
            TicketId = request.TicketId,
            UserId = request.UserId,
            Status = "Reserved",
            UpdatedAt = DateTime.UtcNow
        };

        var success = await repository.ReserveTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        var cancelFlow = await StartReservationFlow(stepFunctions, ticket);

        if (hubContext != null)
        {
            await hubContext.Clients.All.SendAsync("TicketUpdated", ticket.EventId);
            Console.WriteLine(">>> Sinal enviado para o Hub");
        }

        return success
            ? Results.Ok(new { message = "Reservation saved" })
            : Results.BadRequest("Error saving Reservation");
    }


    private static async Task<StartExecutionResponse> StartReservationFlow(IAmazonStepFunctions stepFunctions, Ticket ticket)
    {
        //TODO: reuse new JsonSerializerOptions { PropertyNamingPolicy = null })
        var startRequest = new StartExecutionRequest
        {
            StateMachineArn = "arn:aws:states:us-east-1:000000000000:stateMachine:TicketBookingWorkflow",
            Input = JsonSerializer.Serialize(new
            {
                PK = $"EVENT#{ticket.EventId}",
                SK = $"TICKET#{ticket.TicketId}",
                status = "Canceled"
            }, new JsonSerializerOptions { PropertyNamingPolicy = null })
        };

        return await stepFunctions.StartExecutionAsync(startRequest);
    }
}
