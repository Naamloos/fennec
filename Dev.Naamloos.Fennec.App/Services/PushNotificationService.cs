using System.Diagnostics;
using Dev.Naamloos.Fennec.Sdk;
#if ANDROID || IOS
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif

namespace Dev.Naamloos.Fennec.App.Services;

public sealed class PushNotificationService
{
    private const string GatewayUrl = "https://fennec-notif.naamloos.dev/_matrix/push/v1/notify";

    private readonly ManagedMatrixClient _matrixClient;

    public PushNotificationService(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

#if ANDROID || IOS
        CrossFirebaseCloudMessaging.Current.TokenChanged += OnTokenChanged;
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
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            await RegisterAsync(await CrossFirebaseCloudMessaging.Current.GetTokenAsync());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push notification setup failed: {exception}");
        }
    }

    private void OnTokenChanged(object? sender, FCMTokenChangedEventArgs args) =>
        _ = RegisterAsync(args.Token);

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
