namespace HRDashboard.Models;

public sealed class HireEmployee
{
    public long Id { get; set; }
    public required string EmployeeNumber { get; set; }
    public required string Name { get; set; }
    public string? Department { get; set; }
    public string? Position { get; set; }
    public DateTime HireDate { get; set; }
    public required string Source { get; set; }
    public required string Status { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
}
