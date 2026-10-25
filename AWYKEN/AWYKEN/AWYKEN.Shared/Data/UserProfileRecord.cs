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
}
