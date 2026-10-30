using AWYKEN.Shared.Services;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Shared database operations used by all pages, keeping their profile and
/// schedule values in sync with the same SQLite source of truth.
/// </summary>
public interface IAwykenDataService
{
    bool IsAuthenticated { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string?> SignUpAsync(string displayName, string email, string password, CancellationToken cancellationToken = default);
    Task<string?> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task SignOutAsync(DateOnly refreshDate, CancellationToken cancellationToken = default);
    Task RefreshAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task RefreshWeekAsync(DateOnly weekStart, CancellationToken cancellationToken = default);
    Task RefreshMonthAsync(DateOnly monthStart, CancellationToken cancellationToken = default);
    Task SaveScheduleItemAsync(ScheduleItem item, DateOnly date, CancellationToken cancellationToken = default);
    Task SaveImportedScheduleAsync(IEnumerable<ScheduleItem> items, DateOnly referenceDate, CancellationToken cancellationToken = default);
    Task SaveImportedProfileAsync(StudentProfile student, DateOnly referenceDate, CancellationToken cancellationToken = default);
    Task SaveCommuteLocationsAsync(CommuteLocationSettings locations, DateOnly refreshDate, CancellationToken cancellationToken = default);
    Task DeleteScheduleItemAsync(string id, DateOnly date, CancellationToken cancellationToken = default);
    Task SaveCourseNoteAsync(string scheduleId, DateOnly classDate, string text, string? news, string createdAtText,
        string? attendanceStatus, byte[]? imageData, string? imageContentType,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseNoteRecord>> GetCourseNotesAsync(CancellationToken cancellationToken = default);
    Task<CourseNoteRecord?> GetCourseNoteAsync(string scheduleId, DateOnly classDate,
        CancellationToken cancellationToken = default);
    Task UpdateCourseNoteAsync(int noteId, string text, string? news, string createdAtText,
        string? attendanceStatus, byte[]? imageData, string? imageContentType,
        CancellationToken cancellationToken = default);
}
