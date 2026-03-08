using TicketBooking.Domain.Entities;

namespace TicketBooking.Domain.Interfaces;

public interface IEventRepository
{
    Task<List<string>> GetEventIds();
    Task<Event?> GetEvent(string eventId);
    Task<EventStats?> GetDashboardStats(string eventId);
    Task<bool> CreateEvent(Event evt);
}
