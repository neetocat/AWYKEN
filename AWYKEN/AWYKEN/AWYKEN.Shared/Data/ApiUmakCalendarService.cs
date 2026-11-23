using System.Net.Http.Json;
using AWYKEN.Shared.Models;
using AWYKEN.Shared.Services;

namespace AWYKEN.Shared.Data;

/// <summary>Reads the server-owned shared calendar; the phone never connects to SQL.</summary>
public sealed class ApiUmakCalendarService(HttpClient httpClient) : IUmakCalendarService
{
    private UmakCalendarStatus _status = new(UmakCalendarSource.Bundled, null, null);

    public UmakCalendarStatus Status => _status;

    public Task<IReadOnlyList<UmakEvent>> GetEventsAsync(CancellationToken ct = default) =>
        GetEventsAsync(IUmakCalendarService.GetSchoolYear(UmakCalendarParser.TodayInManila()), ct);

    public async Task<IReadOnlyList<UmakEvent>> GetEventsAsync(int schoolYearStart, CancellationToken ct = default)
    {
        var result = await httpClient.GetFromJsonAsync<ApiCalendarReply>(
            $"api/v1/calendar/{schoolYearStart}", cancellationToken: ct);
        if (result is null)
            throw new InvalidOperationException("The AWYKEN server returned no school calendar data.");
        _status = result.Status;
        return result.Events;
    }

    public async Task<IReadOnlyList<int>> GetSchoolYearsAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<int>>("api/v1/calendar/years", cancellationToken: ct)
        ?? [];

    public void Refresh() => _status = new UmakCalendarStatus(UmakCalendarSource.Database, null, null);
}
