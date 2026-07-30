using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed record EmployeeMovementItem(long Id,string Name,DateTime Date,string? Department,string Type,bool CanDelete);
public sealed record EmployeeMovementResponse(
    IReadOnlyList<EmployeeMovementItem> Hires,
    IReadOnlyList<EmployeeMovementItem> Terminations);

public sealed class EmployeeMovementService(CommonSettingsDbContext movementsDb)
{
    public async Task SyncFromEmployeeDatabaseAsync(AppDbContext employeeDb,DateTime sourceDate,CancellationToken ct)
    {
        var today=DateTime.Today;
        var recentHireFrom=today.AddDays(-29);
        var employees=await employeeDb.Employees.AsNoTracking().ToListAsync(ct);

        var existingHires=await movementsDb.HireEmployees
            .Where(x=>x.HireDate>=recentHireFrom&&x.HireDate<=today)
            .ToListAsync(ct);
        var existingHireByKey=existingHires.ToDictionary(x=>(x.EmployeeNumber,x.HireDate.Date));
        foreach(var employee in employees.Where(x=>x.HireDate!=null))
        {
            var hireDate=employee.HireDate!.Value.Date;
            if(hireDate<recentHireFrom||hireDate>today)continue;
            if(existingHireByKey.TryGetValue((employee.EmployeeNumber,hireDate),out var existing))
            {
                existing.Name=employee.Name?.Trim()??existing.Name;
                existing.Department=employee.Department?.Trim();
                existing.Status="Completed";
                existing.AppliedAtUtc??=DateTimeOffset.UtcNow;
                continue;
            }
            movementsDb.HireEmployees.Add(new HireEmployee
            {
                EmployeeNumber=employee.EmployeeNumber,
                Name=employee.Name?.Trim()??employee.EmployeeNumber,
                Department=employee.Department?.Trim(),
                HireDate=hireDate,
                Source="EmployeeDatabase",
                Status="Completed",
                CreatedBy="시스템",
                CreatedAtUtc=DateTimeOffset.UtcNow,
                AppliedAtUtc=DateTimeOffset.UtcNow
            });
        }

        var terminationEmployees=employees.Where(x=>x.TerminationDate!=null).ToArray();
        var employeeNumbers=employees.Select(x=>x.EmployeeNumber).Distinct().ToArray();
        var existingTerminations=employeeNumbers.Length==0
            ?[]
            :await movementsDb.TerminationEmployees
                .Where(x=>employeeNumbers.Contains(x.EmployeeNumber))
                .ToListAsync(ct);
        var currentEmployeeByNumber=employees
            .GroupBy(x=>x.EmployeeNumber)
            .ToDictionary(x=>x.Key,x=>x.First());
        foreach(var existing in existingTerminations.Where(x=>x.TerminationDate>=today).ToArray())
        {
            var current=currentEmployeeByNumber[existing.EmployeeNumber];
            if(current.TerminationDate?.Date==existing.TerminationDate.Date)continue;
            movementsDb.TerminationEmployees.Remove(existing);
            existingTerminations.Remove(existing);
        }
        var existingTerminationByKey=existingTerminations
            .ToDictionary(x=>(x.EmployeeNumber,x.TerminationDate.Date));
        foreach(var employee in terminationEmployees)
        {
            var terminationDate=employee.TerminationDate!.Value.Date;
            if(existingTerminationByKey.TryGetValue((employee.EmployeeNumber,terminationDate),out var existing))
            {
                existing.Name=employee.Name?.Trim()??existing.Name;
                existing.Department=employee.Department?.Trim();
                existing.SourceDatabaseDate=sourceDate.Date;
                existing.SyncedAtUtc=DateTimeOffset.UtcNow;
                continue;
            }
            movementsDb.TerminationEmployees.Add(new TerminationEmployee
            {
                EmployeeNumber=employee.EmployeeNumber,
                Name=employee.Name?.Trim()??employee.EmployeeNumber,
                Department=employee.Department?.Trim(),
                TerminationDate=terminationDate,
                SourceDatabaseDate=sourceDate.Date,
                SyncedAtUtc=DateTimeOffset.UtcNow
            });
        }
        await movementsDb.SaveChangesAsync(ct);
    }

