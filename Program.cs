using HRDashboard.Authentication;
using HRDashboard.Configuration;
using HRDashboard.Data;
using HRDashboard.Endpoints;
using HRDashboard.Models;
using HRDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var keyDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

var authenticationSettings = builder.Configuration
    .GetSection("Authentication").Get<AuthenticationSettings>() ?? new AuthenticationSettings();
builder.Services.Configure<AuthenticationSettings>(builder.Configuration.GetSection("Authentication"));

if (authenticationSettings.Mode.Equals("Windows", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}
else
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Development 인증은 Development 환경에서만 사용할 수 있습니다.");

    builder.Services.AddAuthentication("Development")
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>("Development", _ => { });
}

var groups = authenticationSettings.Groups;
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardViewer", policy =>
        policy.RequireRole(groups.DashboardViewer, groups.SalaryViewer, groups.Editor, groups.Administrator));
    options.AddPolicy("SalaryViewer", policy =>
        policy.RequireRole(groups.SalaryViewer, groups.Administrator));
    options.AddPolicy("Editor", policy =>
        policy.RequireRole(groups.Editor, groups.Administrator));
    options.AddPolicy("Administrator", policy =>
        policy.RequireRole(groups.Administrator));
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("SQLite 연결 문자열이 없습니다.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<EmployeeImportService>();
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
app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "css")),
    RequestPath = "/css"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "images")),
    RequestPath = "/images"
});

app.Use(async (context, next) =>
{
    await next();
    if (!context.Request.Path.StartsWithSegments("/api") || context.User.Identity?.IsAuthenticated != true)
        return;

    try
    {
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            UserName = context.User.Identity.Name ?? "unknown",
            Action = context.Request.Method,
            Path = context.Request.Path + context.Request.QueryString,
            StatusCode = context.Response.StatusCode
        });
        await db.SaveChangesAsync(context.RequestAborted);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(exception, "감사 로그 저장에 실패했습니다.");
    }
});

app.MapDashboardEndpoints();

var root = app.Environment.ContentRootPath;
app.MapGet("/", () => Results.File(Path.Combine(root, "index.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/index.html", () => Results.File(Path.Combine(root, "index.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/organization.html", () => Results.File(Path.Combine(root, "organization.html"), "text/html; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/dashboard.js", () => Results.File(Path.Combine(root, "js", "dashboard.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");
app.MapGet("/js/organization.js", () => Results.File(Path.Combine(root, "js", "organization.js"), "text/javascript; charset=utf-8"))
    .RequireAuthorization("DashboardViewer");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var importer = scope.ServiceProvider.GetRequiredService<EmployeeImportService>();
    var result = await importer.ImportIfEmptyAsync();
    app.Logger.LogInformation("초기 데이터 상태: {Message}", result.Message);
}

app.Run();

public partial class Program;
