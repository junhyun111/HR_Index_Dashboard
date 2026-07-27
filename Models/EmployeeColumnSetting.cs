namespace HRDashboard.Models;

public sealed class EmployeeColumnSetting
{
    public required string ColumnKey { get; set; }
    public required string DisplayName { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
