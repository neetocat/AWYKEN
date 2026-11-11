namespace AWYKEN.Shared.Data;

public sealed class AdminActivityLogRecord
{
    public int Id { get; set; }
    public int ActorProfileId { get; set; }
    public required string ActorEmail { get; set; }
    public required string Action { get; set; }
    public int? TargetProfileId { get; set; }
    public string? TargetEmail { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
