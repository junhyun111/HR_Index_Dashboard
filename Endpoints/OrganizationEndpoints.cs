using HRDashboard.Services;

namespace HRDashboard.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var organization=endpoints.MapGroup("/api/organization");
        organization.MapGet("",async (OrganizationService service,CancellationToken ct)
            =>Results.Ok(await service.GetAsync(ct))).RequireAuthorization("DashboardViewer");
        organization.MapPut("",Save).RequireAuthorization("Editor");
        return endpoints;
    }

    private static async Task<IResult> Save(
        OrganizationSaveRequest request,HttpContext context,OrganizationService service,CancellationToken ct)
    {
        try
        {
            return Results.Ok(await service.SaveAsync(
                request.Items,context.User.Identity?.Name??"알 수 없음",ct));
        }
        catch(ArgumentException e){return Results.BadRequest(new{message=e.Message});}
    }

    private sealed record OrganizationSaveRequest(IReadOnlyCollection<OrganizationNodeResponse>? Items);
}
