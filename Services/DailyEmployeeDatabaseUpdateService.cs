using HRDashboard.Data;
using HRDashboard.Models;

namespace HRDashboard.Services;

public sealed class DailyEmployeeDatabaseUpdateService(
    DailyEmployeeDatabaseService databases,
    IServiceScopeFactory scopeFactory,
    ILogger<DailyEmployeeDatabaseUpdateService> logger) : BackgroundService
{
    private static readonly TimeZoneInfo KoreaTimeZone=FindKoreaTimeZone();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await UpdateAsync(KoreaToday(),stoppingToken);
        while(!stoppingToken.IsCancellationRequested)
        {
            var delay=DelayUntilNextMidnight();
            await Task.Delay(delay,stoppingToken);
            await UpdateAsync(KoreaToday(),stoppingToken);
        }
    }

    private async Task UpdateAsync(DateTime today,CancellationToken ct)
    {
        try
        {
            var results=await databases.CreateMissingDatabasesThroughAsync(today,ct);
            await using var scope=scopeFactory.CreateAsyncScope();
            var settingsDb=scope.ServiceProvider.GetRequiredService<CommonSettingsDbContext>();
            foreach(var item in results)
            {
                logger.LogInformation("전날 사원 DB를 다음날 DB로 자동 저장했습니다. Source={SourceDate}, File={FileName}",item.SourceDate,item.FileName);
                settingsDb.EmployeeDatabaseChanges.Add(new EmployeeDatabaseChange
                {
                    OccurredAtUtc=DateTimeOffset.UtcNow,
                    UserName="시스템",
                    DatabaseDate=item.DatabaseDate,
                    Action="일일 DB 자동 업데이트",
                    Detail=$"{item.SourceDate:yyyy-MM-dd} DB를 {item.FileName}으로 자동 저장"
                });
            }

            var employeeDb=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await databases.MigrateExistingDatabaseAsync(employeeDb,ct);
            var movements=scope.ServiceProvider.GetRequiredService<EmployeeMovementService>();
            var applied=await movements.ApplyDueScheduledHiresAsync(employeeDb,today,ct);
            if(applied>0)
            {
                logger.LogInformation("입사예정자 {Count}명을 당일 사원 DB에 자동 등록했습니다. Date={Date}",applied,today);
                settingsDb.EmployeeDatabaseChanges.Add(new EmployeeDatabaseChange
                {
                    OccurredAtUtc=DateTimeOffset.UtcNow,
                    UserName="시스템",
                    DatabaseDate=today,
                    Action="입사예정자 자동 등록",
                    Detail=$"입사일이 도래한 입사예정자 {applied}명을 사원 DB에 자동 등록"
                });
            }
            await settingsDb.SaveChangesAsync(ct);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested)
        {
        }
        catch(Exception e)
        {
            logger.LogError(e,"사원 DB 일일 자동 업데이트에 실패했습니다. Date={Date}",today);
        }
    }

    private static DateTime KoreaToday()=>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,KoreaTimeZone).Date;

    private static TimeSpan DelayUntilNextMidnight()
    {
        var now=TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,KoreaTimeZone);
        var nextLocal=DateTime.SpecifyKind(now.Date.AddDays(1),DateTimeKind.Unspecified);
        var nextUtc=TimeZoneInfo.ConvertTimeToUtc(nextLocal,KoreaTimeZone);
        var delay=nextUtc-DateTime.UtcNow;
        return delay>TimeSpan.Zero?delay:TimeSpan.FromSeconds(1);
    }

    private static TimeZoneInfo FindKoreaTimeZone()
    {
        foreach(var id in new[]{"Asia/Seoul","Korea Standard Time"})
            try{return TimeZoneInfo.FindSystemTimeZoneById(id);}catch(TimeZoneNotFoundException){}catch(InvalidTimeZoneException){}
        return TimeZoneInfo.Local;
    }
}
