using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed record EmployeeColumnDefinition(string Key,string DefaultName,int Order);
public sealed record EmployeeColumnSettingResponse(string Key,string DefaultName,string DisplayName,int Order);
public sealed record EmployeeColumnSettingUpdate(string Key,string DisplayName);

public sealed class EmployeeColumnSettingsService(CommonSettingsDbContext db)
{
    public static readonly EmployeeColumnDefinition[] Definitions =
    [
        new("workplace","사업장",1),
        new("parentDepartment","상위부서",2),
        new("department","부서",3),
        new("employeeNumber","사번",4),
        new("name","성명",5),
        new("position","직위",6),
        new("workShift","근무조",7),
        new("duty","직책",8),
        new("jobGroup","직군",9),
        new("employmentType","사원구분",10),
        new("gender","성별",11),
        new("birthDate","생년월일",12),
        new("hireDate","입사일자",13),
        new("terminationDate","퇴사일자",14),
        new("annualSalary","책정연봉",15),
        new("monthlyWage","월임금",16),
        new("education","최종학력",17),
        new("schoolName","학교명",18),
        new("major","전공",19)
    ];

    public async Task EnsureSeededAsync(CancellationToken ct=default)
    {
        var existing=await db.EmployeeColumnSettings.Select(x=>x.ColumnKey).ToHashSetAsync(ct);
        var now=DateTimeOffset.UtcNow;
        foreach(var definition in Definitions.Where(x=>!existing.Contains(x.Key)))
            db.EmployeeColumnSettings.Add(new EmployeeColumnSetting
            {
                ColumnKey=definition.Key,
                DisplayName=definition.DefaultName,
                DisplayOrder=definition.Order,
                UpdatedAtUtc=now
            });
        var legacyAnnualSalary=await db.EmployeeColumnSettings.FirstOrDefaultAsync(
            x=>x.ColumnKey=="annualSalary"&&x.DisplayName=="연봉",ct);
        if(legacyAnnualSalary!=null)
        {
            legacyAnnualSalary.DisplayName="책정연봉";
            legacyAnnualSalary.DisplayOrder=15;
            legacyAnnualSalary.UpdatedAtUtc=now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<EmployeeColumnSettingResponse[]> GetAsync(CancellationToken ct=default)
    {
        await EnsureSeededAsync(ct);
        var saved=await db.EmployeeColumnSettings.AsNoTracking().ToDictionaryAsync(x=>x.ColumnKey,ct);
        return Definitions.Select(x=>new EmployeeColumnSettingResponse(
            x.Key,x.DefaultName,saved.TryGetValue(x.Key,out var setting)?setting.DisplayName:x.DefaultName,x.Order)).ToArray();
    }

    public async Task<EmployeeColumnSettingResponse[]> UpdateAsync(IReadOnlyCollection<EmployeeColumnSettingUpdate> updates,CancellationToken ct=default)
    {
        var definitions=Definitions.ToDictionary(x=>x.Key,StringComparer.OrdinalIgnoreCase);
        var supplied=updates.GroupBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ToArray();
        if(supplied.Any(x=>x.Count()>1))throw new ArgumentException("같은 내부 키를 중복해서 저장할 수 없습니다.");
        if(supplied.Any(x=>!definitions.ContainsKey(x.Key)))throw new ArgumentException("알 수 없는 사원 DB 열이 포함되어 있습니다.");

        await EnsureSeededAsync(ct);
        var rows=await db.EmployeeColumnSettings.ToDictionaryAsync(x=>x.ColumnKey,StringComparer.OrdinalIgnoreCase,ct);
        var proposed=Definitions.ToDictionary(
            x=>x.Key,
            x=>rows.TryGetValue(x.Key,out var row)?row.DisplayName:x.DefaultName,
            StringComparer.OrdinalIgnoreCase);
        foreach(var update in updates)
        {
            var name=update.DisplayName?.Trim()??"";
            if(name.Length==0)throw new ArgumentException("열 이름은 비워 둘 수 없습니다.");
            if(name.Length>50)throw new ArgumentException("열 이름은 50자 이하여야 합니다.");
            proposed[update.Key]=name;
        }
        var duplicate=proposed.GroupBy(x=>x.Value,StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>x.Count()>1);
        if(duplicate!=null)throw new ArgumentException($"열 이름이 중복되었습니다: {duplicate.Key}");
        foreach(var item in proposed)
        {
            var conflicts=Definitions.FirstOrDefault(x=>!x.Key.Equals(item.Key,StringComparison.OrdinalIgnoreCase)
                && x.DefaultName.Equals(item.Value,StringComparison.OrdinalIgnoreCase));
            if(conflicts!=null)throw new ArgumentException($"'{item.Value}'은(는) 다른 열의 기본 이름이므로 사용할 수 없습니다.");
        }

        var now=DateTimeOffset.UtcNow;
        foreach(var update in updates)
        {
            var row=rows[update.Key];
            row.DisplayName=update.DisplayName.Trim();
            row.UpdatedAtUtc=now;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<EmployeeColumnSettingResponse[]> ResetAsync(CancellationToken ct=default)
    {
        await EnsureSeededAsync(ct);
        var rows=await db.EmployeeColumnSettings.ToDictionaryAsync(x=>x.ColumnKey,ct);
        var now=DateTimeOffset.UtcNow;
        foreach(var definition in Definitions)
        {
            rows[definition.Key].DisplayName=definition.DefaultName;
            rows[definition.Key].DisplayOrder=definition.Order;
            rows[definition.Key].UpdatedAtUtc=now;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string,string>> DisplayNamesByDefaultAsync(CancellationToken ct=default)
        =>(await GetAsync(ct)).ToDictionary(x=>x.DefaultName,x=>x.DisplayName,StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyDictionary<string,string>> HeaderAliasesAsync(CancellationToken ct=default)
    {
        var settings=await GetAsync(ct);
        var aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var setting in settings)
        {
            aliases[setting.DefaultName]=setting.DefaultName;
            aliases[setting.DisplayName]=setting.DefaultName;
        }
        // 기존 양식도 계속 업로드할 수 있도록 이전 머리글을 허용한다.
        aliases["연봉"]="책정연봉";
        return aliases;
    }
}
