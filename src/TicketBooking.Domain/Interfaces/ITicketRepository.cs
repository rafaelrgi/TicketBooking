using TicketBooking.Domain.Entities;

namespace TicketBooking.Domain.Interfaces;

public interface ITicketRepository
{
    Task<bool> ReserveTicketAsync(Ticket ticket);
    Task<List<Ticket>> GetTickets(string eventId);
    Task<Ticket?> GetTicket(string eventId, string ticketId);
    Task<List<string>> GetEventIds();
}
