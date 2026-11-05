namespace AWYKEN.Shared.Data;

/// <summary>
/// One account as shown on the <c>/admin</c> page. Deliberately carries no password or
/// PIN hash, so the admin view cannot leak credentials.
/// </summary>
public sealed record AdminAccountRow(
    int Id,
    string? DisplayName,
    string? Email,
    bool IsAdmin,
    int ClassCount,
    int NoteCount,
    int IncentiveCount)
{
    public bool IsGuest => Email is null;
}

/// <summary>A read-only snapshot of the whole database for the admin page.</summary>
public sealed record AdminOverview(
    int AccountCount,
    int ClassCount,
    int NoteCount,
    int IncentiveCount,
    IReadOnlyList<AdminAccountRow> Accounts)
{
    public static AdminOverview Empty { get; } = new(0, 0, 0, 0, []);
}
