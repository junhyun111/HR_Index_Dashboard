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
        api.MapGet("/employee-dates", (DailyEmployeeDatabaseService databases) => Results.Ok(databases.AvailableDates())).RequireAuthorization("DashboardViewer");
        api.MapGet("/employees/export", Export).RequireAuthorization("Editor");
        api.MapPost("/employees/import", Import).DisableAntiforgery().RequireAuthorization("Editor");
        api.MapPost("/employees/paste", Paste).RequireAuthorization("Editor");
        api.MapGet("/employees/search", SearchEmployees).RequireAuthorization("Editor");
        api.MapPost("/employees", CreateEmployee).RequireAuthorization("Editor");
        api.MapPut("/employees/{id:long}", UpdateEmployee).RequireAuthorization("Editor");
        api.MapDelete("/employees/{id:long}", DeleteEmployee).RequireAuthorization("Editor");
        return endpoints;
    }

    private static async Task<IResult> SearchEmployees(string? q, AppDbContext db, DailyEmployeeDatabaseService databases, CancellationToken ct)
    {
        if(!databases.SelectedDatabaseExists()) return Results.Ok(Array.Empty<Employee>());
        var query=db.Employees.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(q)) { var term=q.Trim(); query=query.Where(x=>x.EmployeeNumber.Contains(term)||(x.Name!=null&&x.Name.Contains(term))); }
        return Results.Ok(await query.OrderBy(x=>x.Name).ThenBy(x=>x.EmployeeNumber).Take(30).ToListAsync(ct));
    }

    private static async Task<IResult> CreateEmployee(EmployeeRequest request, AppDbContext db, CancellationToken ct)
    {
        var error=Validate(request); if(error!=null) return Results.BadRequest(new { message=error });
        var number=request.EmployeeNumber.Trim();
        if(await db.Employees.AnyAsync(x=>x.EmployeeNumber==number,ct)) return Results.Conflict(new { message="이미 등록된 사번입니다." });
        var employee=new Employee { EmployeeNumber=number }; SetEmployee(employee,request); db.Employees.Add(employee); await TouchEmployeeData(db,ct); await db.SaveChangesAsync(ct);
        return Results.Created($"/api/employees/{employee.Id}",employee);
    }

    private static async Task<IResult> UpdateEmployee(long id, EmployeeRequest request, AppDbContext db, CancellationToken ct)
    {
        var error=Validate(request); if(error!=null) return Results.BadRequest(new { message=error });
        var employee=await db.Employees.FindAsync([id],ct); if(employee==null) return Results.NotFound(new { message="직원을 찾을 수 없습니다." });
        var number=request.EmployeeNumber.Trim();
        if(await db.Employees.AnyAsync(x=>x.Id!=id&&x.EmployeeNumber==number,ct)) return Results.Conflict(new { message="이미 등록된 사번입니다." });
        SetEmployee(employee,request); await TouchEmployeeData(db,ct); await db.SaveChangesAsync(ct); return Results.Ok(employee);
    }

    private static async Task<IResult> DeleteEmployee(long id, AppDbContext db, CancellationToken ct)
    {
        var employee=await db.Employees.FindAsync([id],ct); if(employee==null) return Results.NotFound(new { message="직원을 찾을 수 없습니다." });
        db.Employees.Remove(employee); await TouchEmployeeData(db,ct); await db.SaveChangesAsync(ct); return Results.NoContent();
    }

    private static string? Validate(EmployeeRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.EmployeeNumber)) return "사번은 필수입니다.";
        if(request.EmployeeNumber.Trim().Length>50) return "사번은 50자 이하여야 합니다.";
        if(request.MonthlyWage<0) return "월임금은 0 이상의 원 단위 숫자여야 합니다.";
        return null;
    }

    private static void SetEmployee(Employee x, EmployeeRequest r)
    {
        string? T(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
        x.EmployeeNumber=r.EmployeeNumber.Trim(); x.Workplace=T(r.Workplace); x.ParentDepartment=T(r.ParentDepartment); x.Department=T(r.Department);
        x.Name=T(r.Name); x.Position=T(r.Position); x.WorkShift=T(r.WorkShift); x.Duty=T(r.Duty); x.JobGroup=T(r.JobGroup);
        x.EmploymentType=T(r.EmploymentType); x.Gender=T(r.Gender); x.BirthDate=r.BirthDate; x.HireDate=r.HireDate; x.TerminationDate=r.TerminationDate; x.MonthlyWage=r.MonthlyWage;
    }

    private static async Task TouchEmployeeData(AppDbContext db,CancellationToken ct)
    {
        var state=await db.EmployeeDataStates.FindAsync([1],ct);
        if(state==null)
            db.EmployeeDataStates.Add(new EmployeeDataState { Id=1,UpdatedDate=DateTime.Today,LastModifiedAt=DateTimeOffset.UtcNow });
        else
            state.LastModifiedAt=DateTimeOffset.UtcNow;
    }

    private static async Task<IResult> Export(AppDbContext db, EmployeeCsvService csv, DailyEmployeeDatabaseService databases, CancellationToken ct)
    {
        if(!databases.SelectedDatabaseExists())
            return Results.File(csv.Export(Array.Empty<Employee>()), "text/csv; charset=utf-8", $"hr-employees-{databases.SelectedDate:yyyy-MM-dd}.csv");
        var rows = await db.Employees.AsNoTracking().OrderBy(x => x.EmployeeNumber).ToListAsync(ct);
        return Results.File(csv.Export(rows), "text/csv; charset=utf-8", $"hr-employees-{DateTime.Now:yyyy-MM-dd}.csv");
    }

    private static async Task<IResult> Import(IFormFile? file, AppDbContext db, EmployeeCsvService csv, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest(new { message = "업로드할 CSV 파일을 선택하세요." });
        if (file.Length > 10 * 1024 * 1024 || !string.Equals(Path.GetExtension(file.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { message = "10MB 이하의 CSV 파일만 업로드할 수 있습니다." });
        try { await using var stream = file.OpenReadStream(); return await Apply(csv.Parse(stream), db, ct, false); }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Paste(EmployeePasteRequest request, AppDbContext db, EmployeeCsvService csv, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { message = "붙여넣은 표가 비어 있습니다." });
        try { return await Apply(csv.ParseClipboard(request.Text), db, ct, request.DeleteMissing); }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Apply(EmployeeImportResult import, AppDbContext db, CancellationToken ct, bool deleteMissing)
    {
        var numbers = import.Rows.Select(x => x.EmployeeNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // 날짜별 DB를 한 번만 읽어 대량 IN 절과 SQLite 매개변수 제한을 피한다.
        var saved = await db.Employees.ToListAsync(ct);
        var existing = saved.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);
        var added = 0; var updated = 0; var deleted = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var row in import.Rows)
        {
            if (!existing.TryGetValue(row.EmployeeNumber, out var x)) { x = new Employee { EmployeeNumber = row.EmployeeNumber }; db.Add(x); added++; }
            else updated++;
            x.Workplace=row.Workplace; x.ParentDepartment=row.ParentDepartment; x.Department=row.Department; x.Name=row.Name;
            x.Position=row.Position; x.WorkShift=row.WorkShift; x.Duty=row.Duty; x.JobGroup=row.JobGroup;
            x.EmploymentType=row.EmploymentType; x.Gender=row.Gender; x.BirthDate=row.BirthDate; x.HireDate=row.HireDate; x.TerminationDate=row.TerminationDate; x.MonthlyWage=row.MonthlyWage;
        }
        if(deleteMissing)
        {
            var missing=saved.Where(x=>!numbers.Contains(x.EmployeeNumber)).ToArray();
            deleted=missing.Length; db.Employees.RemoveRange(missing);
        }
        await TouchEmployeeData(db,ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { added, updated, deleted, total = import.Rows.Count });
    }

    private static async Task<IResult> Dashboard(string? workplace, string? department, string? position, string? search,
        string? sort, string? direction, int page, int pageSize, AppDbContext db, DailyEmployeeDatabaseService databases, CancellationToken ct)
    {
        page=Math.Max(1,page); pageSize=Math.Clamp(pageSize==0?10:pageSize,1,100);
        if(!databases.SelectedDatabaseExists()) return EmptyDashboard(databases.SelectedDate,page,pageSize);
        var totalCount=await db.Employees.CountAsync(ct); var query=db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(workplace)) query=query.Where(x=>x.Workplace==workplace);
        if (!string.IsNullOrWhiteSpace(department)) query=query.Where(x=>x.Department==department||x.ParentDepartment==department);
        if (!string.IsNullOrWhiteSpace(position)) query=query.Where(x=>x.Position==position);
        if (!string.IsNullOrWhiteSpace(search)) { var q=search.Trim(); query=query.Where(x=>x.EmployeeNumber.Contains(q)||(x.Name!=null&&x.Name.Contains(q))||(x.Department!=null&&x.Department.Contains(q))||(x.Duty!=null&&x.Duty.Contains(q))); }
        var rows=Sort(await query.ToListAsync(ct),sort,direction); var filteredCount=rows.Count;
        var today=DateTime.Today;
        var dataAsOf=databases.SelectedDate;
        var lastModifiedAt=await ReadLastModifiedAt(db,ct);
        var pages=Math.Max(1,(int)Math.Ceiling(filteredCount/(double)pageSize)); page=Math.Min(page,pages);
        CountResponse[] Counts(Func<Employee,string?> pick)=>rows.Select(pick).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label).ToArray();
        var genders=rows.Select(x=>x.Gender).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).ToDictionary(x=>x.Key,x=>x.Count());
        return Results.Ok(new {
            filters=new { workplaces=await Values(db.Employees.Select(x=>x.Workplace),ct), departments=await Values(db.Employees.Select(x=>x.Department).Concat(db.Employees.Select(x=>x.ParentDepartment)),ct), positions=await Values(db.Employees.Select(x=>x.Position),ct) },
            summary=new { totalCount,filteredCount,dataAsOf,lastModifiedAt,averageAge=AverageAge(rows,today),averageMonthlyWage=AverageMonthlyWage(rows),averageTenure=AverageTenure(rows,today),hiresThisYear=rows.Count(x=>x.HireDate!=null&&x.HireDate.Value.Year==today.Year),terminationsThisYear=rows.Count(x=>x.TerminationDate!=null&&x.TerminationDate.Value.Year==today.Year&&x.TerminationDate.Value>=today) },
            departments=Counts(x=>x.Department).Where(x=>x.Value>1),genders,
            jobGroups=rows.Select(x=>NormalizeJobGroup(x.JobGroup)).Where(x=>x!=null).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label),
            tenureGroups=TenureGroups(rows,today),
            monthlyWages=MonthlyWageGroups(rows),
            ageGroups=AgeGroups(rows,today),
            monthlyHires=MonthlyDateCounts(rows.Select(x=>x.HireDate),today.Year),
            monthlyTerminations=MonthlyDateCounts(rows.Select(x=>x.TerminationDate),today.Year,today),
            employees=rows.Skip((page-1)*pageSize).Take(pageSize),pagination=new {page,pageSize,pages,totalCount=filteredCount}
        });
    }

    private static async Task<DateTimeOffset?> ReadLastModifiedAt(AppDbContext db,CancellationToken ct)
    {
        var connection=db.Database.GetDbConnection();
        if(connection.State!=System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var schema=connection.CreateCommand();
        schema.CommandText="SELECT COUNT(*) FROM pragma_table_info('EmployeeDataState') WHERE name='LastModifiedAt'";
        if(Convert.ToInt32(await schema.ExecuteScalarAsync(ct))==0)return null;
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT LastModifiedAt FROM EmployeeDataState WHERE Id=1";
        var value=await command.ExecuteScalarAsync(ct);
        return value is string text&&DateTimeOffset.TryParse(text,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var timestamp)
            ?timestamp
            :null;
    }

    private static IResult EmptyDashboard(DateTime dataAsOf,int page,int pageSize)
    {
        var months=Enumerable.Range(1,12).Select(month=>new CountResponse($"{month}월",0)).ToArray();
        return Results.Ok(new {
            filters=new { workplaces=Array.Empty<string>(),departments=Array.Empty<string>(),positions=Array.Empty<string>() },
            summary=new { totalCount=0,filteredCount=0,dataAsOf,lastModifiedAt=(DateTimeOffset?)null,averageAge=(double?)null,averageMonthlyWage=(long?)null,averageTenure=(double?)null,hiresThisYear=0,terminationsThisYear=0 },
            departments=Array.Empty<CountResponse>(),genders=new Dictionary<string,int>(),jobGroups=Array.Empty<CountResponse>(),
            tenureGroups=Array.Empty<CountResponse>(),monthlyWages=Array.Empty<CountResponse>(),ageGroups=Array.Empty<CountResponse>(),
            monthlyHires=months,monthlyTerminations=months,employees=Array.Empty<Employee>(),
            pagination=new {page,pageSize,pages=1,totalCount=0}
        });
    }

    private static string? NormalizeJobGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed=value.Trim();
        return trimmed.StartsWith("계약직",StringComparison.OrdinalIgnoreCase) ? "계약직" : trimmed;
    }

    private static double? AverageAge(IEnumerable<Employee> rows,DateTime today)
    {
        var ages=rows.Where(x=>x.BirthDate!=null).Select(x=>{var birth=x.BirthDate!.Value.Date;return today.Year-birth.Year-(birth.Date>today.AddYears(-(today.Year-birth.Year))?1:0);}).ToArray();
        return ages.Length==0?null:Math.Round(ages.Average(),1);
    }

    private static CountResponse[] AgeGroups(IEnumerable<Employee> rows,DateTime today)
    {
        var counts=new int[6];
        foreach(var birth in rows.Where(x=>x.BirthDate!=null).Select(x=>x.BirthDate!.Value.Date))
        {
            var age=today.Year-birth.Year-(birth>today.AddYears(-(today.Year-birth.Year))?1:0);
            if(age<0) continue;
            counts[age<20?0:age<30?1:age<40?2:age<50?3:age<60?4:5]++;
        }
        var labels=new[]{"20세 미만","20대","30대","40대","50대","60세 이상"};
        return labels.Select((label,i)=>new CountResponse(label,counts[i])).ToArray();
    }

    private static CountResponse[] MonthlyDateCounts(IEnumerable<DateTime?> dates,int year,DateTime? from=null)
    {
        var counts=new int[12];
        foreach(var date in dates.Where(x=>x!=null).Select(x=>x!.Value.Date))
            if(date.Year==year&&(from==null||date>=from.Value.Date)) counts[date.Month-1]++;
        return Enumerable.Range(1,12).Select(month=>new CountResponse($"{month}월",counts[month-1])).ToArray();
    }

    private static double? AverageTenure(IEnumerable<Employee> rows,DateTime today)
    {
        var years=rows.Where(x=>x.HireDate!=null).Select(x=>(today-x.HireDate!.Value.Date).TotalDays/365.2425).Where(x=>x>=0).ToArray();
        return years.Length==0?null:Math.Round(years.Average(),1);
    }

    private static CountResponse[] TenureGroups(IEnumerable<Employee> rows,DateTime today)
    {
        var counts=new int[5];
        foreach(var hire in rows.Where(x=>x.HireDate!=null).Select(x=>x.HireDate!.Value.Date))
        {
            var years=(today-hire).TotalDays/365.2425;
            if(years<0) continue;
            counts[years<1?0:years<3?1:years<5?2:years<10?3:4]++;
        }
        var labels=new[]{"1년 미만","1~3년","3~5년","5~10년","10년 이상"};
        return labels.Select((label,i)=>new CountResponse(label,counts[i])).ToArray();
    }

    private static double? AverageMonthlyWage(IEnumerable<Employee> rows)
    {
        var wages=rows.Where(x=>x.MonthlyWage!=null).Select(x=>x.MonthlyWage!.Value).ToArray();
        return wages.Length==0?null:Math.Round(wages.Average()/10000d,1);
    }

    private static CountResponse[] MonthlyWageGroups(IEnumerable<Employee> rows)
    {
        var counts=new int[8];
        foreach(var wage in rows.Where(x=>x.MonthlyWage!=null).Select(x=>x.MonthlyWage!.Value/10000d))
            counts[wage<300?0:wage<400?1:wage<500?2:wage<600?3:wage<700?4:wage<800?5:wage<900?6:7]++;
        var labels=new[]{"~300","~400","~500","~600","~700","~800","~900","900+"};
        return labels.Select((label,i)=>new CountResponse(label,counts[i])).ToArray();
    }

    private static async Task<string[]> Values(IQueryable<string?> q,CancellationToken ct)=>await q.Where(x=>x!=null&&x!="").Select(x=>x!).Distinct().Order().ToArrayAsync(ct);
    private static List<Employee> Sort(List<Employee> rows,string? sort,string? direction)
    {
        Func<Employee,object?> key=sort switch { "employeeNumber"=>x=>x.EmployeeNumber,"workplace"=>x=>x.Workplace,"parentDepartment"=>x=>x.ParentDepartment,"department"=>x=>x.Department,"position"=>x=>x.Position,"workShift"=>x=>x.WorkShift,"duty"=>x=>x.Duty,"jobGroup"=>x=>x.JobGroup,"employmentType"=>x=>x.EmploymentType,"gender"=>x=>x.Gender,"birthDate"=>x=>x.BirthDate,"hireDate"=>x=>x.HireDate,"terminationDate"=>x=>x.TerminationDate,"monthlyWage"=>x=>x.MonthlyWage,"name"=>x=>x.Name,_=>x=>x.Id };
        return (direction=="desc"?rows.OrderByDescending(key):rows.OrderBy(key)).ToList();
    }
    private sealed record CountResponse(string Label,int Value);
    private sealed record EmployeePasteRequest(string Text,bool DeleteMissing=false);
    private sealed record EmployeeRequest(string EmployeeNumber,string? Workplace,string? ParentDepartment,string? Department,string? Name,string? Position,string? WorkShift,string? Duty,string? JobGroup,string? EmploymentType,string? Gender,DateTime? BirthDate,DateTime? HireDate,DateTime? TerminationDate,long? MonthlyWage);
}
