namespace HRDashboard.Models;

public sealed class Employee
{
    public long Id { get; set; }
    public required string CompanyName { get; set; }
    public required string DepartmentName { get; set; }
    public required string Name { get; set; }
    public required string Grade { get; set; }
    public required string Position { get; set; }
    public required string Gender { get; set; }
    public int Age { get; set; }
    public long MonthlySalary { get; set; }
    public double YearsOfService { get; set; }
}
