using AWYKEN.Shared.Data;

namespace AWYKEN.Services;

/// <summary>Keeps the API transport separate from the public map and weather HTTP clients.</summary>
public sealed class MauiApiHttpClient(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

public sealed class MauiApiTokenStore : IApiTokenStore
{
    private const string TokenKey = "awyken.api.access-token";

    public Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync(TokenKey);
    }

    public Task SetAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.SetAsync(TokenKey, token);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(TokenKey);
        return Task.CompletedTask;
    }
}
