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

    // Filled from the COR import.
    public string? FullName { get; set; }
    public string? StudentNumber { get; set; }
    public string? College { get; set; }
    public string? Program { get; set; }
    public string? YearLevel { get; set; }
    public string? Section { get; set; }
    public string? AcademicTerm { get; set; }
}