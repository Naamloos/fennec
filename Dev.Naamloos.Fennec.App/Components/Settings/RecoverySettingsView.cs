using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RecoverySettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient),
        typeof(ManagedMatrixClient),
        typeof(RecoverySettingsView)
    );

    private readonly Label _status = new()
    {
        Opacity = .75,
        LineBreakMode = LineBreakMode.WordWrap,
    };
    private readonly Label _recoveryKey = new()
    {
        IsVisible = false,
        FontFamily = "OpenSansRegular",
        LineBreakMode = LineBreakMode.CharacterWrap,
    };
    private readonly Button _copy = new()
    {
        Text = "Copy recovery key",
        IsVisible = false,
        BackgroundColor = Colors.Transparent,
    };

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public RecoverySettingsView()
    {
        this.BindService<ManagedMatrixClient, RecoverySettingsView>(MatrixClientProperty);
        Loaded += async (_, _) => await RefreshAsync();

        var enable = new Button { Text = "Create recovery key" };
        enable.DynamicResource(VisualElement.BackgroundColorProperty, "Primary");
        enable.DynamicResource(Button.TextColorProperty, "OnPrimary");
        enable.Clicked += async (_, _) => await EnableAsync(enable);

        var restore = new Button
        {
            Text = "Restore from recovery key",
            BackgroundColor = Colors.Transparent,
        };
        restore.DynamicResource(Button.TextColorProperty, "Primary");
        restore.Clicked += async (_, _) => await RestoreAsync(restore);

        _copy.DynamicResource(Button.TextColorProperty, "Primary");
        _copy.Clicked += async (_, _) => await Clipboard.Default.SetTextAsync(_recoveryKey.Text);

        Content = new SettingsSection(
            "Encryption recovery",
            new Label
            {
                Text =
                    "Back up encrypted message keys so a verified new session can recover message history.",
                Opacity = .7,
                LineBreakMode = LineBreakMode.WordWrap,
            },
            _status,
            enable,
            _recoveryKey,
            _copy,
            restore
        );
    }

    private async Task RefreshAsync()
    {
        if (MatrixClient is null)
            return;
        try
        {
            _status.Text = await MatrixClient.HasRecoveryBackupAsync()
                ? "A recovery backup is available. Keep its recovery key somewhere safe."
                : "No recovery backup is configured for this account.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.TextColor = Colors.Red;
        }
    }

    private async Task EnableAsync(Button button)
    {
        if (MatrixClient is null)
            return;
        var page = Shell.Current?.CurrentPage;
        var passphrase = await InAppDialogs.PromptAsync(
            page,
            "Protect recovery key",
            "Optional passphrase (leave blank to skip)",
            "Continue",
            isPassword: true
        );
        if (page is not null && passphrase is null)
            return;

        try
        {
            button.IsEnabled = false;
            _status.Text = "Creating encrypted backup…";
            var key = await MatrixClient.EnableRecoveryAsync(
                string.IsNullOrWhiteSpace(passphrase) ? null : passphrase
            );
            _recoveryKey.Text = key;
            _recoveryKey.IsVisible = _copy.IsVisible = true;
            _status.Text = "Copy the recovery key now. Fennec will not store or show it again.";
            _status.TextColor = Colors.Orange;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.TextColor = Colors.Red;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task RestoreAsync(Button button)
    {
        if (MatrixClient is null || Shell.Current?.CurrentPage is not { } page)
            return;
        var key = await InAppDialogs.PromptAsync(
            page,
            "Restore encrypted history",
            "Recovery key",
            "Restore",
            isPassword: true
        );
        if (string.IsNullOrWhiteSpace(key))
            return;

        try
        {
            button.IsEnabled = false;
            _status.Text = "Restoring encrypted keys…";
            await MatrixClient.RecoverEncryptionAsync(key);
            _status.Text = "Encrypted key recovery completed.";
            _status.TextColor = Colors.Green;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
            _status.TextColor = Colors.Red;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
