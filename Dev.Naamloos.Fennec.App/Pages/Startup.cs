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
    private string _status = "Starting Fennec…";
    private readonly ManagedMatrixClient _matrixClient;
    private readonly AppNavigationService _appNavigation;

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public Startup(ManagedMatrixClient matrixClient, AppNavigationService appNavigation)
    {
        _matrixClient = matrixClient;
        _appNavigation = appNavigation;

        BindingContext = this;
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        Content = new VerticalStackLayout
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Padding = 24,
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
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap,
                }.Bind(Label.TextProperty, nameof(Status), source: this),
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
            Status = "Restoring your session…";
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
                System.Diagnostics.Debug.WriteLine(
                    $"Could not recover Matrix session: {exception}"
                );
                Status = "Can’t reach your homeserver. Retrying…";
            }

            if (!await _matrixClient.HasSavedSessionAsync())
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        Status = "Opening sign in…";
        _appNavigation.ShowLogin();
    }
}
