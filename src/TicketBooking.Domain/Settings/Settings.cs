namespace TicketBooking.Domain.Settings;

public class SettingsUrls
{
    public const string SectionName = "Urls";

    public string Keycloak { get; set; } = string.Empty;
    public string ApiBase { get; set; } = string.Empty;
    public string EventsWebhook { get; set; } = string.Empty;
    public string SignIn { get; set; } = string.Empty;
    public string TicketHub { get; set; } = string.Empty;
    public string TicketUpdatesQueue { get; set; } = string.Empty;
    public string? RealmUrl { get; set; } = string.Empty;
    public string MetadataAddress { get; set; } = string.Empty;
    public string? Redis { get; set; } = string.Empty;
    public string[]? AllowedOrigins { get; set; } = [];
}

public class SettingsAws
{
    public const string SectionName = "Aws";

    public string? ServiceUrl { get; set; }
    public string? Region { get; set; }
    public string? TicketWorkflowArn { get; set; }
}

public class SettingsAuth
{
    public const string SectionName = "Auth";

    public string Realm { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string NameClaimType { get; set; } = string.Empty;
    public string RoleClaimType { get; set; } = string.Empty;
    public string? Authority { get; set; } = string.Empty;

    public KeycloakCredentials Credentials { get; set; } = new();
}

public class KeycloakCredentials
{
    public string Secret { get; set; } = string.Empty;
}
