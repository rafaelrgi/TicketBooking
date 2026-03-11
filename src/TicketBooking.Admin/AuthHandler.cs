using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace TicketBooking.Admin;

public class AuthHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthHandler> _logger;

    public AuthHandler(IHttpContextAccessor httpContextAccessor, ILogger<AuthHandler> logger)
    {
        _logger = logger;
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
            _logger.LogError("AuthHandler missing access_token: {request}", request.RequestUri);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
