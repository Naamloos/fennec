using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed class Startup : ContentPage
{
    private readonly ManagedMatrixClient _matrixClient;
    private readonly AppNavigationService _navigation;
    private readonly ToastService _toastService;

    private bool _started;

    public Startup(
        ManagedMatrixClient matrixClient,
        AppNavigationService navigation,
        ToastService toastService)
    {
        _matrixClient = matrixClient;
        _navigation = navigation;
        _toastService = toastService;

        Shell.SetNavBarIsVisible(this, false);

        Content = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 16,
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_started)
        {
            return;
        }

        _started = true;

        try
        {
            if (await _matrixClient.RecoverSessionAsync())
            {
                _navigation.ShowShell();
            }
            else
            {
                _navigation.ShowLogin();
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);

            await _matrixClient.LogoutAsync();
            _navigation.ShowLogin();
        }
    }
}