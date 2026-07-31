using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Services;

public sealed record EmojiFontOption(string Id, string DisplayName, string? FontFamily);

public sealed partial class UserSettingsService(ManagedMatrixClient matrixClient) : ObservableObject
{
    public const string EventType = "dev.naamloos.fennec.settings";

    private bool _loaded;
    private bool _applyingRemoteSettings;

    [ObservableProperty]
    private bool _experimentalFeatureEnabled;

    [ObservableProperty]
    private string _emojiFontId = "system";

    private static readonly EmojiFontOption[] AllEmojiFontOptions =
    [
        new("system", "System default", null),
        new("fluent", "Fluent Emoji", "FluentEmojiColor"),
        new("twitter", "Twitter Color Emoji", "TwitterColorEmoji"),
        new("noto", "Noto Color Emoji", "NotoColorEmoji"),
        new("openmoji", "OpenMoji Color", "OpenMojiColor"),
        new("mona", "Mona Color Emoji", "Mona12ColorEmoji"),
        new("serenityos", "SerenityOS Emoji", "SerenityOSEmoji"),
    ];

    public IReadOnlyList<EmojiFontOption> EmojiFontOptions => AllEmojiFontOptions;

    public EmojiFontOption SelectedEmojiFont
    {
        get =>
            EmojiFontOptions.FirstOrDefault(option => option.Id == EmojiFontId)
            ?? EmojiFontOptions[0];
        set => EmojiFontId = value?.Id ?? "system";
    }

    public string? SelectedEmojiFontFamily => SelectedEmojiFont.FontFamily;

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
                Apply(settings);
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() => Apply(settings));
            }
        }
        finally
        {
            _applyingRemoteSettings = false;
        }
    }

    partial void OnExperimentalFeatureEnabledChanged(bool value)
    {
        Save();
    }

    partial void OnEmojiFontIdChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedEmojiFont));
        OnPropertyChanged(nameof(SelectedEmojiFontFamily));
        Save();
    }

    private void Apply(UserSettings? settings)
    {
        ExperimentalFeatureEnabled = settings?.ExperimentalFeatureEnabled ?? false;
        if (!string.IsNullOrWhiteSpace(settings?.EmojiFontId))
        {
            EmojiFontId = settings.EmojiFontId;
        }
    }

    private void Save()
    {
        if (_loaded && !_applyingRemoteSettings)
        {
            _ = matrixClient.SetAccountDataAsync(
                EventType,
                JsonSerializer.Serialize(new UserSettings(ExperimentalFeatureEnabled, EmojiFontId))
            );
        }
    }

    private sealed record UserSettings(bool ExperimentalFeatureEnabled, string? EmojiFontId = null);
}
