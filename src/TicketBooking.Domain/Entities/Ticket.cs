namespace TicketBooking.Domain.Entities;

public class Ticket
{
    public string EventId { get; set; } = string.Empty;
    public int TicketId { get; set; }
    public string? UserId { get; set; }
    public string Status { get; set; } = "Available";
    public bool IsVip { get; set; }
    public DateTime UpdatedAt { get; set; }
}
