namespace AWYKEN.Shared.Data;

/// <summary>A school-wide message authored by an administrator.</summary>
public sealed class AnnouncementRecord
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsPublished { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}
