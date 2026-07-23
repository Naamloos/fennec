using MaterialColorUtilities.Maui;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using AndroidSpecific = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace Dev.Naamloos.Fennec.App;

public sealed class App : Microsoft.Maui.Controls.Application
{
    public static IServiceProvider? Services { get; private set; } = null;

    private readonly Startup _startupPage;

    public App(Startup startupPage, IServiceProvider services)
    {
        _startupPage = startupPage;
        Services = services;

        Resources.MergedDictionaries.Add(new Resources.Styles.Colors());
        Resources.MergedDictionaries.Add(new Resources.Styles.Style());
        IMaterialColorService.Current.Initialize(Resources);

        On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .UseWindowSoftInputModeAdjust(
                AndroidSpecific.WindowSoftInputModeAdjust.Resize);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_startupPage)
        {
            Title = "Fennec",
        };
    }

    public static void SetRootPage(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Current?.Windows.FirstOrDefault() is not { } window)
        {
            throw new InvalidOperationException(
                "The application window is unavailable.");
        }

        window.Page = page;
    }
}
