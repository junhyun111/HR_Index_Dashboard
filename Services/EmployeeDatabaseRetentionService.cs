using HRDashboard.Data;
using HRDashboard.Models;

namespace HRDashboard.Services;

public sealed class EmployeeDatabaseRetentionService(
    DailyEmployeeDatabaseService databases,
    IServiceScopeFactory scopeFactory,
    ILogger<EmployeeDatabaseRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer=new PeriodicTimer(TimeSpan.FromHours(24));
        while(await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var results=databases.DeleteExpiredDatabases(DateTime.Today);
        foreach(var failed in results.Where(x=>!x.Deleted))
            logger.LogWarning("만료된 사원 DB를 삭제하지 못했습니다. File={FileName}, Error={Error}",failed.FileName,failed.Error);
        var deleted=results.Where(x=>x.Deleted).ToArray();
        if(deleted.Length==0)return;

        await using var scope=scopeFactory.CreateAsyncScope();
        var settingsDb=scope.ServiceProvider.GetRequiredService<CommonSettingsDbContext>();
        var now=DateTimeOffset.UtcNow;
        foreach(var item in deleted)
        {
            logger.LogInformation("3년 보존기간이 지난 사원 DB를 삭제했습니다. File={FileName}",item.FileName);
            settingsDb.EmployeeDatabaseChanges.Add(new EmployeeDatabaseChange
            {
                OccurredAtUtc=now,
                UserName="시스템",
                DatabaseDate=item.DatabaseDate,
                Action="보존기간 만료 삭제",
                Detail=$"{item.FileName} · 3년 보존기간 만료"
            });
        }
        await settingsDb.SaveChangesAsync(ct);
    }
}
