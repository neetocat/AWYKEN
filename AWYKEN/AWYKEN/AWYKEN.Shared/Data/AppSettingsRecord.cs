namespace AWYKEN.Shared.Data;

public sealed class AppSettingsRecord
{
    public int Id { get; set; } = 1;
    public required string AppName { get; set; }
    public bool SignupsEnabled { get; set; } = true;
    public bool MaintenanceMode { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedByEmail { get; set; }
}

public sealed record AppSettingsSnapshot(
    string AppName,
    bool SignupsEnabled,
    bool MaintenanceMode,
    DateTime UpdatedAtUtc);
