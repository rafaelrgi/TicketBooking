using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Admin.Infra;

public static class AuthExtensions
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        var settingsAuth = config.GetSection(SettingsAuth.SectionName).Get<SettingsAuth>();
        var settingsUrls = config.GetSection(SettingsUrls.SectionName).Get<SettingsUrls>();
        if (settingsUrls == null || settingsAuth == null)
            throw new ArgumentNullException(nameof(services));

#if DEBUG
        Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
#endif

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                options.Authority = settingsAuth.Authority;
                options.ClientId = settingsAuth.Resource;
                options.ClientSecret = settingsAuth.Credentials.Secret;
                options.RequireHttpsMetadata = false;
                options.CallbackPath = settingsUrls.SignIn;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("roles");
                options.SaveTokens = true;

                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    NameClaimType = settingsAuth.NameClaimType,
                    RoleClaimType = settingsAuth.RoleClaimType
                };
            });

        services.AddHttpContextAccessor();
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();

        services.AddTransient<AuthHandler>();

        services.AddHttpClient("API", client =>
            {
                client.BaseAddress = new Uri(settingsUrls.ApiBase);
            })
            .AddHttpMessageHandler<AuthHandler>();

        services.AddHeaderPropagation(options => { options.Headers.Add("Authorization"); });

        return services;
    }
}
