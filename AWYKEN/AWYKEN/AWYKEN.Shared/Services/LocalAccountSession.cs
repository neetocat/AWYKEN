namespace AWYKEN.Shared.Services;

/// <summary>Holds the signed-in profile for the current circuit; the web host restores it from its secure session cookie.</summary>
public sealed class LocalAccountSession
{
    public int? ProfileId { get; private set; }
    public bool IsAuthenticated => ProfileId is not null;

    public void SignIn(int profileId) => ProfileId = profileId;
    public void SignOut() => ProfileId = null;
}
