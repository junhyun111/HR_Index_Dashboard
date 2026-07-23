using System.Globalization;
using System.Text;
using HRDashboard.Models;
using Microsoft.VisualBasic.FileIO;

namespace HRDashboard.Services;

public sealed class EmployeeCsvService
{
    public static readonly string[] Headers=["사업장","상위부서","부서","사번","성명","직위","근무조","직책","직군","사원구분","성별","생년월일","입사일자","퇴사일자","월임금"];
    private static readonly string[] RequiredHeaders=Headers.Where(x=>x is not "생년월일" and not "월임금").ToArray();
    public byte[] Export(IReadOnlyCollection<Employee> employees)
    {
        var csv=new StringBuilder(); csv.AppendLine(string.Join(',',Headers.Select(Escape)));
        foreach(var x in employees) { string[] values=[x.Workplace??"",x.ParentDepartment??"",x.Department??"",x.EmployeeNumber,x.Name??"",x.Position??"",x.WorkShift??"",x.Duty??"",x.JobGroup??"",x.EmploymentType??"",x.Gender??"",Date(x.BirthDate),Date(x.HireDate),Date(x.TerminationDate),x.MonthlyWage?.ToString(CultureInfo.InvariantCulture)??""]; csv.AppendLine(string.Join(',',values.Select(Escape))); }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
    public EmployeeImportResult Parse(Stream stream) { using var p=new TextFieldParser(stream,Encoding.UTF8,true,true){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};p.SetDelimiters(",");return Parse(p,"CSV"); }
    public EmployeeImportResult ParseClipboard(string text) { if(string.IsNullOrWhiteSpace(text))throw new EmployeeCsvException("붙여넣은 표가 비어 있습니다.");using var p=new TextFieldParser(new StringReader(text)){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};p.SetDelimiters("\t");return Parse(p,"붙여넣은 표"); }
    private static EmployeeImportResult Parse(TextFieldParser parser,string source)
    {
        var headers=parser.ReadFields()??throw new EmployeeCsvException($"{source}가 비어 있습니다.");if(headers.Length>0)headers[0]=headers[0].TrimStart('\uFEFF');
        var map=headers.Select((v,i)=>(v:v.Trim(),i)).GroupBy(x=>x.v).ToDictionary(x=>x.Key,x=>x.First().i);
        var missing=RequiredHeaders.Where(x=>!map.ContainsKey(x)).ToArray();if(missing.Length>0)throw new EmployeeCsvException($"필수 머리글이 없습니다: {string.Join(", ",missing)}");
        var rows=new List<EmployeeImportRow>();var errors=new List<string>();var n=1;
        while(!parser.EndOfData){n++;try{var f=parser.ReadFields()??[];if(f.All(string.IsNullOrWhiteSpace))continue;string V(string h)=>map.TryGetValue(h,out var i)&&i<f.Length?f[i].Trim():"";string? O(string h,int max){var v=V(h);if(v.Length>max)throw new EmployeeCsvException($"{n}행 [{h}]: {max}자를 초과했습니다.");return v.Length==0?null:v;}var no=V("사번");if(no.Length==0)throw new EmployeeCsvException($"{n}행 [사번]: 값이 비어 있습니다.");if(no.Length>50)throw new EmployeeCsvException($"{n}행 [사번]: 50자를 초과했습니다.");rows.Add(new(no,O("사업장",100),O("상위부서",100),O("부서",100),O("성명",100),O("직위",50),O("근무조",50),O("직책",50),O("직군",50),O("사원구분",50),O("성별",20),ParseDate(V("생년월일"),n,"생년월일"),ParseDate(V("입사일자"),n,"입사일자"),ParseDate(V("퇴사일자"),n,"퇴사일자"),ParseWage(V("월임금"),n)));}catch(Exception e)when(e is EmployeeCsvException or MalformedLineException){errors.Add(e.Message);}}
        if(errors.Count>0)throw new EmployeeCsvException(string.Join("\n",errors.Take(20)));if(rows.Count==0)throw new EmployeeCsvException("반영할 사원 데이터가 없습니다.");var dup=rows.GroupBy(x=>x.EmployeeNumber,StringComparer.OrdinalIgnoreCase).Where(x=>x.Count()>1).Select(x=>x.Key).ToArray();if(dup.Length>0)throw new EmployeeCsvException($"사번이 중복되었습니다: {string.Join(", ",dup.Take(20))}");return new(rows);
    }
    private static DateTime? ParseDate(string v,int row,string col){if(v.Length==0)return null;string[] formats=["yyyy-MM-dd","yyyy.MM.dd","yyyy/MM/dd","yyyyMMdd","M/d/yyyy","MM/dd/yyyy"];if(DateTime.TryParseExact(v,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)||DateTime.TryParse(v,CultureInfo.GetCultureInfo("ko-KR"),DateTimeStyles.None,out d))return d.Date;throw new EmployeeCsvException($"{row}행 [{col}]: 날짜 형식을 확인하세요 ({v}).");}
    private static long? ParseWage(string v,int row){if(v.Length==0)return null;v=v.Replace(",","").Replace("원","").Trim();if(long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out var wage)&&wage>=0)return wage;throw new EmployeeCsvException($"{row}행 [월임금]: 0 이상의 원 단위 숫자를 입력하세요.");}
    private static string Date(DateTime? d)=>d?.ToString("yyyy-MM-dd")??"";
    private static string Escape(string v)=>v.IndexOfAny([',','"','\r','\n'])>=0?$"\"{v.Replace("\"","\"\"")}\"":v;
}
public sealed record EmployeeImportResult(IReadOnlyList<EmployeeImportRow> Rows);
public sealed record EmployeeImportRow(string EmployeeNumber,string? Workplace,string? ParentDepartment,string? Department,string? Name,string? Position,string? WorkShift,string? Duty,string? JobGroup,string? EmploymentType,string? Gender,DateTime? BirthDate,DateTime? HireDate,DateTime? TerminationDate,long? MonthlyWage);
public sealed class EmployeeCsvException(string message):Exception(message);
