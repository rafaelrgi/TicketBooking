using System.Text.Json.Serialization;
using TicketBooking.Domain.Entities;

namespace TicketBooking.Application.Dtos;

public record TicketMessageDto(
    string PK,
    string SK,
    TicketStatus Status
)
{
    [JsonIgnore]
    public string EventId => PK.Replace("EVENT#", "");

    [JsonIgnore]
    public int TicketId => int.TryParse(SK.Replace("TICKET#", ""),  out var ticketId) ? ticketId : 0;

    public string Message { get; init; } = "Ticket";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
