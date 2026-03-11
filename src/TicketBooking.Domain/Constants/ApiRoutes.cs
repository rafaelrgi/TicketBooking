namespace TicketBooking.Domain.Constants;

public static class ApiRoutes
{
    public static class Events
    {
        private const string Base = "/api/events";
        public const string GetEvent = $"{Base}/";
        public const string GetEvents = $"{Base}/";
        public const string GetStats = $"{Base}/stats/";
        public const string SaveEvent = $"{Base}/";
    }

    public static class Tickets
    {
        private const string Base = "/api/tickets";
        public const string GetTickets = $"{Base}/";
        public const string ReserveTicket = $"{Base}/reserve/";
        public const string ConfirmTicket = $"{Base}/confirm/";
    }
}
