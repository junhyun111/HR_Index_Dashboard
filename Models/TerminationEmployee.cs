namespace HRDashboard.Models;

public sealed class TerminationEmployee
{
    public long Id { get; set; }
    public required string EmployeeNumber { get; set; }
    public required string Name { get; set; }
    public string? Department { get; set; }
    public DateTime TerminationDate { get; set; }
    public DateTime SourceDatabaseDate { get; set; }
    public DateTimeOffset SyncedAtUtc { get; set; }
}
