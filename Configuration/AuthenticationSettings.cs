namespace HRDashboard.Configuration;

public sealed class AuthenticationSettings
{
    public string Mode { get; set; } = "Development";
    public GroupSettings Groups { get; set; } = new();
}

public sealed class GroupSettings
{
    public string DashboardViewer { get; set; } = "INNODEP\\HR-Dashboard-Viewer";
    public string SalaryViewer { get; set; } = "INNODEP\\HR-Salary-Viewer";
    public string Editor { get; set; } = "INNODEP\\HR-Dashboard-Editor";
    public string Administrator { get; set; } = "INNODEP\\HR-Dashboard-Admin";
}
