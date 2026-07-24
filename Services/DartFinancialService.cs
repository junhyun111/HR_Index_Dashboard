using System.Globalization;
using System.Text.Json;
using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed class DartFinancialService(HttpClient client,IConfiguration configuration)
{
    private const string CorpCode="01264872";
    private static readonly (string Code,string Name)[] Reports=[("11013","1분기"),("11012","반기"),("11014","3분기"),("11011","연간")];
    public bool IsConfigured=>!string.IsNullOrWhiteSpace(configuration["DART_KEY"]);

    public async Task<DartSyncResult> SyncAsync(ManagementDbContext db,CancellationToken ct)
    {
        var key=configuration["DART_KEY"]; if(string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("DART_KEY가 설정되지 않았습니다.");
        var saved=0; var skipped=0; var currentYear=DateTime.Today.Year;
        var startYear=Math.Clamp(configuration.GetValue("Dart:StartYear",2015),2015,currentYear);
        for(var year=startYear;year<=currentYear;year++)
        foreach(var report in Reports)
        {
            var data=await Fetch(key,CorpCode,year,report.Code,"CFS",ct)??await Fetch(key,CorpCode,year,report.Code,"OFS",ct);
            if(data==null){skipped++;continue;}
            var employees=await FetchEmployees(key,year,report.Code,ct);
            var row=await db.FinancialReports.FirstOrDefaultAsync(x=>x.BusinessYear==year&&x.ReportCode==report.Code,ct);
            if(row==null){row=new FinancialReport{BusinessYear=year,ReportCode=report.Code,ReportName=report.Name,FsDiv=data.FsDiv};db.FinancialReports.Add(row);}
            row.ReportName=report.Name;row.FsDiv=data.FsDiv;row.ReceiptNumber=data.ReceiptNumber;row.Revenue=data.Revenue;row.OperatingIncome=data.OperatingIncome;
            row.NetIncome=data.NetIncome;row.Assets=data.Assets;row.Liabilities=data.Liabilities;row.Equity=data.Equity;
            row.DartEmployeeCount=employees?.EmployeeCount;row.EmployeeCountIsEstimated=false;row.EmployeeCountBasis=employees==null?null:"DART 직원현황";
            row.DartSalaryTotal=employees?.SalaryTotal;row.DartAverageSalary=employees?.AverageSalary;row.SyncedAtUtc=DateTime.UtcNow;saved++;
        }
        await db.SaveChangesAsync(ct);
        await FillMissingEmployeeCounts(db,ct);
        return new(saved,skipped,DateTime.UtcNow);
    }

    private static async Task FillMissingEmployeeCounts(ManagementDbContext db,CancellationToken ct)
    {
        var rows=await db.FinancialReports.OrderBy(x=>x.BusinessYear)
            .ThenBy(x=>x.ReportCode=="11013"?1:x.ReportCode=="11012"?2:x.ReportCode=="11014"?3:4).ToListAsync(ct);
        int Period(FinancialReport x)=>x.BusinessYear*4+(x.ReportCode=="11013"?0:x.ReportCode=="11012"?1:x.ReportCode=="11014"?2:3);
        var actual=rows.Where(x=>x.DartEmployeeCount>0&&!x.EmployeeCountIsEstimated).ToArray();
        foreach(var row in rows.Where(x=>x.DartEmployeeCount==null||x.DartEmployeeCount<=0))
        {
            var nearest=actual.OrderBy(x=>Math.Abs(Period(x)-Period(row))).ThenBy(x=>Period(x)).Take(2).ToArray();
            if(nearest.Length==0)continue;
            row.DartEmployeeCount=(int)Math.Round(nearest.Average(x=>x.DartEmployeeCount!.Value),MidpointRounding.AwayFromZero);
            row.EmployeeCountIsEstimated=true;
            row.EmployeeCountBasis=string.Join(", ",nearest.Select(x=>$"{x.BusinessYear} {x.ReportName}"));
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<FinancialData?> Fetch(string key,string corpCode,int year,string reportCode,string fsDiv,CancellationToken ct)
    {
        var url=$"api/fnlttSinglAcntAll.json?crtfc_key={Uri.EscapeDataString(key)}&corp_code={corpCode}&bsns_year={year}&reprt_code={reportCode}&fs_div={fsDiv}";
        using var response=await client.GetAsync(url,ct);response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));var root=json.RootElement;
        if(root.GetProperty("status").GetString()!="000"||!root.TryGetProperty("list",out var list))return null;
        var rows=list.EnumerateArray().ToArray(); if(rows.Length==0)return null;
        long? Find(string[] ids,string[] names)
        {
            var row=rows.FirstOrDefault(x=>ids.Any(id=>{var accountId=x.TryGetProperty("account_id",out var p)?p.GetString():"";return string.Equals(accountId,id,StringComparison.OrdinalIgnoreCase)||accountId?.EndsWith($"_{id}",StringComparison.OrdinalIgnoreCase)==true;}));
            if(row.ValueKind==JsonValueKind.Undefined)row=rows.FirstOrDefault(x=>names.Any(name=>string.Equals((x.TryGetProperty("account_nm",out var p)?p.GetString():"")?.Replace(" ",""),name,StringComparison.OrdinalIgnoreCase)));
            if(row.ValueKind==JsonValueKind.Undefined)return null;
            var cumulative=row.TryGetProperty("thstrm_add_amount",out var add)?ParseAmount(add.GetString()):null;
            if(cumulative!=null)return cumulative;
            return row.TryGetProperty("thstrm_amount",out var current)?ParseAmount(current.GetString()):null;
        }
        return new(fsDiv,rows[0].GetProperty("rcept_no").GetString(),Find(["Revenue"],["매출액","영업수익"]),Find(["OperatingIncomeLoss"],["영업이익"]),Find(["ProfitLoss"],["당기순이익","분기순이익"]),Find(["Assets"],["자산총계"]),Find(["Liabilities"],["부채총계"]),Find(["Equity"],["자본총계"]));
    }

    private async Task<EmployeeData?> FetchEmployees(string key,int year,string reportCode,CancellationToken ct)
    {
        var url=$"api/empSttus.json?crtfc_key={Uri.EscapeDataString(key)}&corp_code={CorpCode}&bsns_year={year}&reprt_code={reportCode}";
        using var response=await client.GetAsync(url,ct);response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));var root=json.RootElement;
        if(root.GetProperty("status").GetString()!="000"||!root.TryGetProperty("list",out var list))return null;
        long salaryTotal=0;int employeeCount=0;
        foreach(var item in list.EnumerateArray())
        {
            var salary=item.TryGetProperty("fyer_salary_totamt",out var salaryElement)?ParseAmount(salaryElement.GetString()):null;
            var count=item.TryGetProperty("sm",out var countElement)?ParseAmount(countElement.GetString()):null;
            if(salary!=null)salaryTotal+=salary.Value;
            if(count is >0 and <=int.MaxValue)employeeCount+=(int)count.Value;
        }
        return employeeCount==0?null:new(employeeCount,salaryTotal,(long)Math.Round(salaryTotal/(double)employeeCount));
    }

    private static long? ParseAmount(string? value)=>long.TryParse(value?.Replace(",",""),NumberStyles.Integer,CultureInfo.InvariantCulture,out var amount)?amount:null;
    private sealed record FinancialData(string FsDiv,string? ReceiptNumber,long? Revenue,long? OperatingIncome,long? NetIncome,long? Assets,long? Liabilities,long? Equity);
    private sealed record EmployeeData(int EmployeeCount,long SalaryTotal,long AverageSalary);
}
public sealed record DartSyncResult(int Saved,int Skipped,DateTime SyncedAtUtc);
