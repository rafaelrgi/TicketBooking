namespace TicketBooking.Domain.Entities;

public class Ticket
{
    public string EventId { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Status { get; set; } = "Available";
    public string Type { get; set; } = "VIP";
    public DateTime UpdatedAt { get; set; }
}