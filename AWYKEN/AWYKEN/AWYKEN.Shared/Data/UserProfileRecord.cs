namespace AWYKEN.Shared.Data;

/// <summary>
/// The single local profile for this first SQLite version. A null DisplayName is
/// the database default and is presented to the UI as "Guest".
/// </summary>
public sealed class UserProfileRecord
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}
