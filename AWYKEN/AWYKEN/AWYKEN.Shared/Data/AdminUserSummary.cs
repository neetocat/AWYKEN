namespace AWYKEN.Shared.Data;

public sealed record AdminUserSummary(
    int Id,
    string? DisplayName,
    string Email,
    bool IsAdmin,
    bool IsDisabled);
