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
        api.MapGet("/session", (HttpContext c) =>
        {
            var isAdministrator=c.User.IsInRole("Administrator");
            var isHrAdministrator=c.User.IsInRole("HrAdministrator");
            return Results.Ok(new {
                userName=c.User.Identity?.Name,
                role=isAdministrator?"Administrator":isHrAdministrator?"HrAdministrator":"User",
                canEdit=isAdministrator||isHrAdministrator,
                canViewSalary=isAdministrator||isHrAdministrator,
                canManageUsers=isAdministrator,
                isAdministrator,
                isHrAdministrator,
                theme=c.User.FindFirst("theme")?.Value??"light"
            });
        }).RequireAuthorization("DashboardViewer");
        api.MapGet("/dashboard", Dashboard).RequireAuthorization("DashboardViewer");
        api.MapGet("/employee-dates", (DailyEmployeeDatabaseService databases) => Results.Ok(databases.AvailableDates())).RequireAuthorization("DashboardViewer");
        api.MapGet("/employees/headcount-trend", async (string? mode,DailyEmployeeDatabaseService databases,CancellationToken ct)
            =>Results.Ok(await databases.HeadcountTrendAsync(mode,ct))).RequireAuthorization("DashboardViewer");
        api.MapGet("/personnel-movements",PersonnelMovements).RequireAuthorization("DashboardViewer");
        api.MapPost("/personnel-movements/hires",AddScheduledHire).RequireAuthorization("Editor");
        api.MapDelete("/personnel-movements/hires/{id:long}",DeleteScheduledHire).RequireAuthorization("Editor");
        api.MapGet("/employees/export", Export).RequireAuthorization("Editor");
        api.MapPost("/employees/import", Import).DisableAntiforgery().RequireAuthorization("Editor");
        api.MapPost("/employees/paste", Paste).RequireAuthorization("Editor");
        api.MapGet("/employees/search", SearchEmployees).RequireAuthorization("Editor");
        api.MapPost("/employees", CreateEmployee).RequireAuthorization("Editor");
        api.MapPut("/employees/{id:long}", UpdateEmployee).RequireAuthorization("Editor");
        api.MapDelete("/employees/all", DeleteAllEmployees).RequireAuthorization("Editor");
        api.MapDelete("/employees/{id:long}", DeleteEmployee).RequireAuthorization("Editor");
        return endpoints;
    }

    private static async Task<IResult> PersonnelMovements(
        AppDbContext employeeDb,DailyEmployeeDatabaseService databases,EmployeeMovementService movements,CancellationToken ct)
    {
        if(await databases.SelectedDatabaseHasEmployeesTableAsync(ct))
        {
            await databases.MigrateExistingDatabaseAsync(employeeDb,ct);
            await movements.SyncFromEmployeeDatabaseAsync(employeeDb,DateTime.Today,ct);
        }
        return Results.Ok(await movements.GetAsync(DateTime.Today,ct));
    }

    private static async Task<IResult> AddScheduledHire(
        ScheduledHireRequest request,HttpContext context,EmployeeMovementService movements,CancellationToken ct)
    {
        try
        {
            var item=await movements.AddScheduledHireAsync(
                request.EmployeeNumber??"",request.Name??"",request.Department,request.Position,request.HireDate,
                context.User.Identity?.Name??"알 수 없음",ct);
            return Results.Created($"/api/personnel-movements/hires/{item.Id}",item);
        }
        catch(ArgumentException e){return Results.BadRequest(new{message=e.Message});}
        catch(InvalidOperationException e){return Results.Conflict(new{message=e.Message});}
    }

    private static async Task<IResult> DeleteScheduledHire(long id,EmployeeMovementService movements,CancellationToken ct)
        =>await movements.DeleteScheduledHireAsync(id,ct)?Results.NoContent():Results.NotFound(new{message="삭제할 입사예정자를 찾을 수 없습니다."});

    private static async Task<IResult> SearchEmployees(string? q, AppDbContext db, DailyEmployeeDatabaseService databases, CancellationToken ct)
    {
        if(!databases.SelectedDatabaseExists()) return Results.Ok(Array.Empty<Employee>());
        await databases.MigrateExistingDatabaseAsync(db,ct);
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

    private static async Task<IResult> DeleteAllEmployees(AppDbContext db, CancellationToken ct)
    {
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var deleted=await db.Employees.ExecuteDeleteAsync(ct);
        await TouchEmployeeData(db,ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { deleted });
    }

    private static string? Validate(EmployeeRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.EmployeeNumber)) return "사번은 필수입니다.";
        if(request.EmployeeNumber.Trim().Length>50) return "사번은 50자 이하여야 합니다.";
        if(request.AnnualSalary<0) return "책정연봉은 0 이상의 원 단위 숫자여야 합니다.";
        if(request.MonthlyWage<0) return "월임금은 0 이상의 원 단위 숫자여야 합니다.";
        return null;
    }

    private static void SetEmployee(Employee x, EmployeeRequest r)
    {
        string? T(string? v)=>string.IsNullOrWhiteSpace(v)||v.Trim()=="-"?null:v.Trim();
        x.EmployeeNumber=r.EmployeeNumber.Trim(); x.Workplace=T(r.Workplace); x.ParentDepartment=T(r.ParentDepartment); x.Department=T(r.Department);
        x.Name=T(r.Name); x.Position=T(r.Position); x.WorkShift=T(r.WorkShift); x.Duty=T(r.Duty); x.JobGroup=T(r.JobGroup);
        x.EmploymentType=T(r.EmploymentType); x.Gender=T(r.Gender);
        x.BirthDate=r.BirthDate; x.HireDate=r.HireDate; x.TerminationDate=r.TerminationDate;
        x.AnnualSalary=r.AnnualSalary; x.MonthlyWage=r.MonthlyWage;
        x.Education=T(r.Education); x.SchoolName=T(r.SchoolName); x.Major=T(r.Major);
    }

    private static async Task TouchEmployeeData(AppDbContext db,CancellationToken ct)
    {
        var state=await db.EmployeeDataStates.FindAsync([1],ct);
        if(state==null)
            db.EmployeeDataStates.Add(new EmployeeDataState { Id=1,UpdatedDate=DateTime.Today,LastModifiedAt=DateTimeOffset.UtcNow });
        else
            state.LastModifiedAt=DateTimeOffset.UtcNow;
    }

    private static async Task<IResult> Export(AppDbContext db, EmployeeCsvService csv, EmployeeColumnSettingsService columnSettings, DailyEmployeeDatabaseService databases, CancellationToken ct)
    {
        var displayNames=await columnSettings.DisplayNamesByDefaultAsync(ct);
        if(!databases.SelectedDatabaseExists())
            return Results.File(csv.ExportExcel(Array.Empty<Employee>(),displayNames), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"hr-employees-{databases.SelectedDate:yyyy-MM-dd}.xlsx");
        await databases.MigrateExistingDatabaseAsync(db,ct);
        var rows = await db.Employees.AsNoTracking().ToListAsync(ct);
        return Results.File(csv.ExportExcel(rows,displayNames), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"hr-employees-{databases.SelectedDate:yyyy-MM-dd}.xlsx");
    }

    private static async Task<IResult> Import(IFormFile? file, bool deleteMissing, AppDbContext db, EmployeeCsvService csv, EmployeeColumnSettingsService columnSettings, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest(new { message = "업로드할 Excel 또는 CSV 파일을 선택하세요." });
        var extension=Path.GetExtension(file.FileName);
        if (file.Length > 10 * 1024 * 1024 || !(extension.Equals(".xlsx",StringComparison.OrdinalIgnoreCase)||extension.Equals(".xlsm",StringComparison.OrdinalIgnoreCase)||extension.Equals(".xls",StringComparison.OrdinalIgnoreCase)||extension.Equals(".csv",StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new { message = "10MB 이하의 .xlsx, .xlsm, .xls 또는 .csv 파일만 업로드할 수 있습니다." });
        try
        {
            await using var stream=file.OpenReadStream();
            var aliases=await columnSettings.HeaderAliasesAsync(ct);
            var import=extension.ToLowerInvariant() switch
            {
                ".xlsx" or ".xlsm"=>csv.ParseExcel(stream,aliases),
                ".xls"=>csv.ParseLegacyExcel(stream,aliases),
                _=>csv.Parse(stream,aliases)
            };
            return await Apply(import,deleteMissing,db,ct);
        }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Paste(EmployeePasteRequest request, AppDbContext db, EmployeeCsvService csv, EmployeeColumnSettingsService columnSettings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { message = "붙여넣은 표가 비어 있습니다." });
        try { return await Apply(csv.ParseClipboard(request.Text,await columnSettings.HeaderAliasesAsync(ct)),request.DeleteMissing,db,ct); }
        catch (EmployeeCsvException e) { return Results.BadRequest(new { message = e.Message }); }
    }

    private static async Task<IResult> Apply(EmployeeImportResult import,bool deleteMissing,AppDbContext db,CancellationToken ct)
    {
        // 날짜별 DB를 한 번만 읽어 대량 IN 절과 SQLite 매개변수 제한을 피한다.
        var saved = await db.Employees.ToListAsync(ct);
        var existing = saved.ToDictionary(x => x.EmployeeNumber, StringComparer.OrdinalIgnoreCase);
        var importedNumbers=import.Rows.Select(x=>x.EmployeeNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing=deleteMissing?saved.Where(x=>!importedNumbers.Contains(x.EmployeeNumber)).ToArray():[];
        var added = 0; var updated = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if(missing.Length>0)db.Employees.RemoveRange(missing);
        foreach (var row in import.Rows)
        {
            if (!existing.TryGetValue(row.EmployeeNumber, out var x)) { x = new Employee { EmployeeNumber = row.EmployeeNumber }; db.Add(x); added++; }
            else updated++;
            if(import.PresentHeaders.Contains("사업장"))x.Workplace=row.Workplace;
            if(import.PresentHeaders.Contains("상위부서"))x.ParentDepartment=row.ParentDepartment;
            if(import.PresentHeaders.Contains("부서"))x.Department=row.Department;
            if(import.PresentHeaders.Contains("성명"))x.Name=row.Name;
            if(import.PresentHeaders.Contains("직위"))x.Position=row.Position;
            if(import.PresentHeaders.Contains("근무조"))x.WorkShift=row.WorkShift;
            if(import.PresentHeaders.Contains("직책"))x.Duty=row.Duty;
            if(import.PresentHeaders.Contains("직군"))x.JobGroup=row.JobGroup;
            if(import.PresentHeaders.Contains("사원구분"))x.EmploymentType=row.EmploymentType;
            if(import.PresentHeaders.Contains("성별"))x.Gender=row.Gender;
            if(import.PresentHeaders.Contains("생년월일"))x.BirthDate=row.BirthDate;
            if(import.PresentHeaders.Contains("입사일자"))x.HireDate=row.HireDate;
            if(import.PresentHeaders.Contains("퇴사일자"))x.TerminationDate=row.TerminationDate;
            if(import.PresentHeaders.Contains("책정연봉"))x.AnnualSalary=row.AnnualSalary;
            if(import.PresentHeaders.Contains("월임금"))x.MonthlyWage=row.MonthlyWage;
            if(import.PresentHeaders.Contains("최종학력"))x.Education=row.Education;
            if(import.PresentHeaders.Contains("학교명"))x.SchoolName=row.SchoolName;
            if(import.PresentHeaders.Contains("전공"))x.Major=row.Major;
        }
        await TouchEmployeeData(db,ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Results.Ok(new { added, updated, deleted=missing.Length, total = import.Rows.Count });
    }

    private static async Task<IResult> Dashboard(string? workplace,string? department,string? position,string? jobGroup,string? gender,string? search,
        int page, int pageSize, HttpContext context, AppDbContext db,
        DailyEmployeeDatabaseService databases,SalaryPositionAxisSettingsService salaryPositionSettings,CancellationToken ct)
    {
        var canViewSalary=context.User.IsInRole("Administrator")||context.User.IsInRole("HrAdministrator");
        page=Math.Max(1,page); pageSize=Math.Clamp(pageSize==0?10:pageSize,1,100);
        var salaryPositionAxes=await salaryPositionSettings.GetAsync(ct);
        // 조회만으로 빈 날짜 DB를 만들지 않는다. 기존에 남아 있는 빈 파일도 빈 화면으로 처리한다.
        if(!await databases.SelectedDatabaseHasEmployeesTableAsync(ct)) return EmptyDashboard(databases.SelectedDate,page,pageSize,salaryPositionAxes);
        await databases.MigrateExistingDatabaseAsync(db,ct);
        var totalCount=await db.Employees.CountAsync(ct); var query=db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(workplace)) query=query.Where(x=>x.Workplace==workplace);
        if (!string.IsNullOrWhiteSpace(department)) query=query.Where(x=>x.Department==department||x.ParentDepartment==department);
        if (!string.IsNullOrWhiteSpace(position)) query=query.Where(x=>x.Position==position);
        if (!string.IsNullOrWhiteSpace(jobGroup)) query=query.Where(x=>x.JobGroup==jobGroup);
        if (!string.IsNullOrWhiteSpace(gender)) query=query.Where(x=>x.Gender==gender);
        if (!string.IsNullOrWhiteSpace(search)) { var q=search.Trim(); query=query.Where(x=>x.EmployeeNumber.Contains(q)||(x.Name!=null&&x.Name.Contains(q))||(x.Department!=null&&x.Department.Contains(q))||(x.Duty!=null&&x.Duty.Contains(q))||(x.Position!=null&&x.Position.Contains(q))||(x.JobGroup!=null&&x.JobGroup.Contains(q))||(x.Gender!=null&&x.Gender.Contains(q))); }
        var rows=await query.ToListAsync(ct); var filteredCount=rows.Count;
        var dataAsOf=databases.SelectedDate.Date;
        var referenceDate=dataAsOf;
        var lastModifiedAt=await ReadLastModifiedAt(db,ct);
        var isAutomaticallyUpdated=lastModifiedAt==null&&await databases.IsAutomaticallyUpdatedAsync(dataAsOf,ct);
        var pages=Math.Max(1,(int)Math.Ceiling(filteredCount/(double)pageSize)); page=Math.Min(page,pages);
        var genders=rows.Select(x=>x.Gender).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).ToDictionary(x=>x.Key,x=>x.Count());
        var averageAnnualSalary=canViewSalary?AverageAnnualSalary(rows):null;
        var annualSalaryGroups=canViewSalary?AnnualSalaryGroups(rows):Array.Empty<CountResponse>();
        var salaryPositionBands=canViewSalary?SalaryPositionBands(rows,salaryPositionAxes):Array.Empty<SalaryPositionBand>();
        var salaryAgeGenderBands=canViewSalary?SalaryAgeGenderBands(rows,referenceDate):Array.Empty<SalaryAgeGenderBand>();
        var departments=rows
            .Where(x=>!string.IsNullOrWhiteSpace(x.Department))
            .GroupBy(x=>x.Department!)
            .Select(group=>new DepartmentCountResponse(
                group.Key,
                group.Count(),
                group.GroupBy(x=>string.IsNullOrWhiteSpace(x.Position)?"미지정":x.Position!)
                    .Select(position=>new CountResponse(position.Key,position.Count()))
                    .OrderByDescending(position=>position.Value)
                    .ThenBy(position=>position.Label)
                    .ToArray()))
            .Where(x=>x.Value>1)
            .OrderByDescending(x=>x.Value)
            .ThenBy(x=>x.Label)
            .ToArray();
        if(!canViewSalary)
            foreach(var employee in rows)
            {
                employee.AnnualSalary=null;
                employee.MonthlyWage=null;
            }
        return Results.Ok(new {
            filters=new { workplaces=await Values(db.Employees.Select(x=>x.Workplace),ct), departments=await Values(db.Employees.Select(x=>x.Department).Concat(db.Employees.Select(x=>x.ParentDepartment)),ct), positions=await Values(db.Employees.Select(x=>x.Position),ct), jobGroups=await Values(db.Employees.Select(x=>x.JobGroup),ct), genders=await Values(db.Employees.Select(x=>x.Gender),ct) },
            summary=new { totalCount,filteredCount,dataAsOf,lastModifiedAt,isAutomaticallyUpdated,averageAge=AverageAge(rows,referenceDate),averageAnnualSalary,averageTenure=AverageTenure(rows,referenceDate),hiresThisYear=rows.Count(x=>x.HireDate!=null&&x.HireDate.Value.Year==referenceDate.Year&&x.HireDate.Value.Date<=referenceDate),terminationsThisYear=rows.Count(x=>x.TerminationDate!=null&&x.TerminationDate.Value.Year==referenceDate.Year&&x.TerminationDate.Value.Date>=referenceDate) },
            departments,genders,
            jobGroups=rows.Select(x=>NormalizeJobGroup(x.JobGroup)).Where(x=>x!=null).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label),
            educationGroups=rows.Select(x=>x.Education?.Trim()).Where(x=>!string.IsNullOrWhiteSpace(x)).GroupBy(x=>x!).Select(x=>new CountResponse(x.Key,x.Count())).OrderByDescending(x=>x.Value).ThenBy(x=>x.Label),
            tenureGroups=TenureGroups(rows,referenceDate),
            annualSalaryGroups,
            salaryPositionBands,
            salaryAgeGenderBands,
            ageGroups=AgeGroups(rows,referenceDate),
            ageTenurePoints=AgeTenurePoints(rows,referenceDate),
            monthlyHires=MonthlyDateCounts(rows.Select(x=>x.HireDate),referenceDate.Year,null,referenceDate),
            monthlyTerminations=MonthlyDateCounts(rows.Select(x=>x.TerminationDate),referenceDate.Year,referenceDate),
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

    private static IResult EmptyDashboard(DateTime dataAsOf,int page,int pageSize,IReadOnlyList<SalaryPositionAxisResponse> salaryPositionAxes)
    {
        var months=Enumerable.Range(1,12).Select(month=>new CountResponse($"{month}월",0)).ToArray();
        return Results.Ok(new {
            filters=new { workplaces=Array.Empty<string>(),departments=Array.Empty<string>(),positions=Array.Empty<string>(),jobGroups=Array.Empty<string>(),genders=Array.Empty<string>() },
            summary=new { totalCount=0,filteredCount=0,dataAsOf,lastModifiedAt=(DateTimeOffset?)null,isAutomaticallyUpdated=false,averageAge=(double?)null,averageAnnualSalary=(long?)null,averageTenure=(double?)null,hiresThisYear=0,terminationsThisYear=0 },
            departments=Array.Empty<DepartmentCountResponse>(),genders=new Dictionary<string,int>(),jobGroups=Array.Empty<CountResponse>(),educationGroups=Array.Empty<CountResponse>(),
            tenureGroups=Array.Empty<CountResponse>(),annualSalaryGroups=Array.Empty<CountResponse>(),
            salaryPositionBands=SalaryPositionBands([],salaryPositionAxes),
            salaryAgeGenderBands=Array.Empty<SalaryAgeGenderBand>(),
            ageGroups=Array.Empty<CountResponse>(),ageTenurePoints=Array.Empty<AgeTenurePoint>(),
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

    private static CountResponse[] MonthlyDateCounts(IEnumerable<DateTime?> dates,int year,DateTime? from=null,DateTime? through=null)
    {
        var counts=new int[12];
        foreach(var date in dates.Where(x=>x!=null).Select(x=>x!.Value.Date))
            if(date.Year==year&&(from==null||date>=from.Value.Date)&&(through==null||date<=through.Value.Date)) counts[date.Month-1]++;
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

    private static AgeTenurePoint[] AgeTenurePoints(IEnumerable<Employee> rows,DateTime today)
        =>rows.Where(x=>x.BirthDate!=null&&x.HireDate!=null)
            .Select(x=>new AgeTenurePoint(
                Math.Round((today-x.BirthDate!.Value.Date).TotalDays/365.2425,2),
                Math.Round((today-x.HireDate!.Value.Date).TotalDays/365.2425,2)))
            .Where(x=>x.Age>=0&&x.Age<=100&&x.Tenure>=0)
            .ToArray();

    private static double? AverageAnnualSalary(IEnumerable<Employee> rows)
    {
        var salaries=rows.Where(x=>x.AnnualSalary!=null).Select(x=>x.AnnualSalary!.Value).ToArray();
        return salaries.Length==0?null:Math.Round(salaries.Average()/10000d,1);
    }

    private static CountResponse[] AnnualSalaryGroups(IEnumerable<Employee> rows)
    {
        var counts=new int[8];
        foreach(var salary in rows.Where(x=>x.AnnualSalary!=null).Select(x=>x.AnnualSalary!.Value/10000d))
            counts[salary<3000?0:salary<4000?1:salary<5000?2:salary<6000?3:salary<7000?4:salary<8000?5:salary<9000?6:7]++;
        var labels=new[]{"~3천","~4천","~5천","~6천","~7천","~8천","~9천","9천+"};
        return labels.Select((label,i)=>new CountResponse(label,counts[i])).ToArray();
    }

    private static SalaryPositionBand[] SalaryPositionBands(IEnumerable<Employee> rows,IReadOnlyList<SalaryPositionAxisResponse> axes)
    {
        var employees=rows.Where(x=>x.AnnualSalary!=null&&!string.IsNullOrWhiteSpace(x.Position)).ToArray();
        return axes.Select(axis=>{
            var values=employees
                .Where(x=>string.Equals(x.Position,axis.PositionName,StringComparison.OrdinalIgnoreCase))
                .Select(x=>x.AnnualSalary!.Value/10000d)
                .Order()
                .ToArray();
            if(values.Length==0)return new SalaryPositionBand(axis.PositionName,0,null,null,null,null,null,null);
            double Q(double percentile)
            {
                var index=(values.Length-1)*percentile;
                var lower=(int)Math.Floor(index);
                var upper=(int)Math.Ceiling(index);
                return values[lower]+(values[upper]-values[lower])*(index-lower);
            }
            double R(double value)=>Math.Round(value,1);
            return new SalaryPositionBand(axis.PositionName,values.Length,R(values[0]),R(Q(.25)),R(Q(.5)),R(Q(.75)),R(values[^1]),R(values.Average()));
        }).ToArray();
    }

    private static SalaryAgeGenderBand[] SalaryAgeGenderBands(IEnumerable<Employee> rows,DateTime today)
    {
        var labels=new[]{"20세 미만","20대","30대","40대","50대","60세 이상"};
        var employees=rows
            .Where(x=>x.AnnualSalary!=null&&x.BirthDate!=null)
            .Select(x=>new {
                Age=today.Year-x.BirthDate!.Value.Year-(x.BirthDate.Value.Date>today.AddYears(-(today.Year-x.BirthDate.Value.Year))?1:0),
                Gender=NormalizeSalaryGender(x.Gender),
                Salary=x.AnnualSalary!.Value/10000d
            })
            .Where(x=>x.Age>=0&&x.Age<=100&&x.Gender!=null)
            .Select(x=>new {
                Group=x.Age<20?0:x.Age<30?1:x.Age<40?2:x.Age<50?3:x.Age<60?4:5,
                x.Gender,
                x.Salary
            })
            .ToArray();
        return labels.SelectMany((label,group)=>new[]{"남성","여성"}.Select(gender=>{
            var values=employees
                .Where(x=>x.Group==group&&x.Gender==gender)
                .Select(x=>x.Salary)
                .Order()
                .ToArray();
            if(values.Length==0)return new SalaryAgeGenderBand(label,gender,0,null,null,null,null,null);
            double Q(double percentile)
            {
                var index=(values.Length-1)*percentile;
                var lower=(int)Math.Floor(index);
                var upper=(int)Math.Ceiling(index);
                return values[lower]+(values[upper]-values[lower])*(index-lower);
            }
            double R(double value)=>Math.Round(value,1);
            return new SalaryAgeGenderBand(label,gender,values.Length,R(values[0]),R(Q(.25)),R(Q(.5)),R(Q(.75)),R(values[^1]));
        })).ToArray();
    }

    private static string? NormalizeSalaryGender(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        var gender=value.Trim();
        if(gender.StartsWith("남",StringComparison.OrdinalIgnoreCase)||gender.Equals("M",StringComparison.OrdinalIgnoreCase)||gender.Equals("Male",StringComparison.OrdinalIgnoreCase))return "남성";
        if(gender.StartsWith("여",StringComparison.OrdinalIgnoreCase)||gender.Equals("F",StringComparison.OrdinalIgnoreCase)||gender.Equals("Female",StringComparison.OrdinalIgnoreCase))return "여성";
        return null;
    }

    private static async Task<string[]> Values(IQueryable<string?> q,CancellationToken ct)=>await q.Where(x=>x!=null&&x!="").Select(x=>x!).Distinct().Order().ToArrayAsync(ct);
    private sealed record CountResponse(string Label,int Value);
    private sealed record DepartmentCountResponse(string Label,int Value,IReadOnlyList<CountResponse> Positions);
    private sealed record SalaryPositionBand(string Label,int Count,double? Min,double? Q1,double? Median,double? Q3,double? Max,double? Average);
    private sealed record SalaryAgeGenderBand(string Label,string Gender,int Count,double? Min,double? Q1,double? Median,double? Q3,double? Max);
    private sealed record AgeTenurePoint(double Age,double Tenure);
    private sealed record EmployeePasteRequest(string Text,bool DeleteMissing);
    private sealed record ScheduledHireRequest(string? EmployeeNumber,string? Name,string? Department,string? Position,DateTime HireDate);
    private sealed record EmployeeRequest(string EmployeeNumber,string? Workplace,string? ParentDepartment,string? Department,string? Name,string? Position,string? WorkShift,string? Duty,string? JobGroup,string? EmploymentType,string? Gender,DateTime? BirthDate,DateTime? HireDate,DateTime? TerminationDate,long? AnnualSalary,long? MonthlyWage,string? Education,string? SchoolName,string? Major);
}
