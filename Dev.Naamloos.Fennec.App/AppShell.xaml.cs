namespace Dev.Naamloos.Fennec.App;

public partial class AppShell : Shell
{
    private readonly AppShellViewModel _viewModel;

    public AppShell(MainPage mainPage, SettingsPage settingsPage, AppShellViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Items.Add(new ShellContent { Route = "main", Content = mainPage });
        Items.Add(new ShellContent { Route = "settings", Content = settingsPage });
        _viewModel.RoomSelected += OnRoomSelected;
        _viewModel.SettingsRequested += OnSettingsRequested;
        _viewModel.Start();
    }

    private async void OnRoomSelected(object? sender, EventArgs e)
    {
        await GoToAsync("//main");
        FlyoutIsPresented = false;
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        await GoToAsync("//settings");
        FlyoutIsPresented = false;
    }
}
