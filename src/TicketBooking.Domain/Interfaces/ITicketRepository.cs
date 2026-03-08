using TicketBooking.Domain.Entities;

namespace TicketBooking.Domain.Interfaces;

public interface ITicketRepository
{
    Task<bool> ReserveTicket(Ticket ticket);
    Task<bool> ConfirmTicket(Ticket ticket);
    Task<List<Ticket>> GetTickets(string eventId);
    Task<Ticket?> GetTicket(string eventId, int ticketId);
}