    public async Task<EmployeeMovementResponse> GetAsync(DateTime today,CancellationToken ct)
    {
        today=today.Date;
        var recentHireFrom=today.AddDays(-29);
        var terminationThrough=today.AddDays(30);
        var hires=await movementsDb.HireEmployees.AsNoTracking()
            .Where(x=>(x.Status=="Completed"&&x.HireDate>=recentHireFrom&&x.HireDate<=today)
                ||(x.Status=="Scheduled"&&x.HireDate>today))
            .OrderByDescending(x=>x.Status=="Scheduled")
            .ThenByDescending(x=>x.HireDate)
            .ThenBy(x=>x.Name)
            .Select(x=>new EmployeeMovementItem(x.Id,x.Name,x.HireDate,x.Department,
                x.Status=="Scheduled"?"입사예정자":"입사자",x.Status=="Scheduled"))
            .ToListAsync(ct);
        var terminations=await movementsDb.TerminationEmployees.AsNoTracking()
            .Where(x=>x.TerminationDate>=today&&x.TerminationDate<=terminationThrough)
            .OrderBy(x=>x.TerminationDate).ThenBy(x=>x.Name)
            .Select(x=>new EmployeeMovementItem(x.Id,x.Name,x.TerminationDate,x.Department,"퇴사예정자",false))
            .ToListAsync(ct);
        return new EmployeeMovementResponse(hires,terminations);
    }

    public async Task<HireEmployee> AddScheduledHireAsync(
        string employeeNumber,string name,string? department,DateTime hireDate,string createdBy,CancellationToken ct)
    {
        employeeNumber=employeeNumber.Trim();
        name=name.Trim();
        department=string.IsNullOrWhiteSpace(department)?null:department.Trim();
        hireDate=hireDate.Date;
        if(employeeNumber.Length==0)throw new ArgumentException("사번을 입력해 주세요.");
        if(name.Length==0)throw new ArgumentException("이름을 입력해 주세요.");
        if(hireDate<=DateTime.Today)throw new ArgumentException("입사예정일은 오늘 이후 날짜여야 합니다.");
        if(await movementsDb.HireEmployees.AnyAsync(x=>x.EmployeeNumber==employeeNumber&&x.HireDate==hireDate,ct))
            throw new InvalidOperationException("같은 사번과 입사일의 입사자가 이미 등록되어 있습니다.");
        var item=new HireEmployee
        {
            EmployeeNumber=employeeNumber,
            Name=name,
            Department=department,
            HireDate=hireDate,
            Source="User",
            Status="Scheduled",
            CreatedBy=createdBy,
            CreatedAtUtc=DateTimeOffset.UtcNow
        };
        movementsDb.HireEmployees.Add(item);
        await movementsDb.SaveChangesAsync(ct);
        return item;
    }

    public async Task<bool> DeleteScheduledHireAsync(long id,CancellationToken ct)
    {
        var item=await movementsDb.HireEmployees.FindAsync([id],ct);
        if(item==null||item.Status!="Scheduled")return false;
        movementsDb.HireEmployees.Remove(item);
        await movementsDb.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ApplyDueScheduledHiresAsync(AppDbContext employeeDb,DateTime today,CancellationToken ct)
    {
        today=today.Date;
        var due=await movementsDb.HireEmployees
            .Where(x=>x.Status=="Scheduled"&&x.HireDate<=today)
            .OrderBy(x=>x.HireDate).ThenBy(x=>x.Id)
            .ToListAsync(ct);
        if(due.Count==0)return 0;

        var applied=0;
        foreach(var item in due)
        {
            var employee=await employeeDb.Employees.FirstOrDefaultAsync(x=>x.EmployeeNumber==item.EmployeeNumber,ct);
            if(employee==null)
            {
                employeeDb.Employees.Add(new Employee
                {
                    EmployeeNumber=item.EmployeeNumber,
                    Name=item.Name,
                    Department=item.Department,
                    HireDate=item.HireDate
                });
                applied++;
            }
            item.Status="Completed";
            item.AppliedAtUtc=DateTimeOffset.UtcNow;
        }
        if(applied>0)
        {
            var state=await employeeDb.EmployeeDataStates.FindAsync([1],ct);
            if(state==null)
                employeeDb.EmployeeDataStates.Add(new EmployeeDataState
                {
                    Id=1,
                    UpdatedDate=today,
                    LastModifiedAt=DateTimeOffset.UtcNow
                });
            else
                state.LastModifiedAt=DateTimeOffset.UtcNow;
            await employeeDb.SaveChangesAsync(ct);
        }
        await movementsDb.SaveChangesAsync(ct);
        return applied;
    }
}
