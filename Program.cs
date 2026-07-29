using HRDashboard.Data;
using HRDashboard.Endpoints;
using HRDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;
using System.Threading.RateLimiting;

var envPath=Path.Combine(Directory.GetCurrentDirectory(),".env");
if(File.Exists(envPath))
foreach(var line in File.ReadLines(envPath))
{
    var trimmed=line.Trim();if(trimmed.Length==0||trimmed.StartsWith('#'))continue;
    var separator=trimmed.IndexOf('=');if(separator<=0)continue;
    var name=trimmed[..separator].Trim();var value=trimmed[(separator+1)..].Trim().Trim('"','\'');
    if(Environment.GetEnvironmentVariable(name)==null)Environment.SetEnvironmentVariable(name,value);
}
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keyDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "HRDashboard.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var idValue=context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if(!long.TryParse(idValue,out var id)){context.RejectPrincipal();return;}
            var db=context.HttpContext.RequestServices.GetRequiredService<CommonSettingsDbContext>();
            var user=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id,context.HttpContext.RequestAborted);
            var claimName=context.Principal?.Identity?.Name;
            var claimRole=context.Principal?.FindFirstValue(ClaimTypes.Role);
            var claimTheme=context.Principal?.FindFirstValue("theme");
            var claimStamp=context.Principal?.FindFirstValue("securityStamp");
            if(user==null||!user.IsActive||user.LoginId!=claimName||user.Role!=claimRole||user.Theme!=claimTheme
                ||user.UpdatedAtUtc.ToUnixTimeMilliseconds().ToString()!=claimStamp)
                context.RejectPrincipal();
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardViewer", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("SalaryViewer", policy => policy.RequireRole("Administrator","HrAdministrator"));
    options.AddPolicy("Editor", policy => policy.RequireRole("Administrator","HrAdministrator"));
    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DailyEmployeeDatabaseService>();
builder.Services.AddHostedService<DailyEmployeeDatabaseUpdateService>();
builder.Services.AddHostedService<EmployeeDatabaseRetentionService>();
builder.Services.AddDbContext<AppDbContext>((services,options)=>
    options.UseSqlite(services.GetRequiredService<DailyEmployeeDatabaseService>().ConnectionStringForSelectedDate()));
var managementConnectionString = builder.Configuration.GetConnectionString("Management")
    ?? throw new InvalidOperationException("경영지표 SQLite 연결 문자열이 없습니다.");
builder.Services.AddDbContext<ManagementDbContext>(options => options.UseSqlite(managementConnectionString));
var commonSettingsConnectionString = builder.Configuration.GetConnectionString("CommonSettings")
    ?? throw new InvalidOperationException("공통 설정 SQLite 연결 문자열이 없습니다.");
