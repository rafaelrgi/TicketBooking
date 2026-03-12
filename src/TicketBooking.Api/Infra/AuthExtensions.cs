using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TicketBooking.Domain.Constants;
using TicketBooking.Domain.Settings;

namespace TicketBooking.Api.Infra;

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
        services.AddKeycloakWebApiAuthentication(config, options =>
        {
            options.MetadataAddress = settingsUrls.MetadataAddress;
            options.Authority = settingsUrls.RealmUrl;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settingsUrls.RealmUrl,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true
            };
/*
#if DEBUG
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Console.WriteLine("FALHA NA AUTENTICAÇÃO: " + context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    Console.WriteLine("TOKEN VALIDADO COM SUCESSO!");
                    return Task.CompletedTask;
                },
                OnForbidden = context =>
                {
                    Console.WriteLine("TOKEN VÁLIDO, MAS SEM PERMISSÃO (ROLE)");
                    return Task.CompletedTask;
                }
            };
#endif
*/
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthConstants.AdminPolicy, policy =>
                policy.RequireAssertion(context =>
                {
                    var realmAccessClaim = context.User.FindFirst("realm_access");
                    if (realmAccessClaim == null) return false;
                    return realmAccessClaim.Value.Contains('"' + AuthConstants.AdminRole + '"');
                }));
        });

        return services;
    }
}
