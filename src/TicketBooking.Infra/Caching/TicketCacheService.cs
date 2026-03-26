using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TicketBooking.Application.Interfaces;

namespace TicketBooking.Infra.Caching;

public class TicketCacheService : ITicketCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<TicketCacheService> _logger;

    private const string Prefix = "tickets:";
    private const int Seconds = 300;

    public TicketCacheService(IDistributedCache cache, ILogger<TicketCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task InvalidateEventCache(string eventId)
    {
        try
        {
            _logger.LogDebug("Clear cache {eventId}", eventId);
            await _cache.RemoveAsync(GetKey(eventId));
        }
        catch (Exception ex)
        {
            _logger.LogError("Redis Offline: {msg}", ex.Message);
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
            _logger.LogError("Redis Offline (Read): {msg}", ex.Message);
            return null;
        }
    }

    public async Task SetEventCache(string eventId, string data)
    {
        try
        {
            _logger.LogDebug("Set cache {eventId}", eventId);
            await _cache.SetStringAsync(GetKey(eventId), data,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Seconds) });
        }
        catch(Exception ex)
        {
            _logger.LogError("Redis Offline: {msg}", ex.Message);
        }
    }

    private static string GetKey(string eventId)
    {
        return $"{Prefix}{eventId}";
    }
}