builder.Services.AddDbContext<CommonSettingsDbContext>(options => options.UseSqlite(commonSettingsConnectionString));
builder.Services.AddScoped<EmployeeColumnSettingsService>();
builder.Services.AddScoped<SalaryPositionAxisSettingsService>();
builder.Services.AddScoped<UserAccountService>();
builder.Services.AddSingleton<EmployeeCsvService>();
builder.Services.AddHttpClient<DartFinancialService>(client =>
{
    client.BaseAddress=new Uri("https://opendart.fss.or.kr/");
    client.Timeout=TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("INNODEP-HR-Dashboard/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, application/xml, application/zip");
});
builder.Services.AddHttpClient<ExternalApiClient>((services, client) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["ExternalApi:BaseUrl"];
    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) client.BaseAddress = uri;
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context,next) =>
{
    var isEmployeeMutation=context.Request.Path.StartsWithSegments("/api/employees")
        && context.Request.Method is "POST" or "PUT" or "DELETE";
    if(isEmployeeMutation)
    {
        var databases=context.RequestServices.GetRequiredService<DailyEmployeeDatabaseService>();
        var db=context.RequestServices.GetRequiredService<AppDbContext>();
        await databases.MigrateSelectedDatabaseAsync(db,context.RequestAborted);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR IGNORE INTO EmployeeDataState (Id, UpdatedDate) VALUES (1, {databases.SelectedDate:yyyy-MM-dd})",
            context.RequestAborted);
    }
    await next();
    if(isEmployeeMutation&&context.Response.StatusCode<400)
    {
        var databases=context.RequestServices.GetRequiredService<DailyEmployeeDatabaseService>();
        var settingsDb=context.RequestServices.GetRequiredService<CommonSettingsDbContext>();
        var action=(context.Request.Method,context.Request.Path.Value) switch
        {
            ("POST",var path) when path?.EndsWith("/paste")==true=>"Excel 붙여넣기",
            ("POST",var path) when path?.EndsWith("/import")==true=>"CSV 가져오기",
            ("POST",_)=>"직원 추가",
            ("PUT",_)=>"직원 수정",
            ("DELETE",var path) when path?.EndsWith("/all")==true=>"전체 삭제",
            ("DELETE",_)=>"직원 삭제",
            _=>"사원 DB 변경"
        };
        settingsDb.EmployeeDatabaseChanges.Add(new HRDashboard.Models.EmployeeDatabaseChange
        {
            OccurredAtUtc=DateTimeOffset.UtcNow,
            UserName=context.User.Identity?.Name??"알 수 없음",
            DatabaseDate=databases.SelectedDate,
            Action=action,
            Detail=$"{context.Request.Method} {context.Request.Path} · HTTP {context.Response.StatusCode}"
        });
        await settingsDb.SaveChangesAsync(context.RequestAborted);
    }
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "css")),
    RequestPath = "/css"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "images")),
    RequestPath = "/images"
});

app.MapDashboardEndpoints();
app.MapManagementEndpoints();
app.MapSettingsEndpoints();
app.MapAuthenticationEndpoints();

var webRoot = app.Environment.WebRootPath;
app.MapGet("/login", () => Results.File(Path.Combine(webRoot, "login.html"), "text/html; charset=utf-8"))
    .AllowAnonymous();
app.MapGet("/login.html", () => Results.File(Path.Combine(webRoot, "login.html"), "text/html; charset=utf-8"))
    .AllowAnonymous();
