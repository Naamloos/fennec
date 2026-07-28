using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class WallpaperSettingsView : ContentView
{
    public static readonly BindableProperty MatrixClientProperty = BindableProperty.Create(
        nameof(MatrixClient), typeof(ManagedMatrixClient), typeof(WallpaperSettingsView));

    private readonly MatrixImage _preview = new() { IsJson = false, UseFullSize = true, Aspect = Aspect.AspectFill };
    private readonly Label _empty = new()
    {
        Text = "No global wallpaper",
        Opacity = .65,
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center,
    };
    private readonly Label _status = new() { FontSize = 12, IsVisible = false };
    private readonly Button _set;
    private readonly Button _clear;

    public ManagedMatrixClient? MatrixClient
    {
        get => (ManagedMatrixClient?)GetValue(MatrixClientProperty);
        set => SetValue(MatrixClientProperty, value);
    }

    public WallpaperSettingsView()
    {
        this.BindService<ManagedMatrixClient, WallpaperSettingsView>(MatrixClientProperty);
        Loaded += async (_, _) => await RefreshAsync();

        _set = new Button { Text = "Choose wallpaper" }
            .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
            .DynamicResource(Button.TextColorProperty, "OnPrimary");
        _clear = new Button { Text = "Clear" }
            .DynamicResource(VisualElement.BackgroundColorProperty, "SurfaceContainer")
            .DynamicResource(Button.TextColorProperty, "OnSurface");
        _set.Clicked += async (_, _) => await SetAsync();
        _clear.Clicked += async (_, _) => await ClearAsync();

        Content = new SettingsSection("Wallpaper",
            new Label
            {
                Text = "Used in rooms without their own wallpaper. Room wallpapers take precedence.",
                FontSize = 12,
                Opacity = .7,
            },
            new Border
            {
                HeightRequest = 180,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Content = new Grid { Children = { _empty, _preview } },
            }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface2"),
            new HorizontalStackLayout { Spacing = 8, Children = { _set, _clear } },
            _status);
    }

    private async Task RefreshAsync()
    {
        if (MatrixClient is null) return;
        try
        {
            SetPreview(await MatrixClient.GetGlobalWallpaperAsync());
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task SetAsync()
    {
        if (MatrixClient is null) return;
        var attachment = await AttachmentPicker.PickConfirmedAsync(new PickOptions
        {
            PickerTitle = "Choose global wallpaper",
            FileTypes = FilePickerFileType.Images,
        });
        if (attachment is null) return;

        try
        {
            SetBusy(true, "Uploading wallpaper…");
            var url = await MatrixClient.UploadMediaAsync(attachment.MimeType, attachment.Data);
            await MatrixClient.SetGlobalWallpaperAsync(url);
            SetPreview(url);
            ShowStatus("Global wallpaper updated");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ClearAsync()
    {
        if (MatrixClient is null) return;
        try
        {
            SetBusy(true, "Clearing wallpaper…");
            await MatrixClient.ClearGlobalWallpaperAsync();
            SetPreview(null);
            ShowStatus("Global wallpaper cleared");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetPreview(string? url)
    {
        _preview.MatrixSource = url;
        _empty.IsVisible = string.IsNullOrWhiteSpace(url);
        _clear.IsEnabled = !string.IsNullOrWhiteSpace(url);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _set.IsEnabled = !busy;
        _clear.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_preview.MatrixSource);
        if (message is not null) ShowStatus(message);
    }

    private void ShowStatus(string message)
    {
        _status.Text = message;
        _status.TextColor = Colors.Gray;
        _status.IsVisible = true;
    }

    private void ShowError(Exception exception)
    {
        _status.Text = exception.Message;
        _status.TextColor = Colors.Red;
        _status.IsVisible = true;
    }
}
