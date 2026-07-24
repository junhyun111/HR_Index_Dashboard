using System.Globalization;

namespace HRDashboard.Services;

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
}
