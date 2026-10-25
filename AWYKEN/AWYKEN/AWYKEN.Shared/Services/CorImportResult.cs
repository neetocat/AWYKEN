namespace AWYKEN.Shared.Services;

public sealed record CorImportResult(IReadOnlyList<ScheduleItem> Classes, string? Error, StudentProfile? Profile = null)
{
    public bool IsSuccess => Error is null && Classes.Count > 0;
    public static CorImportResult Success(IReadOnlyList<ScheduleItem> classes, StudentProfile? profile = null) => new(classes, null, profile);
    public static CorImportResult Failure(string error) => new([], error);
}