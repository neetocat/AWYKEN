namespace AWYKEN.Shared.Services;

public interface IPhoneNotificationService
{
    bool IsSupported { get; }
    Task<bool> IsPermissionGrantedAsync();
    Task<bool> RequestPermissionAsync();
    Task OpenSettingsAsync();
    Task NotifyAsync(IEnumerable<AppNotification> notifications);
}
