using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Plugin.Firebase.CloudMessaging;

namespace Dev.Naamloos.Fennec.App
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize
            | ConfigChanges.Orientation
            | ConfigChanges.UiMode
            | ConfigChanges.ScreenLayout
            | ConfigChanges.SmallestScreenSize
            | ConfigChanges.Density
    )]
    public class MainActivity : MauiAppCompatActivity
    {
        private const string NotificationChannelId = "fennec_messages";

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    NotificationChannelId,
                    "Messages",
                    NotificationImportance.High
                );
                (
                    GetSystemService(NotificationService) as NotificationManager
                )?.CreateNotificationChannel(channel);
            }

            FirebaseCloudMessagingImplementation.ChannelId = NotificationChannelId;
            FirebaseCloudMessagingImplementation.OnNewIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            if (intent is not null)
            {
                FirebaseCloudMessagingImplementation.OnNewIntent(intent);
            }
        }
    }
}
