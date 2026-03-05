using Microsoft.Extensions.Caching.Distributed;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Infra.Caching;

public class TicketCacheService : ITicketCacheService
{
    private readonly IDistributedCache _cache;

    //TODO: Logs private readonly ILogger<TicketCacheService> _logger;
    private const string Prefix = "tickets:";
    private const int Seconds = 300;

    public TicketCacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task InvalidateEventCache(string eventId)
    {
        try
        {
            Console.WriteLine($">>> Clear cache: {eventId}");
            await _cache.RemoveAsync(GetKey(eventId));
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                ">>> Redis Offline (Invalidate)"); //TODO: _logger.LogWarning(ex, ">>> Redis Offline (Invalidate)");
        }
    }

    public async Task<string?> GetEventCache(string eventId)
    {
        try
        {
            return await _cache.GetStringAsync(GetKey(eventId));
        }
        catch (Exception ex)
        {
            Console.WriteLine(">>> Redis Offline (Read)"); //TODO: _logger.LogWarning(ex, ">>> Redis Offline (Read)");
            return null;
        }
    }

    public async Task SetEventCache(string eventId, string data)
    {
        try
        {
            Console.WriteLine($">>> Set cache: {eventId}");
            await _cache.SetStringAsync(GetKey(eventId), data,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Seconds) });
        }
        catch(Exception ex)
        {
            Console.WriteLine(">>> Redis Offline (Write)"); //TODO: _logger.LogWarning(ex, ">>> Redis Offline (Write)");
        }
    }

    private string GetKey(string eventId)
    {
        return $"{Prefix}{eventId}";
    }
}
