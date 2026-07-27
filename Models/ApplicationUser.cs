namespace HRDashboard.Models;

public sealed class ApplicationUser
{
    public long Id { get; set; }
    public required string LoginId { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; }
    public required string Theme { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
