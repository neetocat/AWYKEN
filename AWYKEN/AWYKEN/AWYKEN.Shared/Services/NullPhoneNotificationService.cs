namespace AWYKEN.Shared.Services;

public sealed class NullPhoneNotificationService : IPhoneNotificationService
{
    public bool IsSupported => false;
    public Task<bool> IsPermissionGrantedAsync() => Task.FromResult(false);
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task OpenSettingsAsync() => Task.CompletedTask;
    public Task NotifyAsync(IEnumerable<AppNotification> notifications) => Task.CompletedTask;
}
