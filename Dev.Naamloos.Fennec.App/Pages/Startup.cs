using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Startup : ContentPage
{
    private bool _started;
    private readonly ManagedMatrixClient _matrixClient;
    private readonly AppNavigationService _appNavigation;

    public Startup(ManagedMatrixClient matrixClient, AppNavigationService appNavigation)
    {
        _matrixClient = matrixClient;
        _appNavigation = appNavigation;

        BindingContext = this;
        Shell.SetNavBarIsVisible(this, false);
        build();
    }

    private void build()
    {
        Content = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 16,
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Loaded),
                }.Bind(EventToCommandBehavior.CommandProperty, nameof(StartCommand)),
            },
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    WidthRequest = 48,
                    HeightRequest = 48,
                },
                new Label
                {
                    Text = "Starting Fennec…",
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_started || _matrixClient is null || _appNavigation is null)
        {
            return;
        }

        _started = true;

        while (await _matrixClient.HasSavedSessionAsync())
        {
            try
            {
                if (await _matrixClient.RecoverSessionAsync())
                {
                    _appNavigation.ShowShell();
                    return;
                }
            }
            catch (Exception exception)
            {
                // ServerUnreachable during ClientBuilder.Build is transient; keep the
                // persisted session and retry instead of presenting a false logout.
                System.Diagnostics.Debug.WriteLine($"Could not recover Matrix session: {exception}");
            }

            if (!await _matrixClient.HasSavedSessionAsync())
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        _appNavigation.ShowLogin();
    }
}
