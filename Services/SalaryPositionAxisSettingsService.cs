using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed record SalaryPositionAxisResponse(long Id,string PositionName,int Order);

public sealed class SalaryPositionAxisSettingsService(CommonSettingsDbContext db)
{
    private static readonly string[] Defaults=["리더","선임","책임","수석","상무"];

    public async Task<SalaryPositionAxisResponse[]> GetAsync(CancellationToken ct=default)
    {
        await EnsureTableAndDefaultsAsync(ct);
        return await db.SalaryPositionAxisSettings.AsNoTracking()
            .OrderBy(x=>x.DisplayOrder).ThenBy(x=>x.Id)
            .Select(x=>new SalaryPositionAxisResponse(x.Id,x.PositionName,x.DisplayOrder))
            .ToArrayAsync(ct);
    }

    public async Task<SalaryPositionAxisResponse[]> UpdateAsync(IReadOnlyCollection<string?> requested,CancellationToken ct=default)
    {
        var names=requested.Select(x=>x?.Trim()??"").ToArray();
        if(names.Length==0)throw new ArgumentException("직위를 한 개 이상 등록해 주세요.");
        if(names.Length>20)throw new ArgumentException("직위는 최대 20개까지 등록할 수 있습니다.");
        if(names.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("직위 이름은 비워 둘 수 없습니다.");
        if(names.Any(x=>x.Length>50))throw new ArgumentException("직위 이름은 50자 이하여야 합니다.");
        var duplicate=names.GroupBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>x.Count()>1);
        if(duplicate!=null)throw new ArgumentException($"직위 이름이 중복되었습니다: {duplicate.Key}");

        await EnsureTableAndDefaultsAsync(ct);
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        db.SalaryPositionAxisSettings.RemoveRange(await db.SalaryPositionAxisSettings.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        var now=DateTimeOffset.UtcNow;
        db.SalaryPositionAxisSettings.AddRange(names.Select((name,index)=>new SalaryPositionAxisSetting
        {
            PositionName=name,
            DisplayOrder=index+1,
            UpdatedAtUtc=now
        }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(ct);
    }

    private async Task EnsureTableAndDefaultsAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS SalaryPositionAxisSettings (
              Id INTEGER NOT NULL CONSTRAINT PK_SalaryPositionAxisSettings PRIMARY KEY AUTOINCREMENT,
              PositionName TEXT NOT NULL,
              DisplayOrder INTEGER NOT NULL,
              UpdatedAtUtc TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_SalaryPositionAxisSettings_PositionName
              ON SalaryPositionAxisSettings (PositionName);
            """,ct);
        var now=DateTimeOffset.UtcNow;
        if(!await db.SalaryPositionAxisSettings.AnyAsync(ct))
            foreach(var item in Defaults.Select((name,index)=>(name,order:index+1)))
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT OR IGNORE INTO SalaryPositionAxisSettings (PositionName,DisplayOrder,UpdatedAtUtc) VALUES ({item.name},{item.order},{now})",ct);
    }
}
