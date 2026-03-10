using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace TicketBooking.Api.Infra;

public static class AuthExtensions
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddKeycloakWebApiAuthentication(config, options =>
        {
            //TODO: config?
            var realmUrl = "http://localhost:8080/realms/tickets";
            options.MetadataAddress = $"{realmUrl}/.well-known/openid-configuration";
            options.Authority = realmUrl;
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = realmUrl,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true
            };
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
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", policy =>
                policy.RequireAssertion(context =>
                {
                    var realmAccessClaim = context.User.FindFirst("realm_access");
                    if (realmAccessClaim == null) return false;
                    return realmAccessClaim.Value.Contains("\"Admin\"");
                }));
        });

        return services;
    }
}
