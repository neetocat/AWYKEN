using System.Security.Cryptography;

namespace AWYKEN.Shared.Data;

/// <summary>Stores passwords as salted PBKDF2-SHA256 hashes, never as plain text.</summary>
internal static class LocalPasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Algorithm = "pbkdf2-sha256";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? encodedHash)
    {
        if (string.IsNullOrWhiteSpace(encodedHash))
            return false;

        var parts = encodedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != Algorithm
            || !int.TryParse(parts[1], out var iterations)
            || iterations is < 100_000 or > 2_000_000)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
