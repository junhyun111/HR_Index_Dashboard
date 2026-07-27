using HRDashboard.Services;

namespace HRDashboard.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/settings/employee-columns");
        group.MapGet("", async (EmployeeColumnSettingsService settings,CancellationToken ct)
            =>Results.Ok(await settings.GetAsync(ct))).RequireAuthorization("DashboardViewer");
        group.MapPut("", Update).RequireAuthorization("Administrator");
        group.MapPost("/reset", async (EmployeeColumnSettingsService settings,CancellationToken ct)
            =>Results.Ok(await settings.ResetAsync(ct))).RequireAuthorization("Administrator");
        return endpoints;
    }

    private static async Task<IResult> Update(EmployeeColumnSettingUpdate[] request,EmployeeColumnSettingsService settings,CancellationToken ct)
    {
        try{return Results.Ok(await settings.UpdateAsync(request,ct));}
        catch(ArgumentException e){return Results.BadRequest(new{message=e.Message});}
    }
}
