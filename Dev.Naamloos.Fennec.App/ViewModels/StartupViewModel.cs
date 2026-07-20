namespace Dev.Naamloos.Fennec.App.ViewModels;

public sealed class StartupViewModel(
    MatrixService matrixService,
    AppNavigationService navigation)
{
    private bool _started;

    public async Task InitializeAsync()
    {
        if (_started) return;
        _started = true;

        try
        {
            if (await matrixService.TryRestoreSessionAsync())
                navigation.ShowShell();
            else
                navigation.ShowLogin();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            await matrixService.ClearSavedSessionAsync();
            navigation.ShowLogin();
        }
    }
}
