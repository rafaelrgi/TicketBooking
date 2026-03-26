namespace TicketBooking.Domain.Interfaces;

public interface IServiceBus
{
    public string QueueUrl { get; }
    Task Publish<T>(T message, CancellationToken ct = default) where T : class;
    Task Subscribe<T>(Func<T, CancellationToken, Task<bool>> handler, CancellationToken cancelToken) where T : class;
}
