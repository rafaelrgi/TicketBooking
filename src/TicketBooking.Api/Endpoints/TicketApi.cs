using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Options;
using TicketBooking.Domain.Entities;
using TicketBooking.Domain.Interfaces;
using TicketBooking.Application.Interfaces;
using TicketBooking.Domain.Constants;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Api.Endpoints;

//TODO: Dto
public record ReservationRequest(string EventId, int TicketId, bool IsVip, string UserId);

//TODO: Dto
public record ConfirmationRequest(string EventId, int TicketId, string UserId);

public static class TicketApi
{
    private static readonly JsonSerializerOptions JsonSerializerOpt = new JsonSerializerOptions{ PropertyNamingPolicy = null };

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.Tickets.ReserveTicket, ReserveTicket).RequireAuthorization();
        app.MapPost(ApiRoutes.Tickets.ConfirmTicket, ConfirmTicket).RequireAuthorization();

        app.MapGet($"{ApiRoutes.Tickets.GetTickets}{{eventId}}", GetTickets).RequireAuthorization(AuthConstants.AdminPolicy);

        return app;
    }

    private static async Task<IResult> GetTickets(string eventId, HttpContext context, ITicketRepository repository,
        ITicketCacheService cache)
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

    private static async Task<IResult> ConfirmTicket(ConfirmationRequest request, IOptions<SettingsUrls> settingsUrls,
        IAmazonSQS queue, ITicketRepository repository, ITicketCacheService cache)
    {
        var ticket = await repository.GetTicket(request.EventId, request.TicketId);
        if (ticket is not { Status: "Reserved" })
            return Results.NotFound("Ticket not found");

        ticket.Status = "Confirmed";
        ticket.UpdatedAt = DateTime.UtcNow;
        var success = await repository.ConfirmTicket(ticket);
        await cache.InvalidateEventCache(request.EventId);
        await NotifyQueue(ticket, "Confirmed", queue, settingsUrls);

        return success
            ? Results.Ok(new { message = "Confirmation saved" })
            : Results.BadRequest("Error saving Confirmation");
    }

    private static async Task<IResult> ReserveTicket(ReservationRequest request,
        ITicketRepository repository, IEventRepository eventRepository,
        IAmazonStepFunctions stepFunctions, IAmazonSQS queue, ITicketCacheService cache,
        IOptions<SettingsAws> settingsAws, IOptions<SettingsUrls> settingsUrls)
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
        await NotifyQueue(ticket, "Reserved", queue, settingsUrls);
        var cancelFlow = await StartReservationFlow(stepFunctions, ticket, settingsAws);

        return success
            ? Results.Ok(new { message = "Reservation saved" })
            : Results.BadRequest("Error saving Reservation");
    }

    private static async Task NotifyQueue(Ticket ticket, string status, IAmazonSQS queue, IOptions<SettingsUrls> settingsUrls)
    {
        var message = new
        {
            PK = $"EVENT#{ticket.EventId}",
            SK = $"TICKET#{ticket.TicketId}",
            Status = status
        };

        await queue.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = settingsUrls.Value.TicketUpdatesQueue,
            MessageBody = JsonSerializer.Serialize(message)
        });
        Console.WriteLine($">>> NotifyQueue: {ticket.EventId}.{ticket.TicketId} = {status}");
    }


    private static async Task<StartExecutionResponse> StartReservationFlow(IAmazonStepFunctions stepFunctions, Ticket ticket,
        IOptions<SettingsAws> settingsAws)
    {
        var startRequest = new StartExecutionRequest
        {
            StateMachineArn = settingsAws.Value.TicketWorkflowArn,
            Input = JsonSerializer.Serialize(new
            {
                PK = $"EVENT#{ticket.EventId}",
                SK = $"TICKET#{ticket.TicketId}",
                status = "Canceled"
            }, JsonSerializerOpt) ?? "{}"
        };
        return await stepFunctions.StartExecutionAsync(startRequest);
    }
}
