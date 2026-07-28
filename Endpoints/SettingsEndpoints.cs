using System.Net.Mail;
using System.Security.Claims;
using HRDashboard.Data;
using HRDashboard.Models;
using HRDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var columns=endpoints.MapGroup("/api/settings/employee-columns");
        columns.MapGet("",async (EmployeeColumnSettingsService settings,CancellationToken ct)
            =>Results.Ok(await settings.GetAsync(ct))).RequireAuthorization("DashboardViewer");
        columns.MapPut("",UpdateColumns).RequireAuthorization("Administrator");
        columns.MapPost("/reset",async (EmployeeColumnSettingsService settings,CancellationToken ct)
            =>Results.Ok(await settings.ResetAsync(ct))).RequireAuthorization("Administrator");

        var settings=endpoints.MapGroup("/api/settings").RequireAuthorization("DashboardViewer");
        settings.MapPut("/profile",UpdateProfile);
        settings.MapPut("/theme",UpdateTheme);
        settings.MapGet("/accounts",ListAccounts).RequireAuthorization("Administrator");
        settings.MapPost("/accounts",CreateAccount).RequireAuthorization("Administrator");
        settings.MapPut("/accounts/{id:long}",UpdateAccount).RequireAuthorization("Administrator");
        settings.MapDelete("/accounts/{id:long}",DeleteAccount).RequireAuthorization("Administrator");
        settings.MapGet("/database-history",DatabaseHistory).RequireAuthorization("Administrator");
        return endpoints;
    }

    private static async Task<IResult> UpdateColumns(EmployeeColumnSettingUpdate[] request,EmployeeColumnSettingsService settings,CancellationToken ct)
    {
        try{return Results.Ok(await settings.UpdateAsync(request,ct));}
        catch(ArgumentException e){return Results.BadRequest(new{message=e.Message});}
    }

    private static async Task<IResult> UpdateProfile(ProfileUpdateRequest request,HttpContext context,CommonSettingsDbContext db,CancellationToken ct)
    {
        var user=await CurrentUser(context,db,ct);
        if(user==null)return Results.Unauthorized();
        if(!UserAccountService.VerifyPassword(request.CurrentPassword??"",user.PasswordHash))
            return Results.BadRequest(new{message="기존 비밀번호가 올바르지 않습니다."});
        var loginId=request.NewLoginId?.Trim()??"";
        var error=ValidateLoginId(loginId,user.Role);if(error!=null)return Results.BadRequest(new{message=error});
        if(string.IsNullOrEmpty(request.NewPassword)||request.NewPassword.Length<4)
            return Results.BadRequest(new{message="새 비밀번호는 4자 이상이어야 합니다."});
        if(await db.Users.AnyAsync(x=>x.Id!=user.Id&&x.LoginId==loginId,ct))
            return Results.Conflict(new{message="이미 사용 중인 아이디 또는 이메일입니다."});
        user.LoginId=loginId;
        user.PasswordHash=UserAccountService.HashPassword(request.NewPassword);
        user.UpdatedAtUtc=DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuthenticationEndpoints.SignInAsync(context,user);
        return Results.Ok(Account(user));
    }

    private static async Task<IResult> UpdateTheme(ThemeUpdateRequest request,HttpContext context,CommonSettingsDbContext db,CancellationToken ct)
    {
        var theme=request.Theme?.Trim().ToLowerInvariant();
        if(theme is not ("light" or "dark"))return Results.BadRequest(new{message="화면 모드를 선택해 주세요."});
        var user=await CurrentUser(context,db,ct);if(user==null)return Results.Unauthorized();
        user.Theme=theme;user.UpdatedAtUtc=DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuthenticationEndpoints.SignInAsync(context,user);
        return Results.Ok(new{theme});
    }

    private static async Task<IResult> ListAccounts(CommonSettingsDbContext db,CancellationToken ct)
        =>Results.Ok((await db.Users.AsNoTracking().OrderBy(x=>x.Role=="Administrator"?0:1).ThenBy(x=>x.LoginId).ToListAsync(ct)).Select(Account));

    private static async Task<IResult> CreateAccount(AccountCreateRequest request,CommonSettingsDbContext db,CancellationToken ct)
    {
        var role=NormalizeRole(request.Role);if(role==null)return Results.BadRequest(new{message="권한을 선택해 주세요."});
        var loginId=request.LoginId?.Trim()??"";var error=ValidateLoginId(loginId,role);if(error!=null)return Results.BadRequest(new{message=error});
        if(string.IsNullOrEmpty(request.Password)||request.Password.Length<4)return Results.BadRequest(new{message="비밀번호는 4자 이상이어야 합니다."});
        if(await db.Users.AnyAsync(x=>x.LoginId==loginId,ct))return Results.Conflict(new{message="이미 사용 중인 아이디 또는 이메일입니다."});
        var now=DateTimeOffset.UtcNow;
        var user=new ApplicationUser{LoginId=loginId,PasswordHash=UserAccountService.HashPassword(request.Password),Role=role,Theme="light",IsActive=true,CreatedAtUtc=now,UpdatedAtUtc=now};
        db.Users.Add(user);await db.SaveChangesAsync(ct);
        return Results.Created($"/api/settings/accounts/{user.Id}",Account(user));
    }

    private static async Task<IResult> UpdateAccount(long id,AccountUpdateRequest request,HttpContext context,CommonSettingsDbContext db,CancellationToken ct)
    {
        var currentId=CurrentUserId(context);if(currentId==id)return Results.BadRequest(new{message="본인 계정은 로그인 관리에서 기존 비밀번호를 확인한 뒤 변경해 주세요."});
        var user=await db.Users.FindAsync([id],ct);if(user==null)return Results.NotFound(new{message="계정을 찾을 수 없습니다."});
        var role=NormalizeRole(request.Role);if(role==null)return Results.BadRequest(new{message="권한을 선택해 주세요."});
        var loginId=request.LoginId?.Trim()??"";var error=ValidateLoginId(loginId,role);if(error!=null)return Results.BadRequest(new{message=error});
        if(await db.Users.AnyAsync(x=>x.Id!=id&&x.LoginId==loginId,ct))return Results.Conflict(new{message="이미 사용 중인 아이디 또는 이메일입니다."});
        if(user.Role=="Administrator"&&(role!="Administrator"||!request.IsActive)&&await db.Users.CountAsync(x=>x.Role=="Administrator"&&x.IsActive,ct)<=1)
            return Results.BadRequest(new{message="마지막 활성 관리자 계정의 권한을 변경하거나 비활성화할 수 없습니다."});
        if(!string.IsNullOrEmpty(request.NewPassword))
        {
            if(request.NewPassword.Length<4)return Results.BadRequest(new{message="새 비밀번호는 4자 이상이어야 합니다."});
            user.PasswordHash=UserAccountService.HashPassword(request.NewPassword);
        }
        user.LoginId=loginId;user.Role=role;user.IsActive=request.IsActive;user.UpdatedAtUtc=DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);return Results.Ok(Account(user));
    }

    private static async Task<IResult> DeleteAccount(long id,HttpContext context,CommonSettingsDbContext db,CancellationToken ct)
    {
        if(CurrentUserId(context)==id)return Results.BadRequest(new{message="현재 로그인한 본인 계정은 삭제할 수 없습니다."});
        var user=await db.Users.FindAsync([id],ct);if(user==null)return Results.NotFound(new{message="계정을 찾을 수 없습니다."});
        if(user.Role=="Administrator"&&await db.Users.CountAsync(x=>x.Role=="Administrator"&&x.IsActive,ct)<=1)
            return Results.BadRequest(new{message="마지막 관리자 계정은 삭제할 수 없습니다."});
        db.Users.Remove(user);await db.SaveChangesAsync(ct);return Results.NoContent();
    }

    private static async Task<IResult> DatabaseHistory(CommonSettingsDbContext db,CancellationToken ct)
        =>Results.Ok(await db.EmployeeDatabaseChanges.AsNoTracking().OrderByDescending(x=>x.Id).Take(100).ToListAsync(ct));

    private static async Task<ApplicationUser?> CurrentUser(HttpContext context,CommonSettingsDbContext db,CancellationToken ct)
    {
        var id=CurrentUserId(context);return id==null?null:await db.Users.FindAsync([id.Value],ct);
    }
    private static long? CurrentUserId(HttpContext context)=>long.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:null;
    private static object Account(ApplicationUser x)=>new{x.Id,x.LoginId,x.Role,x.Theme,x.IsActive,x.CreatedAtUtc,x.UpdatedAtUtc};
    private static string? NormalizeRole(string? role)=>role is "Administrator" or "HrAdministrator" or "User"?role:null;
    private static string? ValidateLoginId(string loginId,string role)
    {
        if(loginId.Length<3||loginId.Length>120)return "아이디 또는 이메일은 3~120자로 입력해 주세요.";
        if(role=="User")
        {
            try
            {
                var address=new MailAddress(loginId);
                if(!address.Address.Equals(loginId,StringComparison.OrdinalIgnoreCase)
                    ||!address.Host.Equals("innodep.com",StringComparison.OrdinalIgnoreCase))
                    return "일반 사용자 계정은 @innodep.com 이메일만 사용할 수 있습니다.";
            }
            catch(FormatException){return "일반 사용자 계정은 @innodep.com 이메일만 사용할 수 있습니다.";}
        }
        return null;
    }

    private sealed record ProfileUpdateRequest(string? CurrentPassword,string? NewLoginId,string? NewPassword);
    private sealed record ThemeUpdateRequest(string? Theme);
    private sealed record AccountCreateRequest(string? LoginId,string? Password,string? Role);
    private sealed record AccountUpdateRequest(string? LoginId,string? NewPassword,string? Role,bool IsActive);
}
