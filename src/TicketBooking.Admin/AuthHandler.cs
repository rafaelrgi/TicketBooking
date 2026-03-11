using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace TicketBooking.Admin;

public class AuthHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var accessToken = await httpContext?.GetTokenAsync("access_token")!;
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else
        {
            Console.WriteLine("DEBUG: AuthHandler não encontrou o token!");
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
