using System.Diagnostics.Metrics;

public static class TelemetryConfig
{
   // public static readonly ActivitySource ActivitySource = new("TicketBooking.Telemetry");

    public static readonly Meter TicketMeter = new("TicketBooking.Metrics", "1.0.0");

    public static readonly Counter<long> ReservedCounter = TicketMeter.CreateCounter<long>("tickets.reserved.count", "Reserved", "Reserved tickets");
    public static readonly Counter<long> ConfirmedCounter = TicketMeter.CreateCounter<long>("tickets.confirmed.count", "Confirmed", "Confirmed tickets");
    public static readonly Counter<long> CanceledCounter = TicketMeter.CreateCounter<long>("tickets.canceled.count", "Canceled", "Canceled tickets");
}
