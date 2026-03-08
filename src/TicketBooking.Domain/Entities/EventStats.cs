namespace TicketBooking.Domain.Entities;

public record EventStats
(
    string Event,
    int Total,
    int Confirmed,
    int Reserved,
    int Available
);
