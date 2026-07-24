namespace HRDashboard.Models;

public sealed class FinancialReport
{
    public long Id { get; set; }
    public int BusinessYear { get; set; }
    public required string ReportCode { get; set; }
    public required string ReportName { get; set; }
    public required string FsDiv { get; set; }
    public string? ReceiptNumber { get; set; }
    public long? Revenue { get; set; }
    public long? OperatingIncome { get; set; }
    public long? NetIncome { get; set; }
    public long? Assets { get; set; }
    public long? Liabilities { get; set; }
    public long? Equity { get; set; }
    public int? DartEmployeeCount { get; set; }
    public bool EmployeeCountIsEstimated { get; set; }
    public string? EmployeeCountBasis { get; set; }
    public long? DartSalaryTotal { get; set; }
    public long? DartAverageSalary { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
