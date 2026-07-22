using HRDashboard.Data;
using HRDashboard.Models;
using HRDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Endpoints;

public static class DashboardEndpoints
{
    private static readonly string[] GradeOrder = ["사원", "주임", "대리", "과장", "차장", "부장", "임원"];

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/session", (HttpContext context) => Results.Ok(new
            {
                userName = context.User.Identity?.Name,
                canViewSalary = true,
                canEdit = true,
                isAdministrator = true
            })).RequireAuthorization("DashboardViewer");

        api.MapGet("/dashboard", GetDashboardAsync)
            .RequireAuthorization("DashboardViewer");

        api.MapGet("/employees/export", ExportEmployeesAsync)
            .RequireAuthorization("Editor");
        api.MapPost("/employees/import", ImportEmployeesAsync)
            .DisableAntiforgery()
            .RequireAuthorization("Editor");
        api.MapPost("/employees/paste", PasteEmployeesAsync)
            .RequireAuthorization("Editor");

        api.MapGet("/integrations/status", async (
            ExternalApiClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await client.CheckAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return Results.Ok(new ExternalApiStatus(true, null, exception.Message));
            }
        }).RequireAuthorization("Administrator");

        return endpoints;
    }

    private static async Task<IResult> ExportEmployeesAsync(
        AppDbContext db, EmployeeCsvService csv, CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking().OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var bytes = csv.Export(employees, includeSalary: true);
        return Results.File(bytes, "text/csv; charset=utf-8", $"hr-employees-{DateTime.Now:yyyy-MM-dd}.csv");
    }

    private static async Task<IResult> ImportEmployeesAsync(
        IFormFile? file, AppDbContext db, EmployeeCsvService csv, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return Results.BadRequest(new { message = "업로드할 CSV 파일을 선택하세요." });
        if (file.Length > 10 * 1024 * 1024) return Results.BadRequest(new { message = "파일 크기는 10MB 이하여야 합니다." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = ".csv 형식만 업로드할 수 있습니다." });

        try
        {
            await using var stream = file.OpenReadStream();
            var import = csv.Parse(stream, canEditSalary: true);
            return await ApplyImportAsync(import, db, cancellationToken);
        }
        catch (EmployeeCsvException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or System.Text.DecoderFallbackException)
        {
            return Results.BadRequest(new { message = "올바른 UTF-8 CSV 파일을 읽을 수 없습니다." });
        }
    }

    private static async Task<IResult> PasteEmployeesAsync(
        EmployeePasteRequest request, AppDbContext db, EmployeeCsvService csv, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { message = "붙여넣은 표가 비어 있습니다." });
        if (request.Text.Length > 5_000_000) return Results.BadRequest(new { message = "붙여넣는 데이터는 5MB 이하여야 합니다." });
        try
        {
            var import = csv.ParseClipboard(request.Text, canEditSalary: true);
            return await ApplyImportAsync(import, db, cancellationToken);
        }
        catch (EmployeeCsvException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<IResult> ApplyImportAsync(
        EmployeeImportResult import, AppDbContext db, CancellationToken cancellationToken)
    {
        var ids = import.Rows.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        var existing = await db.Employees.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var unknownIds = ids.Where(id => !existing.ContainsKey(id)).ToArray();
        if (unknownIds.Length > 0)
            return Results.BadRequest(new { message = $"DB에 없는 직원 ID가 있습니다: {string.Join(", ", unknownIds.Take(20))}" });

        var added = 0;
        var updated = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var row in import.Rows)
        {
            Employee employee;
            if (row.Id.HasValue)
            {
                employee = existing[row.Id.Value];
                updated++;
            }
            else
            {
                employee = new Employee
                {
                    CompanyName = row.CompanyName, DepartmentName = row.DepartmentName, Name = row.Name,
                    Grade = row.Grade, Position = row.Position, Gender = row.Gender
                };
                db.Employees.Add(employee);
                added++;
            }
            employee.CompanyName = row.CompanyName;
            employee.DepartmentName = row.DepartmentName;
            employee.Name = row.Name;
            employee.Grade = row.Grade;
            employee.Position = row.Position;
            employee.Gender = row.Gender;
            employee.Age = row.Age;
            if (row.MonthlySalary.HasValue) employee.MonthlySalary = row.MonthlySalary.Value;
            employee.YearsOfService = row.YearsOfService;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { added, updated, total = import.Rows.Count });
    }

    private static async Task<IResult> GetDashboardAsync(
        string? department,
        string? grade,
        string? gender,
        string? search,
        string? sort,
        string? direction,
        int page,
        int pageSize,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize == 0 ? 10 : pageSize, 1, 100);
        var totalCount = await db.Employees.CountAsync(cancellationToken);

        var query = db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(department)) query = query.Where(x => x.DepartmentName == department);
        if (!string.IsNullOrWhiteSpace(grade)) query = query.Where(x => x.Grade == grade);
        if (!string.IsNullOrWhiteSpace(gender)) query = query.Where(x => x.Gender == gender);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.DepartmentName.Contains(term) ||
                                     x.Position.Contains(term) || x.Grade.Contains(term));
        }

        var rows = await query.ToListAsync(cancellationToken);
        rows = Sort(rows, sort, direction);
        var filteredCount = rows.Count;
        var pages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));
        page = Math.Min(page, pages);

        const bool canViewSalary = true;
        var pageRows = rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmployeeResponse(
                x.DepartmentName, x.Name, x.Grade, x.Position,
                x.Gender, x.Age, canViewSalary ? x.MonthlySalary : null, x.YearsOfService));

        var ages = rows.Select(x => x.Age).ToArray();
        var tenures = rows.Select(x => x.YearsOfService).ToArray();
        var salaries = rows.Select(x => x.MonthlySalary).Order().ToArray();
        var medianSalary = salaries.Length == 0 ? 0 : salaries.Length % 2 == 1
            ? salaries[salaries.Length / 2]
            : (salaries[salaries.Length / 2 - 1] + salaries[salaries.Length / 2]) / 2;

        var departments = rows.GroupBy(x => x.DepartmentName)
            .Select(x => new CountResponse(x.Key, x.Count()))
            .OrderByDescending(x => x.Value).ThenBy(x => x.Label).ToArray();
        var genders = rows.GroupBy(x => x.Gender)
            .ToDictionary(x => x.Key, x => x.Count());

        return Results.Ok(new
        {
            permissions = new { canViewSalary },
            filters = new
            {
                departments = await db.Employees.Select(x => x.DepartmentName).Distinct().Order().ToArrayAsync(cancellationToken),
                grades = (await db.Employees.Select(x => x.Grade).Distinct().ToArrayAsync(cancellationToken))
                    .OrderBy(x => Array.IndexOf(GradeOrder, x) is var index && index >= 0 ? index : int.MaxValue),
                genders = await db.Employees.Select(x => x.Gender).Distinct().Order().ToArrayAsync(cancellationToken)
            },
            summary = new
            {
                totalCount,
                filteredCount,
                averageAge = ages.Length == 0 ? (double?)null : Math.Round(ages.Average(), 1),
                minimumAge = ages.Length == 0 ? (int?)null : ages.Min(),
                maximumAge = ages.Length == 0 ? (int?)null : ages.Max(),
                averageSalary = canViewSalary && salaries.Length > 0 ? (double?)Math.Round(salaries.Average()) : null,
                medianSalary = canViewSalary && salaries.Length > 0 ? (long?)medianSalary : null,
                averageTenure = tenures.Length == 0 ? (double?)null : Math.Round(tenures.Average(), 1),
                longTermPercentage = rows.Count == 0 ? 0 : Math.Round(rows.Count(x => x.YearsOfService >= 10) * 100d / rows.Count, 1)
            },
            departments,
            genders,
            salaryDistribution = canViewSalary ? BuildSalaryDistribution(salaries) : [],
            employees = pageRows,
            pagination = new { page, pageSize, pages, totalCount = filteredCount }
        });
    }

    private static List<Employee> Sort(List<Employee> rows, string? sort, string? direction)
    {
        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        Func<Employee, object> selector = sort switch
        {
            "departmentName" => x => x.DepartmentName,
            "grade" => x => Array.IndexOf(GradeOrder, x.Grade),
            "position" => x => x.Position,
            "gender" => x => x.Gender,
            "age" => x => x.Age,
            "monthlySalary" => x => x.MonthlySalary,
            "yearsOfService" => x => x.YearsOfService,
            _ => x => x.Name
        };
        return (descending ? rows.OrderByDescending(selector) : rows.OrderBy(selector)).ToList();
    }

    private static CountResponse[] BuildSalaryDistribution(long[] salaries)
    {
        var limits = new long[] { 3_000_000, 4_000_000, 5_000_000, 6_000_000, 7_000_000, 8_000_000, 9_000_000, long.MaxValue };
        var labels = new[] { "~300", "~400", "~500", "~600", "~700", "~800", "~900", "900+" };
        var counts = new int[limits.Length];
        foreach (var salary in salaries)
        {
            var index = Array.FindIndex(limits, limit => salary < limit);
            counts[index < 0 ? counts.Length - 1 : index]++;
        }
        return labels.Select((label, index) => new CountResponse(label, counts[index])).ToArray();
    }

    private sealed record EmployeeResponse(string DepartmentName, string Name, string Grade,
        string Position, string Gender, int Age, long? MonthlySalary, double YearsOfService);
    private sealed record CountResponse(string Label, int Value);
    private sealed record EmployeePasteRequest(string Text);
}
