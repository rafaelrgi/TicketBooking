namespace TicketBooking.Domain.Entities;

public enum TicketStatus
{
    Reserved,
    Confirmed,
    Available
}

public class Ticket
{
    public string EventId { get; set; } = string.Empty;
    public int TicketId { get; set; }
    public string? UserId { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Available;
    public bool IsVip { get; set; }
    public DateTime UpdatedAt { get; set; }
}
