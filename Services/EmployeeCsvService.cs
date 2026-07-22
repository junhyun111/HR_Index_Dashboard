using System.Globalization;
using System.Text;
using HRDashboard.Models;
using Microsoft.VisualBasic.FileIO;

namespace HRDashboard.Services;

public sealed class EmployeeCsvService
{
    private static readonly string[] Headers =
        ["직원 ID", "회사명", "부서명", "사원명", "직급", "직책", "성별", "나이", "월 임금", "근속연수"];

    public byte[] Export(IReadOnlyCollection<Employee> employees, bool includeSalary)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', Headers.Select(Escape)));
        foreach (var employee in employees)
        {
            var values = new[]
            {
                employee.Id.ToString(CultureInfo.InvariantCulture), employee.CompanyName,
                employee.DepartmentName, employee.Name, employee.Grade, employee.Position,
                employee.Gender, employee.Age.ToString(CultureInfo.InvariantCulture),
                includeSalary ? employee.MonthlySalary.ToString(CultureInfo.InvariantCulture) : "",
                employee.YearsOfService.ToString("0.0#", CultureInfo.InvariantCulture)
            };
            csv.AppendLine(string.Join(',', values.Select(Escape)));
        }
        var content = Encoding.UTF8.GetBytes(csv.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
        return result;
    }

    public EmployeeImportResult Parse(Stream stream, bool canEditSalary)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8, detectEncoding: true, leaveOpen: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        return ParseDelimited(parser, canEditSalary, "CSV");
    }

    public EmployeeImportResult ParseClipboard(string text, bool canEditSalary)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new EmployeeCsvException("붙여넣은 표가 비어 있습니다.");
        using var reader = new StringReader(text);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters("\t");
        return ParseDelimited(parser, canEditSalary, "붙여넣은 표");
    }

    private static EmployeeImportResult ParseDelimited(TextFieldParser parser, bool canEditSalary, string sourceName)
    {
        string[]? headers;
        try { headers = parser.ReadFields(); }
        catch (MalformedLineException exception) { throw new EmployeeCsvException($"{sourceName} 머리글 형식이 잘못되었습니다: {exception.Message}"); }
        if (headers is null) throw new EmployeeCsvException($"{sourceName}가 비어 있습니다.");
        if (headers.Length > 0) headers[0] = headers[0].TrimStart('\uFEFF');
        var headerMap = headers.Select((header, index) => new { Header = header.Trim(), Index = index })
            .GroupBy(x => x.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);
        var required = canEditSalary ? Headers : Headers.Where(x => x != "월 임금");
        var missing = required.Where(header => !headerMap.ContainsKey(header)).ToArray();
        if (missing.Length > 0) throw new EmployeeCsvException($"필수 열이 없습니다: {string.Join(", ", missing)}");

        var rows = new List<EmployeeImportRow>();
        var errors = new List<string>();
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            try
            {
                var fields = parser.ReadFields() ?? [];
                if (fields.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(ParseRow(fields, rowNumber, headerMap, canEditSalary));
            }
            catch (MalformedLineException exception) { errors.Add($"{rowNumber}행: 표 형식이 잘못되었습니다. {exception.Message}"); }
            catch (EmployeeCsvException exception) { errors.Add(exception.Message); }
        }
        if (errors.Count > 0) throw new EmployeeCsvException(string.Join("\n", errors.Take(20)));
        if (rows.Count == 0) throw new EmployeeCsvException("반영할 사원 데이터가 없습니다.");
        var duplicateIds = rows.Where(x => x.Id.HasValue).GroupBy(x => x.Id).Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicateIds.Length > 0) throw new EmployeeCsvException($"직원 ID가 중복되었습니다: {string.Join(", ", duplicateIds)}");
        return new EmployeeImportResult(rows);
    }

    private static EmployeeImportRow ParseRow(string[] fields, int rowNumber,
        IReadOnlyDictionary<string, int> columns, bool canEditSalary)
    {
        string Value(string header) => columns[header] < fields.Length ? fields[columns[header]].Trim() : "";
        string Text(string header, int maxLength)
        {
            var value = Value(header);
            if (string.IsNullOrWhiteSpace(value)) throw Error(header, "값이 비어 있습니다.");
            if (value.Length > maxLength) throw Error(header, $"{maxLength}자를 초과했습니다.");
            return value;
        }
        long? id = null;
        var idText = Value("직원 ID");
        if (!string.IsNullOrEmpty(idText))
        {
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0)
                throw Error("직원 ID", "양의 정수여야 합니다.");
            id = parsedId;
        }
        if (!int.TryParse(Value("나이"), out var age) || age is < 15 or > 100) throw Error("나이", "15~100 사이의 정수여야 합니다.");
        if (!double.TryParse(Value("근속연수"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tenure) || tenure is < 0 or > 80)
            throw Error("근속연수", "0~80 사이의 숫자여야 합니다.");
        long? salary = null;
        if (canEditSalary)
        {
            var salaryText = Value("월 임금").Replace(",", "");
            if (!long.TryParse(salaryText, out var parsedSalary) || parsedSalary < 0) throw Error("월 임금", "0 이상의 정수여야 합니다.");
            salary = parsedSalary;
        }
        return new EmployeeImportRow(id, Text("회사명", 100), Text("부서명", 100), Text("사원명", 50),
            Text("직급", 30), Text("직책", 50), Text("성별", 20), age, salary, tenure);
        EmployeeCsvException Error(string column, string message) => new($"{rowNumber}행 [{column}]: {message}");
    }

    private static string Escape(string? value)
    {
        value ??= "";
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

public sealed record EmployeeImportResult(IReadOnlyList<EmployeeImportRow> Rows);
public sealed record EmployeeImportRow(long? Id, string CompanyName, string DepartmentName, string Name,
    string Grade, string Position, string Gender, int Age, long? MonthlySalary, double YearsOfService);
public sealed class EmployeeCsvException(string message) : Exception(message);
