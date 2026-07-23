using HRDashboard.Data;
using HRDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Endpoints;

public static class ManagementEndpoints
{
    public static IEndpointRouteBuilder MapManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api=endpoints.MapGroup("/api/management").RequireAuthorization("DashboardViewer");
        api.MapGet("",Dashboard);
        api.MapPost("/sync",Sync).RequireAuthorization("Editor");
        return endpoints;
    }

    private static async Task<IResult> Sync(AppDbContext db,DartFinancialService dart,CancellationToken ct)
    {
        if(!dart.IsConfigured)return Results.BadRequest(new{message="DART_KEY가 설정되지 않았습니다."});
        try{return Results.Ok(await dart.SyncAsync(db,ct));}
        catch(Exception e){return Results.BadRequest(new{message=$"DART 동기화 실패: {e.Message}"});}
    }

    private static async Task<IResult> Dashboard(AppDbContext db,CancellationToken ct)
    {
        var reports=await db.FinancialReports.AsNoTracking().OrderBy(x=>x.BusinessYear).ThenBy(x=>x.ReportCode=="11013"?1:x.ReportCode=="11012"?2:x.ReportCode=="11014"?3:4).ToListAsync(ct);
        var latest=reports.LastOrDefault();var today=DateTime.Today;
        var active=await db.Employees.AsNoTracking().Where(x=>x.TerminationDate==null||x.TerminationDate>=today).ToListAsync(ct);
        var headcount=active.Count;var monthlyPayroll=active.Where(x=>x.MonthlyWage!=null).Sum(x=>x.MonthlyWage??0);var wageCount=active.Count(x=>x.MonthlyWage!=null);
        double? Ratio(long? a,long? b)=>a!=null&&b is not null and not 0?Math.Round(a.Value/(double)b.Value*100,1):null;
        double? PerPerson(long? value)=>value!=null&&headcount>0?Math.Round(value.Value/(double)headcount):null;
        var annualPayroll=monthlyPayroll*12;
        return Results.Ok(new{
            company=new{name="이노뎁",stockCode="303530"},headcount,monthlyPayroll,wageCount,
            latest=latest==null?null:new{latest.BusinessYear,latest.ReportCode,latest.ReportName,latest.FsDiv,latest.ReceiptNumber,latest.Revenue,latest.OperatingIncome,latest.NetIncome,latest.Assets,latest.Liabilities,latest.Equity,latest.SyncedAtUtc,
                operatingMargin=Ratio(latest.OperatingIncome,latest.Revenue),debtRatio=Ratio(latest.Liabilities,latest.Equity),revenuePerEmployee=PerPerson(latest.Revenue),operatingIncomePerEmployee=PerPerson(latest.OperatingIncome),
                laborCostRatio=annualPayroll>0?Ratio(annualPayroll,latest.Revenue):null,laborRoi=annualPayroll>0&&latest.OperatingIncome!=null?(double?)Math.Round((latest.OperatingIncome.Value+annualPayroll)/(double)annualPayroll*100,1):null},
            reports=reports.Select(x=>new{x.BusinessYear,x.ReportCode,x.ReportName,x.FsDiv,x.ReceiptNumber,x.Revenue,x.OperatingIncome,x.NetIncome,x.Assets,x.Liabilities,x.Equity,x.SyncedAtUtc})
        });
    }
}
