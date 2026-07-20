using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using AndroidSpecific =
    Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace Dev.Naamloos.Fennec.App
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly StartupPage _startupPage;

        public App(StartupPage startupPage)
        {
            On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
                .UseWindowSoftInputModeAdjust(
                    AndroidSpecific.WindowSoftInputModeAdjust.Resize);

            _startupPage = startupPage;
            InitializeComponent();
            ApplyThemePalette();
            RequestedThemeChanged += (_, _) => ApplyThemePalette();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(_startupPage);
        }

        public static void SetRootPage(Page page)
        {
            if (Current?.Windows.FirstOrDefault() is not { } window)
            {
                throw new InvalidOperationException(
                    "The application window is unavailable.");
            }

            window.Page = page;
        }

        private void ApplyThemePalette()
        {
            var dark = RequestedTheme == AppTheme.Dark;
            Resources["AppBackground"] = Color.FromArgb(dark ? "#101411" : "#F7FAF7");
            Resources["Surface"] = Color.FromArgb(dark ? "#181D19" : "#FFFFFF");
            Resources["SurfaceContainer"] = Color.FromArgb(dark ? "#202621" : "#EEF3EE");
            Resources["SurfaceContainerHigh"] = Color.FromArgb(dark ? "#2A312B" : "#E4EBE4");
            Resources["Outline"] = Color.FromArgb(dark ? "#8C938B" : "#727971");
            Resources["OnSurface"] = Color.FromArgb(dark ? "#E1E7E1" : "#171D18");
            Resources["OnSurfaceVariant"] = Color.FromArgb(dark ? "#C1C9C0" : "#424942");
            Resources["Primary"] = Color.FromArgb(dark ? "#86D5A8" : "#216C46");
            Resources["OnPrimary"] = Color.FromArgb(dark ? "#003921" : "#FFFFFF");
            Resources["PrimaryContainer"] = Color.FromArgb(dark ? "#005231" : "#A1F2C2");
            Resources["OnPrimaryContainer"] = Color.FromArgb(dark ? "#A1F2C2" : "#002111");
            Resources["Scrim"] = Color.FromArgb(dark ? "#CC000000" : "#B3000000");

#if ANDROID
            if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.S) return;
            var context = Android.App.Application.Context;
            var resourceName = dark ? "system_accent1_200" : "system_accent1_600";
            var resourceId = context.Resources?.GetIdentifier(resourceName, "color", "android") ?? 0;
            if (resourceId == 0) return;
            var accent = context.GetColor(resourceId);
            Resources["Primary"] = Color.FromInt(accent);
#elif WINDOWS
            var accent = new global::Windows.UI.ViewManagement.UISettings()
                .GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
            Resources["Primary"] = Color.FromRgba(accent.R, accent.G, accent.B, accent.A);
#endif
        }
    }
}
