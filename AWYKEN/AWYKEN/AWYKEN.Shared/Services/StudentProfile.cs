namespace AWYKEN.Shared.Services;

/// <summary>
/// Student details stored for the signed-in user's profile.
/// </summary>
public sealed record StudentProfile(
    string? FullName, string? FirstName, string? Email, string? StudentNumber,
    string? College, string? Program, string? YearLevel, string? Section, string? AcademicTerm,
    string? Address = null, string? Guardian = null, string? Gender = null, string? DateOfBirth = null,
    string? Phone = null)
{
    public static StudentProfile Empty { get; } = new(null, null, null, null, null, null, null, null, null);

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(StudentNumber) || !string.IsNullOrWhiteSpace(Program);
}
