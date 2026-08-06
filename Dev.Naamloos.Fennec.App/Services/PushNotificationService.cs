using System.Diagnostics;
using System.Collections.Concurrent;
using Dev.Naamloos.Fennec.Sdk;
#if ANDROID || IOS
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif
#if IOS
using UserNotifications;
#endif

namespace Dev.Naamloos.Fennec.App.Services;

public sealed class PushNotificationService
{
    private const string GatewayUrl = "https://fennec-notif.naamloos.dev/_matrix/push/v1/notify";

    private readonly ManagedMatrixClient _matrixClient;

#if ANDROID || IOS
    private readonly ConcurrentQueue<FCMNotification> _pendingEncryptedNotifications = [];
#endif

    public PushNotificationService(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

#if ANDROID || IOS
        CrossFirebaseCloudMessaging.Current.TokenChanged += OnTokenChanged;
        CrossFirebaseCloudMessaging.Current.NotificationReceived += OnNotificationReceived;
#endif
    }

    public Task InitializeAsync()
    {
#if ANDROID || IOS
        return InitializeMobileAsync();
#else
        return Task.CompletedTask;
#endif
    }

#if ANDROID || IOS
    private async Task InitializeMobileAsync()
    {
        try
        {
#if ANDROID
            if (
                OperatingSystem.IsAndroidVersionAtLeast(33)
                && AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                    Android.App.Application.Context,
                    Android.Manifest.Permission.PostNotifications
                ) != Android.Content.PM.Permission.Granted
            )
            {
                Platform.CurrentActivity?.RequestPermissions(
                    [Android.Manifest.Permission.PostNotifications],
                    0
                );
            }
#endif
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            await RegisterAsync(await CrossFirebaseCloudMessaging.Current.GetTokenAsync());

            while (_pendingEncryptedNotifications.TryDequeue(out var notification))
            {
                await HandleNotificationAsync(notification);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push notification setup failed: {exception}");
        }
    }

    private void OnTokenChanged(object? sender, FCMTokenChangedEventArgs args) =>
        _ = RegisterAsync(args.Token);

    private void OnNotificationReceived(object? sender, FCMNotificationReceivedEventArgs args) =>
        _ = HandleNotificationAsync(args.Notification);

    private async Task HandleNotificationAsync(FCMNotification notification)
    {
        if (
            !notification.Data.TryGetValue("resolve_encrypted", out var resolve)
            || resolve != "true"
            || !notification.Data.TryGetValue("room_id", out var roomId)
            || !notification.Data.TryGetValue("event_id", out var eventId)
        )
        {
            return;
        }

        if (!_matrixClient.IsLoggedIn)
        {
            _pendingEncryptedNotifications.Enqueue(notification);
            return;
        }

        var fallback = new MatrixNotificationPreview(
            notification.Data.TryGetValue("title", out var title) ? title : "Fennec",
            notification.Data.TryGetValue("body", out var body) ? body : "New Matrix message",
            DateTimeOffset.UtcNow
        );

        try
        {
            await ShowLocalNotificationAsync(
                await _matrixClient.ResolvePushNotificationAsync(roomId, eventId) ?? fallback,
                eventId
            );
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not resolve encrypted notification: {exception}");
            await ShowLocalNotificationAsync(fallback, eventId);
        }
    }

    private static Task ShowLocalNotificationAsync(
        MatrixNotificationPreview notification,
        string eventId
    )
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var builder = Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O
            ? new Android.App.Notification.Builder(context, "fennec_messages")
            : new Android.App.Notification.Builder(context);
        builder
            .SetSmallIcon(Android.Resource.Drawable.SymDefAppIcon)
            .SetContentTitle(notification.Title)
            .SetContentText(notification.Body)
            .SetWhen(notification.Timestamp.ToUnixTimeMilliseconds())
            .SetShowWhen(true)
            .SetAutoCancel(true);

        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
        if (intent is not null)
        {
            intent.SetFlags(Android.Content.ActivityFlags.ClearTop | Android.Content.ActivityFlags.SingleTop);
            builder.SetContentIntent(
                Android.App.PendingIntent.GetActivity(
                    context,
                    StringComparer.Ordinal.GetHashCode(eventId),
                    intent,
                    Android.App.PendingIntentFlags.Immutable
                        | Android.App.PendingIntentFlags.UpdateCurrent
                )
            );
        }

        (
            context.GetSystemService(Android.Content.Context.NotificationService)
                as Android.App.NotificationManager
        )?.Notify(StringComparer.Ordinal.GetHashCode(eventId), builder.Build());
        return Task.CompletedTask;
#elif IOS
        var content = new UNMutableNotificationContent
        {
            Title = notification.Title,
            Body = notification.Body,
            Sound = UNNotificationSound.Default,
        };
        return UNUserNotificationCenter.Current.AddNotificationRequestAsync(
            UNNotificationRequest.FromIdentifier(eventId, content, null)
        );
#else
        return Task.CompletedTask;
#endif
    }

    private async Task RegisterAsync(string token)
    {
        if (!_matrixClient.IsLoggedIn || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            await _matrixClient.SetPushNotificationsAsync(
                token,
                AppInfo.Current.PackageName,
                GatewayUrl
            );
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Matrix pusher registration failed: {exception}");
        }
    }
#endif
}
