namespace TicketBooking.Application.Interfaces;

public interface ITicketCacheService
{
    public Task<string?> GetEventCache(string eventId);
    public Task SetEventCache(string eventId, string data);
    public Task InvalidateEventCache(string eventId);
}
