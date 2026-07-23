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

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial AppNavigationService? AppNavigation { get; set; }

    public Startup()
    {
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
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(StartCommand)),
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
        if (_started ||
            MatrixClient is null ||
            AppNavigation is null)
        {
            return;
        }

        _started = true;

        try
        {
            if (await MatrixClient.RecoverSessionAsync())
            {
                AppNavigation.ShowShell();
            }
            else
            {
                AppNavigation.ShowLogin();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await MatrixClient.LogoutAsync();
            AppNavigation.ShowLogin();
        }
    }
}
