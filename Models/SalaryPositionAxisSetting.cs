namespace HRDashboard.Models;

public sealed class SalaryPositionAxisSetting
{
    public long Id { get; set; }
    public required string PositionName { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
