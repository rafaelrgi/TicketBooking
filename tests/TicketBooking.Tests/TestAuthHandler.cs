using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TicketBooking.Tests.Integration;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) :
        base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey("X-Test-Expired"))
            return Task.FromResult(AuthenticateResult.Fail("Token expired"));

        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("Shall Not Pass!"));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Test User"),
            //new Claim("sub", "test-user-guid"),
            //new Claim("realm_access", "{\"roles\":[\"Admin\"]}")
        };
        if (Context.Request.Headers.TryGetValue("X-Role", out var role))
            claims.Add(new Claim("realm_access", "{\"roles\":[\"" + role.ToString() + "\"]}")); //ClaimTypes.Role, role.ToString())
        else
            claims.Add(new Claim("realm_access", "{\"roles\":[\"Admin\"]}")); //ClaimTypes.Role, "Admin"

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
