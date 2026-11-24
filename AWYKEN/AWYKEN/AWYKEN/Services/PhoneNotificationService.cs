using AWYKEN.Shared.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
#elif IOS || MACCATALYST
using Foundation;
using UIKit;
using UserNotifications;
#endif

namespace AWYKEN;

public sealed class PhoneNotificationService : IPhoneNotificationService
{
    private const string ChannelId = "awyken_alerts";
    private const string ChannelName = "AWYKEN alerts";
    private const string DeliveryPreferenceKey = "awyken_phone_alert_deliveries_";

#if IOS || MACCATALYST
    private static readonly UNUserNotificationCenterDelegate ForegroundDelegate = new AwykenNotificationDelegate();
#endif

    public bool IsSupported => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst();

    public async Task<bool> IsPermissionGrantedAsync()
    {
#if ANDROID
        return await Permissions.CheckStatusAsync<NotificationPermission>() == PermissionStatus.Granted;
#elif IOS || MACCATALYST
        var settings = await GetNotificationSettingsAsync();
        return settings.AuthorizationStatus is UNAuthorizationStatus.Authorized
            or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral;
#else
        return false;
#endif
    }

    public async Task<bool> RequestPermissionAsync()
    {
#if ANDROID
        return await Permissions.RequestAsync<NotificationPermission>() == PermissionStatus.Granted;
#elif IOS || MACCATALYST
        SetForegroundNotificationDelegate();
        var completion = new TaskCompletionSource<bool>();
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge,
            (granted, error) => completion.TrySetResult(granted && error is null));
        return await completion.Task;
#else
        return false;
#endif
    }

    public Task OpenSettingsAsync()
    {
#if ANDROID
        AppInfo.ShowSettingsUI();
#elif IOS || MACCATALYST
        MainThread.BeginInvokeOnMainThread(() =>
            UIApplication.SharedApplication.OpenUrl(new NSUrl(UIApplication.OpenSettingsUrlString)));
#endif
        return Task.CompletedTask;
    }

    public async Task NotifyAsync(IEnumerable<AppNotification> notifications)
    {
        if (!IsSupported || !await IsPermissionGrantedAsync())
            return;

        var today = DateTime.Today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var dailyPreferenceKey = DeliveryPreferenceKey + today;
        var delivered = Preferences.Default.Get(dailyPreferenceKey, "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var notification in notifications)
        {
            var deliveryKey = $"{today}:{notification.Id}";
            if (!delivered.Add(deliveryKey))
                continue;

            try
            {
#if ANDROID
                await ShowAndroidNotificationAsync(notification, deliveryKey);
#elif IOS || MACCATALYST
                await ShowAppleNotificationAsync(notification, deliveryKey);
#endif
                Preferences.Default.Set(dailyPreferenceKey, string.Join('\n', delivered));
            }
            catch
            {
                delivered.Remove(deliveryKey);
                throw;
            }
        }
    }

#if ANDROID
    private static Task ShowAndroidNotificationAsync(AppNotification notification, string deliveryKey)
    {
        var context = global::Android.App.Application.Context;
        if (context is null)
            return Task.CompletedTask;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null)
            return Task.CompletedTask;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Default));

        var id = Math.Abs(StringComparer.Ordinal.GetHashCode(deliveryKey) % int.MaxValue);
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        var pendingIntent = PendingIntent.GetActivity(context, id, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var message = FormatMessage(notification);
        var builder = new NotificationCompat.Builder(context, ChannelId);
        if (builder is null)
            return Task.CompletedTask;

        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetContentTitle(notification.Title);
        builder.SetContentText(message);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(message));
        builder.SetContentIntent(pendingIntent);
        builder.SetAutoCancel(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetPriority((int)NotificationCompat.PriorityDefault);

        var notificationManager = NotificationManagerCompat.From(context);
        notificationManager?.Notify(id, builder.Build());
        return Task.CompletedTask;
    }
#elif IOS || MACCATALYST
    private static async Task ShowAppleNotificationAsync(AppNotification notification, string deliveryKey)
    {
        SetForegroundNotificationDelegate();
        var content = new UNMutableNotificationContent
        {
            Title = notification.Title,
            Body = FormatMessage(notification),
            Sound = UNNotificationSound.Default,
            Badge = 1
        };
        var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(1, false);
        var request = UNNotificationRequest.FromIdentifier(deliveryKey, content, trigger);
        var completion = new TaskCompletionSource<bool>();
        UNUserNotificationCenter.Current.AddNotificationRequest(request, error =>
            completion.TrySetResult(error is null));
        if (!await completion.Task)
            throw new InvalidOperationException("The phone could not schedule this alert.");
    }

    private static Task<UNNotificationSettings> GetNotificationSettingsAsync()
    {
        SetForegroundNotificationDelegate();
        var completion = new TaskCompletionSource<UNNotificationSettings>();
        UNUserNotificationCenter.Current.GetNotificationSettings(settings => completion.TrySetResult(settings));
        return completion.Task;
    }

    private static void SetForegroundNotificationDelegate()
    {
        UNUserNotificationCenter.Current.Delegate = ForegroundDelegate;
    }

    private sealed class AwykenNotificationDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
        {
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
        }
    }
#endif

    private static string FormatMessage(AppNotification notification) =>
        string.IsNullOrWhiteSpace(notification.Time)
            ? notification.Message
            : $"{notification.Message} · {notification.Time}";
}

#if ANDROID
public sealed class NotificationPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? [(global::Android.Manifest.Permission.PostNotifications, true)]
            : [];
}
#endif
