using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TicketBooking.Admin.Infra;

public static class AuthExtensions
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(options =>
            {
                var keycloakConfig = config.GetSection("Keycloak");
                options.Authority = $"{keycloakConfig["auth-server-url"]}realms/{keycloakConfig["realm"]}";
                options.ClientId = keycloakConfig["resource"];
                options.ClientSecret = keycloakConfig["credentials:secret"];
                options.RequireHttpsMetadata = false;
                options.CallbackPath = "/signin-oidc";
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("roles");
                options.SaveTokens = true;

                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role"
                };
            });

        services.AddHttpContextAccessor();
        services.AddCascadingAuthenticationState();
        services.AddAuthorization();

        services.AddTransient<AuthHandler>();

        services.AddHttpClient("API", client =>
            {
                //TODO: config?
                client.BaseAddress = new Uri("http://localhost:5070/");
            })
            .AddHttpMessageHandler<AuthHandler>();

        services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("Authorization");
        });

        return services;
    }
}
