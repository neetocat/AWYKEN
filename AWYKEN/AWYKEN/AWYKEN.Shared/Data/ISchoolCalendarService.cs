namespace AWYKEN.Shared.Data;

/// <summary>
/// The admin-managed school calendar: holidays, suspensions, semester breaks, examinations,
/// and the semester ranges that Academic Management uses for term dates.
/// </summary>
public interface ISchoolCalendarService
{
    Task<IReadOnlyList<CalendarEventView>> GetEventsAsync(bool includeArchived = true,
        CancellationToken cancellationToken = default);
    Task<string?> SaveEventAsync(CalendarEventRecord calendarEvent,
        CancellationToken cancellationToken = default);
    Task<string?> SetPublishedAsync(int eventId, bool published,
        CancellationToken cancellationToken = default);
    Task<string?> ArchiveEventAsync(int eventId, bool archived,
        CancellationToken cancellationToken = default);
    Task<string?> DeleteEventAsync(int eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarFeedView>> GetFeedsAsync(bool includeArchived = true,
        CancellationToken cancellationToken = default);
    Task<string?> SaveFeedAsync(CalendarFeedRecord feed, CancellationToken cancellationToken = default);
    Task<string?> ArchiveFeedAsync(int feedId, bool archived,
        CancellationToken cancellationToken = default);
    /// <summary>Downloads an iCal feed and upserts its entries. Returns a summary message.</summary>
    Task<string?> ImportFeedAsync(int feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the start and end of a semester in the stored calendar, so a term can take its
    /// dates from the calendar instead of being typed by hand.
    /// </summary>
    Task<(DateOnly Start, DateOnly End)?> FindTermDatesAsync(string schoolYear, string semester,
        CancellationToken cancellationToken = default);
}
