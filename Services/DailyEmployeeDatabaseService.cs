using System.Globalization;
using HRDashboard.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed record HeadcountTrendItem(string Label,int Value,DateTime TargetDate,DateTime? BasisDate);
public sealed record HeadcountTrendResponse(string Mode,DateTime EndDate,IReadOnlyList<HeadcountTrendItem> Items);
public sealed record EmployeeDatabaseCleanupResult(DateTime DatabaseDate,string FileName,bool Deleted,string? Error);
public sealed record EmployeeDatabaseCopyResult(DateTime SourceDate,DateTime DatabaseDate,string FileName);

public sealed class DailyEmployeeDatabaseService(IWebHostEnvironment environment,IHttpContextAccessor httpContextAccessor)
{
    private readonly object storageLock=new();
    private readonly SemaphoreSlim copyLock=new(1,1);

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
        return Path.Combine(DatabaseDirectory(),$"employee{date:yyMMdd}.db");
    }

    public string ConnectionStringForSelectedDate()=>$"Data Source={PathFor(SelectedDate)}";
    public bool SelectedDatabaseExists()=>File.Exists(PathFor(SelectedDate));

    public async Task<bool> SelectedDatabaseHasEmployeesTableAsync(CancellationToken ct)
    {
        return await DatabaseHasEmployeesTableAsync(PathFor(SelectedDate),ct);
    }

    public async Task<(DateTime Date,string Path)?> LatestDatabaseWithEmployeesAsync(CancellationToken ct)
    {
        foreach(var item in AvailableDatabasePaths()
            .Where(x=>x.Key<=SelectedDate.Date)
            .OrderByDescending(x=>x.Key))
        {
            if(await DatabaseHasEmployeesTableAsync(item.Value,ct))
                return (item.Key,item.Value);
        }
        return null;
    }

    public async Task<IReadOnlyList<EmployeeDatabaseCopyResult>> CreateMissingDatabasesThroughAsync(DateTime throughDate,CancellationToken ct)
    {
        await copyLock.WaitAsync(ct);
        try
        {
            var targetDate=throughDate.Date;
            var databases=AvailableDatabasePaths();
            var source=default(KeyValuePair<DateTime,string>);
            foreach(var candidate in databases.Where(x=>x.Key<=targetDate).OrderByDescending(x=>x.Key))
            {
                if(!await DatabaseHasEmployeesTableAsync(candidate.Value,ct))continue;
                source=candidate;
                break;
            }
            if(source.Key==default)return Array.Empty<EmployeeDatabaseCopyResult>();

            var results=new List<EmployeeDatabaseCopyResult>();
            for(var date=source.Key.AddDays(1);date<=targetDate;date=date.AddDays(1))
            {
                ct.ThrowIfCancellationRequested();
                var destinationPath=PathFor(date);
                if(File.Exists(destinationPath))
                {
                    if(await DatabaseHasEmployeesTableAsync(destinationPath,ct))
                    {
                        source=new KeyValuePair<DateTime,string>(date,destinationPath);
                        continue;
                    }
                }

                await CopyDatabaseAsync(source.Value,destinationPath,source.Key,date,ct);
                results.Add(new EmployeeDatabaseCopyResult(source.Key,date,Path.GetFileName(destinationPath)));
                source=new KeyValuePair<DateTime,string>(date,destinationPath);
            }
            return results;
        }
        finally
        {
            copyLock.Release();
        }
    }

    public async Task<bool> IsAutomaticallyUpdatedAsync(DateTime date,CancellationToken ct)
    {
        var path=PathFor(date);
        if(!File.Exists(path))return false;
        try
        {
            await using var connection=new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            await connection.OpenAsync(ct);
            await using var command=connection.CreateCommand();
            command.CommandText="""
                SELECT 1
                FROM sqlite_master
                WHERE type='table' AND name='EmployeeDatabaseMetadata'
                  AND EXISTS (
                    SELECT 1 FROM EmployeeDatabaseMetadata
                    WHERE Key='UpdateType' AND Value='Automatic'
                  )
                LIMIT 1
                """;
            return await command.ExecuteScalarAsync(ct)!=null;
        }
        catch(SqliteException)
        {
            return false;
        }
    }

    private static async Task<bool> DatabaseHasEmployeesTableAsync(string path,CancellationToken ct)
    {
        if(!File.Exists(path)) return false;
        try
        {
            await using var connection=new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            await connection.OpenAsync(ct);
            await using var command=connection.CreateCommand();
            command.CommandText="SELECT 1 FROM sqlite_master WHERE type='table' AND name='Employees' LIMIT 1";
            return await command.ExecuteScalarAsync(ct)!=null;
        }
        catch(SqliteException)
        {
            return false;
        }
    }

    private static async Task CopyDatabaseAsync(string sourcePath,string destinationPath,DateTime sourceDate,DateTime targetDate,CancellationToken ct)
    {
        var temporaryPath=destinationPath+$".tmp-{Guid.NewGuid():N}";
        var sourceConnectionString=new SqliteConnectionStringBuilder
        {
            DataSource=sourcePath,
            Mode=SqliteOpenMode.ReadOnly,
            Pooling=false
        }.ToString();
        var destinationConnectionString=new SqliteConnectionStringBuilder
        {
            DataSource=temporaryPath,
            Pooling=false
        }.ToString();
        try
        {
            await using(var source=new SqliteConnection(sourceConnectionString))
            await using(var destination=new SqliteConnection(destinationConnectionString))
            {
                await source.OpenAsync(ct);
                await destination.OpenAsync(ct);
                source.BackupDatabase(destination);

                await using(var stateSchema=destination.CreateCommand())
                {
                    stateSchema.CommandText="SELECT name FROM pragma_table_info('EmployeeDataState')";
                    var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    await using var reader=await stateSchema.ExecuteReaderAsync(ct);
                    while(await reader.ReadAsync(ct))columns.Add(reader.GetString(0));
                    await reader.CloseAsync();

                    await using var ensureState=destination.CreateCommand();
                    if(columns.Count==0)
                        ensureState.CommandText="""
                            CREATE TABLE EmployeeDataState (
                                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                                UpdatedDate TEXT NOT NULL,
                                LastModifiedAt TEXT NULL
                            )
                            """;
                    else if(!columns.Contains("LastModifiedAt"))
                        ensureState.CommandText="ALTER TABLE EmployeeDataState ADD COLUMN LastModifiedAt TEXT NULL";
                    if(ensureState.CommandText.Length>0)await ensureState.ExecuteNonQueryAsync(ct);
                }

                await using var metadata=destination.CreateCommand();
                metadata.CommandText="""
                    CREATE TABLE IF NOT EXISTS EmployeeDatabaseMetadata (
                        Key TEXT NOT NULL PRIMARY KEY,
                        Value TEXT NOT NULL
                    );
                    INSERT OR REPLACE INTO EmployeeDatabaseMetadata (Key,Value) VALUES ('UpdateType','Automatic');
                    INSERT OR REPLACE INTO EmployeeDatabaseMetadata (Key,Value) VALUES ('SourceDate',$sourceDate);
                    INSERT OR REPLACE INTO EmployeeDatabaseMetadata (Key,Value) VALUES ('UpdatedAtUtc',$updatedAtUtc);
                    INSERT OR IGNORE INTO EmployeeDataState (Id,UpdatedDate,LastModifiedAt)
                    VALUES (1,$targetDate,NULL);
                    UPDATE EmployeeDataState
                    SET UpdatedDate=$targetDate, LastModifiedAt=NULL
                    WHERE Id=1;
                    """;
                metadata.Parameters.AddWithValue("$sourceDate",sourceDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
                metadata.Parameters.AddWithValue("$targetDate",targetDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
                metadata.Parameters.AddWithValue("$updatedAtUtc",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture));
                await metadata.ExecuteNonQueryAsync(ct);
                await destination.CloseAsync();
            }
            File.Move(temporaryPath,destinationPath,true);
        }
        finally
        {
            if(File.Exists(temporaryPath))File.Delete(temporaryPath);
        }
    }

    public async Task MigrateSelectedDatabaseAsync(AppDbContext db,CancellationToken ct)
        =>await MigrateExistingDatabaseAsync(db,ct);

    public async Task MigrateExistingDatabaseAsync(AppDbContext db,CancellationToken ct)
    {
        // EnsureCreated로 만든 기존 날짜별 DB에는 EF 마이그레이션 이력이 없다.
        // 현재 스키마를 최초 마이그레이션 기준점으로 한 번만 등록해 이후 변경부터는 EF가 관리한다.
        var connection=db.Database.GetDbConnection();
        if(connection.State!=System.Data.ConnectionState.Open)await connection.OpenAsync(ct);
        await using var tableCheck=connection.CreateCommand();
        tableCheck.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Employees'";
        if(Convert.ToInt32(await tableCheck.ExecuteScalarAsync(ct))>0)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
                    MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                    ProductVersion TEXT NOT NULL
                );
                INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('20260728002109_InitialEmployeeDatabase', '10.0.10');
                """,ct);
        }
        await db.Database.MigrateAsync(ct);
    }

    public object[] AvailableDates()
    {
        var directory=DatabaseDirectory();
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

    public IReadOnlyList<EmployeeDatabaseCleanupResult> DeleteExpiredDatabases(DateTime today)
    {
        var cutoff=today.Date.AddYears(-3);
        var results=new List<EmployeeDatabaseCleanupResult>();
        foreach(var path in Directory.GetFiles(DatabaseDirectory(),"employee??????.db",SearchOption.TopDirectoryOnly))
        {
            if(!TryDatabaseDate(path,out var databaseDate)||databaseDate>=cutoff)continue;
            try
            {
                foreach(var suffix in new[]{"-wal","-shm"})
                {
                    var sidecar=path+suffix;
                    if(File.Exists(sidecar))File.Delete(sidecar);
                }
                File.Delete(path);
                results.Add(new EmployeeDatabaseCleanupResult(databaseDate,Path.GetFileName(path),!File.Exists(path),null));
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException)
            {
                results.Add(new EmployeeDatabaseCleanupResult(databaseDate,Path.GetFileName(path),false,e.Message));
            }
        }
        return results;
    }

    private Dictionary<DateTime,string> AvailableDatabasePaths()
    {
        var directory=DatabaseDirectory();
        var result=new Dictionary<DateTime,string>();
        foreach(var path in Directory.GetFiles(directory,"employee??????.db"))
        {
            if(TryDatabaseDate(path,out var date))result[date]=path;
        }
        return result;
    }

    private string DatabaseDirectory()
    {
        var dataDirectory=Path.Combine(environment.ContentRootPath,"App_Data");
        var employeeDirectory=Path.Combine(dataDirectory,"employee-daily");
        lock(storageLock)
        {
            Directory.CreateDirectory(employeeDirectory);
            foreach(var sourcePath in Directory.GetFiles(dataDirectory,"employee??????.db",SearchOption.TopDirectoryOnly))
            {
                var destinationPath=Path.Combine(employeeDirectory,Path.GetFileName(sourcePath));
                if(File.Exists(destinationPath))continue;
                File.Move(sourcePath,destinationPath);
                foreach(var suffix in new[]{"-wal","-shm"})
                {
                    var sourceSidecar=sourcePath+suffix;
                    var destinationSidecar=destinationPath+suffix;
                    if(File.Exists(sourceSidecar)&&!File.Exists(destinationSidecar))
                        File.Move(sourceSidecar,destinationSidecar);
                }
            }
        }
        return employeeDirectory;
    }

    private static bool TryDatabaseDate(string path,out DateTime date)
    {
        date=default;
        var name=Path.GetFileNameWithoutExtension(path);
        if(!name.StartsWith("employee",StringComparison.OrdinalIgnoreCase))return false;
        var value=name["employee".Length..];
        if(value.Length!=6||!int.TryParse(value[..2],out var year)||!int.TryParse(value.Substring(2,2),out var month)||!int.TryParse(value.Substring(4,2),out var day))return false;
        try{date=new DateTime(2000+year,month,day);return true;}catch(ArgumentOutOfRangeException){return false;}
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
