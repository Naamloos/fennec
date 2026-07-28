using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;
using System.Text.Json;

namespace Dev.Naamloos.Fennec.App.Services;

public sealed partial class UserSettingsService(ManagedMatrixClient matrixClient) : ObservableObject
{
    public const string EventType = "dev.naamloos.fennec.settings";

    private bool _loaded;

    [ObservableProperty]
    private bool _experimentalFeatureEnabled;

    public async Task LoadAsync()
    {
        if (_loaded) return;

        var content = await matrixClient.GetAccountDataAsync(EventType);
        if (!string.IsNullOrWhiteSpace(content))
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(content);
            ExperimentalFeatureEnabled = settings?.ExperimentalFeatureEnabled ?? false;
        }

        _loaded = true;
    }

    partial void OnExperimentalFeatureEnabledChanged(bool value)
    {
        if (_loaded)
        {
            _ = matrixClient.SetAccountDataAsync(
                EventType,
                JsonSerializer.Serialize(new UserSettings(value)));
        }
    }

    private sealed record UserSettings(bool ExperimentalFeatureEnabled);
}
