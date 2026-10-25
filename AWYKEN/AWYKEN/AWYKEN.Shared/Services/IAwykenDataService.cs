using AWYKEN.Shared.Services;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Shared database operations used by all pages, keeping their profile and
/// schedule values in sync with the same SQLite source of truth.
/// </summary>
public interface IAwykenDataService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task SaveScheduleItemAsync(ScheduleItem item, DateOnly date, CancellationToken cancellationToken = default);
    Task SaveImportedScheduleAsync(IEnumerable<ScheduleItem> items, DateOnly referenceDate, CancellationToken cancellationToken = default);
    Task SaveImportedProfileAsync(StudentProfile student, DateOnly referenceDate, CancellationToken cancellationToken = default);
    Task DeleteScheduleItemAsync(string id, DateOnly date, CancellationToken cancellationToken = default);
}
