namespace HRDashboard.Models;

public sealed class OrganizationNode
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? ParentId { get; set; }
    public int DisplayOrder { get; set; }
    public int LayoutX { get; set; }
    public int LayoutY { get; set; }
}

public sealed class OrganizationState
{
    public int Id { get; set; }=1;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string UpdatedBy { get; set; }
}
