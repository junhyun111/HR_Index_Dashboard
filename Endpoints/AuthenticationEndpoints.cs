using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HRDashboard.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HRDashboard.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");
        endpoints.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext context, IOptions<LoginSettings> options)
    {
        var settings = options.Value;
        if (!FixedTimeEquals(request.UserName, settings.UserName) ||
            !FixedTimeEquals(request.Password, settings.Password))
        {
            await Task.Delay(Random.Shared.Next(150, 350), context.RequestAborted);
            return Results.Json(new { message = "아이디 또는 비밀번호가 올바르지 않습니다." }, statusCode: 401);
        }

        var claims = new[] { new Claim(ClaimTypes.Name, settings.UserName) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
        return Results.Ok(new { userName = settings.UserName });
    }

    private static bool FixedTimeEquals(string? supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied ?? "");
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}

public sealed record LoginRequest(string? UserName, string? Password);
