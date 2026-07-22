using System.Security.Claims;
using System.Text.Encodings.Web;
using HRDashboard.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HRDashboard.Authentication;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<AuthenticationSettings> settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var groups = settings.Value.Groups;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, $"DEV\\{Environment.UserName}"),
            new(ClaimTypes.Role, groups.DashboardViewer),
            new(ClaimTypes.Role, groups.SalaryViewer),
            new(ClaimTypes.Role, groups.Editor),
            new(ClaimTypes.Role, groups.Administrator)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
