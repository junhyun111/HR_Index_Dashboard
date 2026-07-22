namespace HRDashboard.Models;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string UserName { get; set; }
    public required string Action { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
}
