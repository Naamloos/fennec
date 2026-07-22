using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;
using AndroidSpecific = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace Dev.Naamloos.Fennec.App;

public sealed class App : Microsoft.Maui.Controls.Application
{
    private readonly Startup _startupPage;

    public App(Startup startupPage)
    {
        _startupPage = startupPage;

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