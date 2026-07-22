using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed class EmployeeImportService(
    AppDbContext db,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<EmployeeImportService> logger)
{
    public async Task<ImportResult> ImportIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Employees.AnyAsync(cancellationToken))
            return new ImportResult(0, false, "기존 데이터가 있어 가져오기를 건너뛰었습니다.");

        return await ImportAsync(false, cancellationToken);
    }

    public async Task<ImportResult> ImportAsync(bool replaceExisting, CancellationToken cancellationToken = default)
    {
        var configuredPath = configuration["EmployeeImport:Path"] ?? "js/innodep_hr_dummy_200.json";
        var path = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        if (!path.StartsWith(environment.ContentRootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("가져오기 파일은 애플리케이션 폴더 안에 있어야 합니다.");
        if (!File.Exists(path))
            throw new FileNotFoundException("직원 JSON 파일을 찾을 수 없습니다.", path);

        await using var stream = File.OpenRead(path);
        var rows = await JsonSerializer.DeserializeAsync<List<EmployeeJson>>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("JSON 최상위 값은 배열이어야 합니다.");
        var employees = rows.Select((row, index) => row.ToEntity(index + 1)).ToList();
        if (employees.Count == 0) throw new InvalidDataException("가져올 직원이 없습니다.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (replaceExisting)
            await db.Employees.ExecuteDeleteAsync(cancellationToken);
        else if (await db.Employees.AnyAsync(cancellationToken))
            return new ImportResult(0, false, "기존 데이터가 있어 가져오기를 중단했습니다.");

        db.Employees.AddRange(employees);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        stream.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        logger.LogInformation("직원 JSON 가져오기 완료: {Count}건, SHA256 {Hash}", employees.Count, hash);
        return new ImportResult(employees.Count, true, hash);
    }

    private sealed class EmployeeJson
    {
        [JsonPropertyName("회사명")] public string? CompanyName { get; set; }
        [JsonPropertyName("부서명")] public string? DepartmentName { get; set; }
        [JsonPropertyName("이름")] public string? Name { get; set; }
        [JsonPropertyName("직급")] public string? Grade { get; set; }
        [JsonPropertyName("직책")] public string? Position { get; set; }
        [JsonPropertyName("성별")] public string? Gender { get; set; }
        [JsonPropertyName("나이")] public int Age { get; set; }
        [JsonPropertyName("임금(월)")] public long MonthlySalary { get; set; }
        [JsonPropertyName("근속연수")] public double YearsOfService { get; set; }

        public Employee ToEntity(int row)
        {
            static string Required(string? value, string field, int rowNumber) =>
                !string.IsNullOrWhiteSpace(value) ? value.Trim() :
                    throw new InvalidDataException($"{rowNumber}행의 {field} 값이 없습니다.");

            if (Age is < 15 or > 100) throw new InvalidDataException($"{row}행의 나이가 올바르지 않습니다.");
            if (MonthlySalary < 0) throw new InvalidDataException($"{row}행의 임금이 올바르지 않습니다.");
            if (YearsOfService < 0) throw new InvalidDataException($"{row}행의 근속연수가 올바르지 않습니다.");

            return new Employee
            {
                CompanyName = Required(CompanyName, "회사명", row),
                DepartmentName = Required(DepartmentName, "부서명", row),
                Name = Required(Name, "이름", row),
                Grade = Required(Grade, "직급", row),
                Position = Required(Position, "직책", row),
                Gender = Required(Gender, "성별", row),
                Age = Age,
                MonthlySalary = MonthlySalary,
                YearsOfService = YearsOfService
            };
        }
    }
}

public sealed record ImportResult(int Count, bool Imported, string Message);
