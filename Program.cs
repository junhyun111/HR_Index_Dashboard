using HRDashboard.Configuration;
using HRDashboard.Data;
using HRDashboard.Endpoints;
using HRDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keyDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

builder.Services.AddOptions<LoginSettings>()
    .Bind(builder.Configuration.GetSection("Authentication"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.UserName) && !string.IsNullOrEmpty(x.Password),
        "로그인 아이디와 비밀번호 설정이 필요합니다.")
    .ValidateOnStart();
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
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardViewer", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("SalaryViewer", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Editor", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
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

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("SQLite 연결 문자열이 없습니다.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<EmployeeCsvService>();
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
app.MapGet("/js/dashboard.js", () => Results.File(Path.Combine(webRoot, "js", "dashboard.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/organization.js", () => Results.File(Path.Combine(webRoot, "js", "organization.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    await using var columnsCommand = connection.CreateCommand();
    columnsCommand.CommandText = "PRAGMA table_info('Employees')";
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using (var reader = await columnsCommand.ExecuteReaderAsync())
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
    if (!columns.Contains("BirthDate"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Employees ADD COLUMN BirthDate TEXT NULL");
    if (!columns.Contains("MonthlyWage"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Employees ADD COLUMN MonthlyWage INTEGER NULL");
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS EmployeeDataState (Id INTEGER NOT NULL PRIMARY KEY, UpdatedDate TEXT NOT NULL)");
    await db.Database.ExecuteSqlRawAsync("INSERT OR IGNORE INTO EmployeeDataState (Id, UpdatedDate) VALUES (1, date('now', 'localtime'))");
}

app.Run();

public partial class Program;
