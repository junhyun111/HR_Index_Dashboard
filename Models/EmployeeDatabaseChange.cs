namespace HRDashboard.Models;

public sealed class EmployeeDatabaseChange
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string UserName { get; set; }
    public DateTime DatabaseDate { get; set; }
    public required string Action { get; set; }
    public required string Detail { get; set; }
}
