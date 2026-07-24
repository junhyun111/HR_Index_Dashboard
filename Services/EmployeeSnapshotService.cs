using HRDashboard.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed class EmployeeSnapshotService(IWebHostEnvironment environment)
{
    private static readonly string[] KoreanDays=["일","월","화","수","목","금","토"];

    public static string FileName(DateTime date)=>
        $"hr-dashboard-{date:yyyy-MM-dd}-{KoreanDays[(int)date.DayOfWeek]}.db";

    public async Task<EmployeeSnapshotResult> SaveAsync(DateTime date,AppDbContext sourceDb,CancellationToken ct)
    {
        date=date.Date;
        if(date.Year is <2000 or >2100)throw new ArgumentOutOfRangeException(nameof(date),"저장 날짜는 2000년부터 2100년 사이여야 합니다.");
        var dataDirectory=Path.Combine(environment.ContentRootPath,"App_Data");
        Directory.CreateDirectory(dataDirectory);
        var targetPath=Path.GetFullPath(Path.Combine(dataDirectory,FileName(date)));
        if(!targetPath.StartsWith(Path.GetFullPath(dataDirectory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("허용되지 않은 DB 저장 경로입니다.");

        var employees=await sourceDb.Employees.AsNoTracking().ToListAsync(ct);
        var updatedDate=(await sourceDb.EmployeeDataStates.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==1,ct))?.UpdatedDate.Date??date;
        await using var target=new SqliteConnection($"Data Source={targetPath}");
        await target.OpenAsync(ct);
        await using var transaction=await target.BeginTransactionAsync(ct);
        await using var command=target.CreateCommand();
        command.Transaction=(SqliteTransaction)transaction;
        command.CommandText="""
            CREATE TABLE IF NOT EXISTS Employees (
              Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
              EmployeeNumber TEXT NOT NULL,
              Workplace TEXT NULL, ParentDepartment TEXT NULL, Department TEXT NULL, Name TEXT NULL,
              Position TEXT NULL, WorkShift TEXT NULL, Duty TEXT NULL, JobGroup TEXT NULL,
              EmploymentType TEXT NULL, Gender TEXT NULL, BirthDate TEXT NULL, HireDate TEXT NULL,
              TerminationDate TEXT NULL, MonthlyWage INTEGER NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Employees_EmployeeNumber ON Employees(EmployeeNumber);
            CREATE INDEX IF NOT EXISTS IX_Employees_Department ON Employees(Department);
            CREATE INDEX IF NOT EXISTS IX_Employees_Name ON Employees(Name);
            CREATE TABLE IF NOT EXISTS EmployeeDataState (Id INTEGER NOT NULL PRIMARY KEY, UpdatedDate TEXT NOT NULL);
            DELETE FROM Employees;
            DELETE FROM EmployeeDataState;
            """;
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText="""
            INSERT INTO Employees
              (Id,EmployeeNumber,Workplace,ParentDepartment,Department,Name,Position,WorkShift,Duty,JobGroup,EmploymentType,Gender,BirthDate,HireDate,TerminationDate,MonthlyWage)
            VALUES
              ($id,$number,$workplace,$parentDepartment,$department,$name,$position,$workShift,$duty,$jobGroup,$employmentType,$gender,$birthDate,$hireDate,$terminationDate,$monthlyWage)
            """;
        foreach(var employee in employees)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id",employee.Id);
            command.Parameters.AddWithValue("$number",employee.EmployeeNumber);
            command.Parameters.AddWithValue("$workplace",(object?)employee.Workplace??DBNull.Value);
            command.Parameters.AddWithValue("$parentDepartment",(object?)employee.ParentDepartment??DBNull.Value);
            command.Parameters.AddWithValue("$department",(object?)employee.Department??DBNull.Value);
            command.Parameters.AddWithValue("$name",(object?)employee.Name??DBNull.Value);
            command.Parameters.AddWithValue("$position",(object?)employee.Position??DBNull.Value);
            command.Parameters.AddWithValue("$workShift",(object?)employee.WorkShift??DBNull.Value);
            command.Parameters.AddWithValue("$duty",(object?)employee.Duty??DBNull.Value);
            command.Parameters.AddWithValue("$jobGroup",(object?)employee.JobGroup??DBNull.Value);
            command.Parameters.AddWithValue("$employmentType",(object?)employee.EmploymentType??DBNull.Value);
            command.Parameters.AddWithValue("$gender",(object?)employee.Gender??DBNull.Value);
            command.Parameters.AddWithValue("$birthDate",(object?)employee.BirthDate??DBNull.Value);
            command.Parameters.AddWithValue("$hireDate",(object?)employee.HireDate??DBNull.Value);
            command.Parameters.AddWithValue("$terminationDate",(object?)employee.TerminationDate??DBNull.Value);
            command.Parameters.AddWithValue("$monthlyWage",(object?)employee.MonthlyWage??DBNull.Value);
            await command.ExecuteNonQueryAsync(ct);
        }
        command.Parameters.Clear();
        command.CommandText="INSERT INTO EmployeeDataState(Id,UpdatedDate) VALUES(1,$updatedDate)";
        command.Parameters.AddWithValue("$updatedDate",updatedDate);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        await target.CloseAsync();
        SqliteConnection.ClearPool(target);
        return new(date,FileName(date),employees.Count);
    }
}

public sealed record EmployeeSnapshotResult(DateTime Date,string FileName,int EmployeeCount);
