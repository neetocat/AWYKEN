namespace AWYKEN;

public sealed record MobileApiConfiguration(string? BaseUrl, string? ErrorMessage)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
