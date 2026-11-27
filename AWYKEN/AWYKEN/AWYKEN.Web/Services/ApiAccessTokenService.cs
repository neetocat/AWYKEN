using System.Security.Cryptography;
using System.Text;
using AWYKEN.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Web.Services;

/// <summary>Issues short-lived, user-scoped API tokens. The signing key exists only on the server.</summary>
public sealed class ApiAccessTokenService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    byte[] signingKey)
{
    public static TimeSpan TokenLifetime => TimeSpan.FromHours(8);

    public string Issue(int profileId)
    {
        var payload = $"{profileId}:{DateTimeOffset.UtcNow.Add(TokenLifetime).ToUnixTimeSeconds()}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
        var encoded = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(encoded));
        return $"{encoded}.{Base64Url(signature)}";
    }

    public async Task<int?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var parts = token.Split('.');
        if (parts.Length != 2)
            return null;

        byte[] suppliedSignature;
        byte[] payloadBytes;
        try
        {
            suppliedSignature = FromBase64Url(parts[1]);
            payloadBytes = FromBase64Url(parts[0]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(parts[0]));
        if (suppliedSignature.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expected))
            return null;

        var values = Encoding.UTF8.GetString(payloadBytes).Split(':');
        if (values.Length != 3
            || !int.TryParse(values[0], out var profileId)
            || !long.TryParse(values[1], out var expiresAt)
            || DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt)
            return null;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var stillActive = await db.Profiles.AsNoTracking().AnyAsync(profile =>
            profile.Id == profileId && !profile.IsDisabled && profile.AccountStatus == "Active",
            cancellationToken);
        return stillActive ? profileId : null;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