app.MapGet("/", () => Results.File(Path.Combine(webRoot, "index.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/index.html", () => Results.File(Path.Combine(webRoot, "index.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/organization.html", () => Results.File(Path.Combine(webRoot, "organization.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/management.html", () => Results.File(Path.Combine(webRoot, "management.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/settings.html", () => Results.File(Path.Combine(webRoot, "settings.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/dashboard.js", () => Results.File(Path.Combine(webRoot, "js", "dashboard.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/organization.js", () => Results.File(Path.Combine(webRoot, "js", "organization.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/management.js", () => Results.File(Path.Combine(webRoot, "js", "management.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/settings.js", () => Results.File(Path.Combine(webRoot, "js", "settings.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/theme.js", () => Results.File(Path.Combine(webRoot, "js", "theme.js"), "text/javascript; charset=utf-8"))
    .AllowAnonymous();

await using (var scope = app.Services.CreateAsyncScope())
{
    var settingsDb=scope.ServiceProvider.GetRequiredService<CommonSettingsDbContext>();
    await settingsDb.Database.EnsureCreatedAsync();
    await settingsDb.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS Users (
          Id INTEGER NOT NULL CONSTRAINT PK_Users PRIMARY KEY AUTOINCREMENT,
          LoginId TEXT NOT NULL,
          PasswordHash TEXT NOT NULL,
          Role TEXT NOT NULL,
          Theme TEXT NOT NULL DEFAULT 'light',
          IsActive INTEGER NOT NULL DEFAULT 1,
          CreatedAtUtc TEXT NOT NULL,
          UpdatedAtUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_LoginId ON Users (LoginId);
        CREATE TABLE IF NOT EXISTS EmployeeDatabaseChanges (
          Id INTEGER NOT NULL CONSTRAINT PK_EmployeeDatabaseChanges PRIMARY KEY AUTOINCREMENT,
          OccurredAtUtc TEXT NOT NULL,
          UserName TEXT NOT NULL,
          DatabaseDate TEXT NOT NULL,
          Action TEXT NOT NULL,
          Detail TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_EmployeeDatabaseChanges_OccurredAtUtc ON EmployeeDatabaseChanges (OccurredAtUtc);
        """);
    var settings=scope.ServiceProvider.GetRequiredService<EmployeeColumnSettingsService>();
    await settings.EnsureSeededAsync();
    var salaryPositions=scope.ServiceProvider.GetRequiredService<SalaryPositionAxisSettingsService>();
    await salaryPositions.GetAsync();
    var accounts=scope.ServiceProvider.GetRequiredService<UserAccountService>();
    await accounts.EnsureAdministratorAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var managementDb=scope.ServiceProvider.GetRequiredService<ManagementDbContext>();
    await managementDb.Database.EnsureCreatedAsync();
    var managementConnection=managementDb.Database.GetDbConnection();
    await managementConnection.OpenAsync();
    var managementColumns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using(var schemaCommand=managementConnection.CreateCommand())
    {
        schemaCommand.CommandText="PRAGMA table_info('FinancialReports')";
        await using var schemaReader=await schemaCommand.ExecuteReaderAsync();
        while(await schemaReader.ReadAsync())managementColumns.Add(schemaReader.GetString(1));
    }
    if(!managementColumns.Contains("EmployeeCountIsEstimated"))
        await managementDb.Database.ExecuteSqlRawAsync("ALTER TABLE FinancialReports ADD COLUMN EmployeeCountIsEstimated INTEGER NOT NULL DEFAULT 0");
    if(!managementColumns.Contains("EmployeeCountBasis"))
        await managementDb.Database.ExecuteSqlRawAsync("ALTER TABLE FinancialReports ADD COLUMN EmployeeCountBasis TEXT NULL");

    if(!await managementDb.FinancialReports.AnyAsync())
    {
        var sourcePath=Path.Combine(dataDirectory,"hr-dashboard.db");
        if(File.Exists(sourcePath))
        {
            await using var migrate=managementConnection.CreateCommand();
            migrate.CommandText="""
                ATTACH DATABASE $source AS legacy;
                INSERT OR IGNORE INTO FinancialReports
                  (BusinessYear,ReportCode,ReportName,FsDiv,ReceiptNumber,Revenue,OperatingIncome,NetIncome,Assets,Liabilities,Equity,DartEmployeeCount,DartSalaryTotal,DartAverageSalary,SyncedAtUtc,EmployeeCountIsEstimated,EmployeeCountBasis)
                SELECT BusinessYear,ReportCode,ReportName,FsDiv,ReceiptNumber,Revenue,OperatingIncome,NetIncome,Assets,Liabilities,Equity,DartEmployeeCount,DartSalaryTotal,DartAverageSalary,SyncedAtUtc,0,
                       CASE WHEN DartEmployeeCount IS NOT NULL THEN '기존 DART 직원현황' ELSE NULL END
                FROM legacy.FinancialReports;
                DETACH DATABASE legacy;
                """;
            var parameter=migrate.CreateParameter();parameter.ParameterName="$source";parameter.Value=sourcePath;migrate.Parameters.Add(parameter);
            try{await migrate.ExecuteNonQueryAsync();}catch(Microsoft.Data.Sqlite.SqliteException){ }
        }
    }
    if(await managementDb.FinancialReports.AnyAsync())
    {
        var employeeDb=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await employeeDb.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS FinancialReports");
    }
}

app.Run();

public partial class Program;
