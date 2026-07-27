using System.Security.Claims;
using HRDashboard.Models;
using HRDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

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

    private static async Task<IResult> LoginAsync(LoginRequest request,HttpContext context,UserAccountService accounts,CancellationToken ct)
    {
        var loginId=request.UserName?.Trim()??"";
        var user=await accounts.AuthenticateAsync(loginId,request.Password??"",ct);
        if(user==null)
        {
            await Task.Delay(Random.Shared.Next(150,350),ct);
            return Results.Json(new{message="아이디 또는 비밀번호가 올바르지 않습니다."},statusCode:401);
        }
        await SignInAsync(context,user);
        return Results.Ok(new{userName=user.LoginId,role=user.Role,isAdministrator=user.Role=="Administrator",theme=user.Theme});
    }

    public static async Task SignInAsync(HttpContext context,ApplicationUser user)
    {
        var claims=new[]
        {
            new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),
            new Claim(ClaimTypes.Name,user.LoginId),
            new Claim(ClaimTypes.Role,user.Role),
            new Claim("theme",user.Theme),
            new Claim("securityStamp",user.UpdatedAtUtc.ToUnixTimeMilliseconds().ToString())
        };
        var principal=new ClaimsPrincipal(new ClaimsIdentity(claims,CookieAuthenticationDefaults.AuthenticationScheme));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,principal,
            new AuthenticationProperties{IsPersistent=false});
    }
}

public sealed record LoginRequest(string? UserName,string? Password);
