namespace AWYKEN.Shared.Data;

/// <summary>
/// A local account profile. The default guest profile has a null email and password;
/// registered accounts store only a salted password hash.
/// </summary>
public sealed class UserProfileRecord
{
    public int Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string? FullName { get; set; }
    public string? StudentEmail { get; set; }
    public string? StudentNumber { get; set; }
    public string? College { get; set; }
    public string? Program { get; set; }
    public string? YearLevel { get; set; }
    public string? Section { get; set; }
    public string? AcademicTerm { get; set; }
    // Store both user-entered addresses and resolved coordinates to avoid repeat lookups.
    public string? HomeAddress { get; set; }
    public double? HomeLatitude { get; set; }
    public double? HomeLongitude { get; set; }
    public string? SchoolAddress { get; set; }
    public double? SchoolLatitude { get; set; }
    public double? SchoolLongitude { get; set; }
}
