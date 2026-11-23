using System.Text.Json;
using AWYKEN.Shared.Models;
using AWYKEN.Shared.Services;

namespace AWYKEN.Shared.Data;

public sealed record ApiSignInRequest(string Email, string Password);
public sealed record ApiSignUpRequest(string DisplayName, string Email, string Password);
public sealed record ApiSessionReply(string? Error, string? AccessToken, int? ProfileId,
    bool IsAdmin, ApiDashboardSnapshot? Dashboard);
public sealed record ApiSessionSnapshot(int? ProfileId, bool IsAuthenticated, bool IsAdmin,
    ApiDashboardSnapshot Dashboard);
public sealed record ApiRpcRequest(string Method, JsonElement[] Arguments);
public sealed record ApiRpcReply(JsonElement? Result, ApiDashboardSnapshot? Dashboard);
public sealed record ApiCalendarReply(IReadOnlyList<UmakEvent> Events, UmakCalendarStatus Status);

/// <summary>Only UI state is sent back to the phone; no profile hashes or database rows.</summary>
public sealed record ApiDashboardSnapshot(
    UserInfo User,
    StudentProfile Student,
    CommuteLocationSettings CommuteLocations,
    WeatherInfo Weather,
    int? EstimatedTravelMinutes,
    IReadOnlyList<ScheduleItem> Schedule,
    IReadOnlyList<ScheduleItem> ImportPreview,
    StudentProfile? ImportProfilePreview)
{
    public static ApiDashboardSnapshot From(DashboardState state) => new(
        state.User, state.Student, state.CommuteLocations, state.Weather,
        state.EstimatedTravelMinutes, state.Schedule, state.ImportPreview,
        state.ImportProfilePreview);
}

/// <summary>Platform storage for the short-lived user token; the token is never an app secret.</summary>
public interface IApiTokenStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);
    Task SetAsync(string token, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
