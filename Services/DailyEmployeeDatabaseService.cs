using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HRDashboard.Services;

public sealed record HeadcountTrendItem(string Label,int Value,DateTime TargetDate,DateTime? BasisDate);
public sealed record HeadcountTrendResponse(string Mode,DateTime EndDate,IReadOnlyList<HeadcountTrendItem> Items);

public sealed class DailyEmployeeDatabaseService(IWebHostEnvironment environment,IHttpContextAccessor httpContextAccessor)
{
    public DateTime SelectedDate
    {
        get
        {
            var request=httpContextAccessor.HttpContext?.Request;
            var value=request?.Headers["X-Employee-Date"].FirstOrDefault()??request?.Query["date"].FirstOrDefault();
            return DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
                && date.Year is >=2000 and <=2100 ? date.Date : DateTime.Today;
        }
    }

    public string PathFor(DateTime date)
    {
        var directory=Path.Combine(environment.ContentRootPath,"App_Data");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory,$"employee{date:yyMMdd}.db");
    }

    public string ConnectionStringForSelectedDate()=>$"Data Source={PathFor(SelectedDate)}";
    public bool SelectedDatabaseExists()=>File.Exists(PathFor(SelectedDate));

    public object[] AvailableDates()
    {
        var directory=Path.Combine(environment.ContentRootPath,"App_Data");
        if(!Directory.Exists(directory))return [];
        return Directory.GetFiles(directory,"employee??????.db")
            .Select(path=>new{Path=path,Name=Path.GetFileNameWithoutExtension(path)})
            .Select(x=>new{x.Path,Date=DateTime.TryParseExact(x.Name["employee".Length..],"yyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)?date:(DateTime?)null})
            .Where(x=>x.Date!=null).OrderByDescending(x=>x.Date)
            .Select(x=>(object)new{date=x.Date!.Value.ToString("yyyy-MM-dd"),fileName=Path.GetFileName(x.Path)})
            .ToArray();
    }

    public async Task<HeadcountTrendResponse> HeadcountTrendAsync(string? mode,CancellationToken ct)
    {
        var normalized=string.Equals(mode,"daily",StringComparison.OrdinalIgnoreCase)?"daily":"monthly";
        var endDate=SelectedDate.Date;
        var databases=AvailableDatabasePaths();
        var items=new List<HeadcountTrendItem>();
        if(normalized=="daily")
        {
            for(var date=endDate.AddDays(-14);date<=endDate;date=date.AddDays(1))
            {
                var count=databases.TryGetValue(date,out var path)?await CountEmployeesAsync(path,ct):0;
                items.Add(new HeadcountTrendItem(date.ToString("M.d",CultureInfo.GetCultureInfo("ko-KR")),count,date,databases.ContainsKey(date)?date:null));
            }
        }
        else
        {
            var currentMonth=new DateTime(endDate.Year,endDate.Month,1);
            for(var offset=11;offset>=0;offset--)
            {
                var month=currentMonth.AddMonths(-offset);
                var calendarMonthEnd=month.AddMonths(1).AddDays(-1);
                var targetDate=calendarMonthEnd>endDate?endDate:calendarMonthEnd;
                var basisDate=databases.Keys.Where(x=>x.Year==month.Year&&x.Month==month.Month&&x<=targetDate)
                    .OrderByDescending(x=>x).FirstOrDefault();
                var hasDatabase=basisDate!=default;
                var count=hasDatabase?await CountEmployeesAsync(databases[basisDate],ct):0;
                items.Add(new HeadcountTrendItem(month.ToString("yy.MM",CultureInfo.InvariantCulture),count,calendarMonthEnd,hasDatabase?basisDate:null));
            }
        }
        return new HeadcountTrendResponse(normalized,endDate,items);
    }

    private Dictionary<DateTime,string> AvailableDatabasePaths()
    {
        var directory=Path.Combine(environment.ContentRootPath,"App_Data");
        if(!Directory.Exists(directory))return [];
        var result=new Dictionary<DateTime,string>();
        foreach(var path in Directory.GetFiles(directory,"employee??????.db"))
        {
            var name=Path.GetFileNameWithoutExtension(path);
            var value=name["employee".Length..];
            if(value.Length==6&&int.TryParse(value[..2],out var year)&&int.TryParse(value.Substring(2,2),out var month)&&int.TryParse(value.Substring(4,2),out var day))
            {
                try{result[new DateTime(2000+year,month,day)]=path;}catch(ArgumentOutOfRangeException){ }
            }
        }
        return result;
    }

    private static async Task<int> CountEmployeesAsync(string path,CancellationToken ct)
    {
        try
        {
            await using var connection=new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            await connection.OpenAsync(ct);
            await using var command=connection.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM Employees";
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
        }
        catch(SqliteException)
        {
            return 0;
        }
    }
}
