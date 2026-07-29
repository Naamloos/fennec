using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Services;

public sealed partial class UserSettingsService(ManagedMatrixClient matrixClient) : ObservableObject
{
    public const string EventType = "dev.naamloos.fennec.settings";

    private bool _loaded;
    private bool _applyingRemoteSettings;

    [ObservableProperty]
    private bool _experimentalFeatureEnabled;

    public async Task LoadAsync()
    {
        await RefreshAsync();
        if (_loaded)
            return;

        _loaded = true;
        _ = WatchAsync();
    }

    private async Task WatchAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (matrixClient.IsLoggedIn)
                {
                    await RefreshAsync();
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Could not refresh user settings: {exception}");
            }
        }
    }

    private async Task RefreshAsync()
    {
        var content = await matrixClient.GetAccountDataAsync(EventType);
        var settings = string.IsNullOrWhiteSpace(content)
            ? null
            : JsonSerializer.Deserialize<UserSettings>(content);

        _applyingRemoteSettings = true;
        try
        {
            if (MainThread.IsMainThread)
            {
                ExperimentalFeatureEnabled = settings?.ExperimentalFeatureEnabled ?? false;
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    ExperimentalFeatureEnabled = settings?.ExperimentalFeatureEnabled ?? false
                );
            }
        }
        finally
        {
            _applyingRemoteSettings = false;
        }
    }

    partial void OnExperimentalFeatureEnabledChanged(bool value)
    {
        if (_loaded && !_applyingRemoteSettings)
        {
            _ = matrixClient.SetAccountDataAsync(
                EventType,
                JsonSerializer.Serialize(new UserSettings(value))
            );
        }
    }

    private sealed record UserSettings(bool ExperimentalFeatureEnabled);
}
