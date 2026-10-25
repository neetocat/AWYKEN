namespace AWYKEN.Shared.Services;

/// <summary>
/// Student details read from a COR. Only what the app needs is kept; address,
/// birth date, gender, and guardian on the COR are intentionally not stored.
/// </summary>
public sealed record StudentProfile(
    string? FullName, string? FirstName, string? Email, string? StudentNumber,
    string? College, string? Program, string? YearLevel, string? Section, string? AcademicTerm)
{
    public static StudentProfile Empty { get; } = new(null, null, null, null, null, null, null, null, null);

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(StudentNumber) || !string.IsNullOrWhiteSpace(Program);
}