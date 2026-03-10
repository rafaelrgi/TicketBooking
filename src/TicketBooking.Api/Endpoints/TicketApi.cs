using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Api.Endpoints;

//TODO: Dto
public record ReservationRequest(string EventId, int TicketId, bool IsVip, string UserId);

//TODO: Dto
public record ConfirmationRequest(string EventId, int TicketId, string UserId);

public static class TicketApi
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/tickets");

        api.MapPost("/reserve", ReserveTicket).RequireAuthorization();
        api.MapPost("/confirm", ConfirmTicket).RequireAuthorization();
        api.MapGet("/{eventId}", GetTickets).RequireAuthorization("RequireAdmin");

        return app;
    }

    private static async Task<IResult> GetTickets(string eventId, HttpContext context, ITicketRepository repository, ITicketCacheService cache)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        Console.WriteLine($"Token recebido pela API: {authHeader}");

        Console.WriteLine($">>> GetTickets: {eventId}");
        // cache
        var cachedData = await cache.GetEventCache(eventId);
        if (!string.IsNullOrEmpty(cachedData))
            return Results.Ok(JsonSerializer.Deserialize<List<Ticket>>(cachedData));

        // db
        Console.WriteLine($">>> CacheMiss: GetTickets {eventId}");
        var tickets = await repository.GetTickets(eventId);
        if (tickets.Count == 0)
            return Results.NotFound();
        await cache.SetEventCache(eventId, JsonSerializer.Serialize(tickets));
        return Results.Ok(tickets);
    }

    private static async Task<IResult> ConfirmTicket(ConfirmationRequest request,
        ITicketRepository repository, IAmazonSQS queue, ITicketCacheService cache)
    {
        var ticket = await repository.GetTicket(request.EventId, request.TicketId);
        if (ticket is not { Status: "Reserved" })
            return Results.NotFound("Ticket not found");

        ticket.Status = "Confirmed";
        ticket.UpdatedAt = DateTime.UtcNow;
        var success = await repository.ConfirmTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        await NotifyQueue(ticket, "Confirmed", queue);

        return success
            ? Results.Ok(new { message = "Confirmation saved" })
            : Results.BadRequest("Error saving Confirmation");
    }

    private static async Task<IResult> ReserveTicket(ReservationRequest request,
        ITicketRepository repository, IEventRepository eventRepository,
        IAmazonStepFunctions stepFunctions, IAmazonSQS queue, ITicketCacheService cache)
    {
        var evt = await eventRepository.GetEvent(request.EventId);
        if (evt is null)
            return Results.BadRequest("Event invalid");
        if (request.TicketId >= evt.TotalTickets)
            return Results.BadRequest("Ticket invalid");

        Console.WriteLine("----------------------------------------");
        var ticket = new Ticket
        {
            EventId = request.EventId,
            TicketId = request.TicketId,
            UserId = request.UserId,
            Status = "Reserved",
            IsVip = request.IsVip,
            UpdatedAt = DateTime.UtcNow
        };

        var success = await repository.ReserveTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        await NotifyQueue(ticket, "Reserved", queue);
        var cancelFlow = await StartReservationFlow(stepFunctions, ticket);

        return success
            ? Results.Ok(new { message = "Reservation saved" })
            : Results.BadRequest("Error saving Reservation");
    }

    private static async Task NotifyQueue(Ticket ticket, string status, IAmazonSQS queue)
    {
        //TODO: usar IAmazonSQS para buscar a URL pelo nome? config?
        const string queueUrl = "http://localhost:4566/000000000000/TicketUpdatesQueue";
        var message = new
        {
            PK = $"EVENT#{ticket.EventId}",
            SK = $"TICKET#{ticket.TicketId}",
            Status = status
        };

        await queue.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = JsonSerializer.Serialize(message)
        });

        Console.WriteLine($">>> NotifyQueue: {ticket.EventId}.{ticket.TicketId} = {status}");
    }


    private static async Task<StartExecutionResponse> StartReservationFlow(IAmazonStepFunctions stepFunctions, Ticket ticket)
    {
        //TODO: reuse new JsonSerializerOptions { PropertyNamingPolicy = null })
        var startRequest = new StartExecutionRequest
        {
            //TODO: cfg?
            StateMachineArn = "arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow",
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
