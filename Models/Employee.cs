namespace HRDashboard.Models;

public sealed class Employee
{
    public long Id { get; set; }
    public required string EmployeeNumber { get; set; }
    public string? Workplace { get; set; }
    public string? ParentDepartment { get; set; }
    public string? Department { get; set; }
    public string? Name { get; set; }
    public string? Position { get; set; }
    public string? WorkShift { get; set; }
    public string? Duty { get; set; }
    public string? JobGroup { get; set; }
    public string? EmploymentType { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public long? AnnualSalary { get; set; }
    public long? MonthlyWage { get; set; }
    public string? Education { get; set; }
    public string? SchoolName { get; set; }
    public string? Major { get; set; }
}
