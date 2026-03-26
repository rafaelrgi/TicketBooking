namespace TicketBooking.Application.Dtos;

public record EventMessageDto(
    string EventId
)
{
    public const string Message = "Event";
    public readonly DateTime Timestamp = DateTime.Now;
}
