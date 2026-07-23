using HRDashboard.Data;
using HRDashboard.Models;
using HRDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");
        api.MapGet("/session", (HttpContext c) => Results.Ok(new { userName = c.User.Identity?.Name, canEdit = true, isAdministrator = true })).RequireAuthorization("DashboardViewer");
        api.MapGet("/dashboard", Dashboard).RequireAuthorization("DashboardViewer");
        api.MapGet("/employees/export", Export).RequireAuthorization("Editor");
        api.MapPost("/employees/import", Import).DisableAntiforgery().RequireAuthorization("Editor");
        api.MapPost("/employees/paste", Paste).RequireAuthorization("Editor");
        return endpoints;
    }

    private static async Task<IResult> Export(AppDbContext db, EmployeeCsvService csv, CancellationToken ct)
    {
        var rows = await db.Employees.AsNoTracking().OrderBy(x => x.EmployeeNumber).ToListAsync(ct);
        return Results.File(csv.Export(rows), "text/csv; charset=utf-8", $"hr-employees-{DateTime.Now:yyyy-MM-dd}.csv");
    }

    private static async Task<IResult> Import(IFormFile? file, AppDbContext db, EmployeeCsvService csv, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest(new { message = "업로드할 CSV 파일을 선택하세요." });
        if (file.Length > 10 * 1024 * 1024 || !string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "10MB 이하의 CSV 파일만 업로드할 수 있습니다." });
        try { await using var stream = file.OpenReadStream(); return await Apply(csv.Parse(stream), db, ct); }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Paste(EmployeePasteRequest request, AppDbContext db, EmployeeCsvService csv, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { message = "붙여넣은 표가 비어 있습니다." });
        try { return await Apply(csv.ParseClipboard(request.Text), db, ct); }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Apply(EmployeeImportResult import, AppDbContext db, CancellationToken ct)
    {
        var numbers = import.Rows.Select(x => x.EmployeeNumber).ToArray();
        var saved = await db.Employees.Where(x => numbers.Contains(x.EmployeeNumber)).ToListAsync(ct);
        var existing = saved.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);
        var added = 0; var updated = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var row in import.Rows)
        {
            if (!existing.TryGetValue(row.EmployeeNumber, out var x)) { x = new Employee { EmployeeNumber = row.EmployeeNumber }; db.Add(x); added++; }
            else updated++;
            x.Workplace=row.Workplace; x.ParentDepartment=row.ParentDepartment; x.Department=row.Department; x.Name=row.Name;
            x.Position=row.Position; x.WorkShift=row.WorkShift; x.Duty=row.Duty; x.JobGroup=row.JobGroup;
            x.EmploymentType=row.EmploymentType; x.Gender=row.Gender; x.HireDate=row.HireDate; x.TerminationDate=row.TerminationDate;
        }
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { added, updated, total = import.Rows.Count });
    }

    private static async Task<IResult> Dashboard(string? workplace, string? department, string? gender, string? search,
        string? sort, string? direction, int page, int pageSize, AppDbContext db, CancellationToken ct)
    {
        page=Math.Max(1,page); pageSize=Math.Clamp(pageSize==0?10:pageSize,1,100);
        var totalCount=await db.Employees.CountAsync(ct); var query=db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(workplace)) query=query.Where(x=>x.Workplace==workplace);
        if (!string.IsNullOrWhiteSpace(department)) query=query.Where(x=>x.Department==department);
        if (!string.IsNullOrWhiteSpace(gender)) query=query.Where(x=>x.Gender==gender);
        if (!string.IsNullOrWhiteSpace(search)) { var q=search.Trim(); query=query.Where(x=>x.EmployeeNumber.Contains(q)||(x.Name!=null&&x.Name.Contains(q))||(x.Department!=null&&x.Department.Contains(q))||(x.Duty!=null&&x.Duty.Contains(q))); }
        var rows=Sort(await query.ToListAsync(ct),sort,direction); var filteredCount=rows.Count;
        var pages=Math.Max(1,(int)Math.Ceiling(filteredCount/(double)pageSize)); page=Math.Min(page,pages); var today=DateTime.Today;
        CountResponse[] Counts(Func<Employee,string?> pick)=>rows.Select(pick).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label).ToArray();
        var genders=rows.Select(x=>x.Gender).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).ToDictionary(x=>x.Key,x=>x.Count());
        return Results.Ok(new {
            filters=new { workplaces=await Values(db.Employees.Select(x=>x.Workplace),ct), departments=await Values(db.Employees.Select(x=>x.Department),ct), genders=await Values(db.Employees.Select(x=>x.Gender),ct) },
            summary=new { totalCount,filteredCount,activeCount=rows.Count(x=>x.TerminationDate is null||x.TerminationDate>=today),hiresThisYear=rows.Count(x=>x.HireDate!=null&&x.HireDate.Value.Year==today.Year),terminationsThisYear=rows.Count(x=>x.TerminationDate!=null&&x.TerminationDate.Value.Year==today.Year&&x.TerminationDate.Value>=today) },
            departments=Counts(x=>x.Department).Where(x=>x.Value>1),genders,
            jobGroups=rows.Select(x=>NormalizeJobGroup(x.JobGroup)).Where(x=>x!=null).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label),
            employees=rows.Skip((page-1)*pageSize).Take(pageSize),pagination=new {page,pageSize,pages,totalCount=filteredCount}
        });
    }

    private static string? NormalizeJobGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed=value.Trim();
        return trimmed.StartsWith("계약직",StringComparison.OrdinalIgnoreCase) ? "계약직" : trimmed;
    }

    private static async Task<string[]> Values(IQueryable<string?> q,CancellationToken ct)=>await q.Where(x=>x!=null&&x!="").Select(x=>x!).Distinct().Order().ToArrayAsync(ct);
    private static List<Employee> Sort(List<Employee> rows,string? sort,string? direction)
    {
        Func<Employee,object?> key=sort switch { "employeeNumber"=>x=>x.EmployeeNumber,"workplace"=>x=>x.Workplace,"parentDepartment"=>x=>x.ParentDepartment,"department"=>x=>x.Department,"position"=>x=>x.Position,"workShift"=>x=>x.WorkShift,"duty"=>x=>x.Duty,"jobGroup"=>x=>x.JobGroup,"employmentType"=>x=>x.EmploymentType,"gender"=>x=>x.Gender,"hireDate"=>x=>x.HireDate,"terminationDate"=>x=>x.TerminationDate,_=>x=>x.Name };
        return (direction=="desc"?rows.OrderByDescending(key):rows.OrderBy(key)).ToList();
    }
    private sealed record CountResponse(string Label,int Value);
    private sealed record EmployeePasteRequest(string Text);
}
