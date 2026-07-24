namespace HRDashboard.Models;

public sealed class EmployeeDataState
{
    public int Id { get; set; }
    public DateTime UpdatedDate { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
}
