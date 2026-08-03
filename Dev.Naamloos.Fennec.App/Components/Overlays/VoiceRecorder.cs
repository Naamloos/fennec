using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Audio;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class VoiceRecorder : ContentView
{
    private IAudioRecorder? _recorder;

    [BindableProperty]
    public partial IAudioManager? AudioManager { get; set; }

    [BindableProperty]
    public partial ICommand? SendCommand { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnIsOpenChanged))]
    public partial bool IsOpen { get; set; }

    [BindableProperty]
    public partial bool IsRecording { get; set; }

    [BindableProperty]
    public partial string Status { get; set; } = "Ready to record";

    public VoiceRecorder()
    {
        IsVisible = false;
        this.BindService<IAudioManager, VoiceRecorder>(AudioManagerProperty);
        Content = new Grid
        {
            Children =
            {
                new BoxView
                {
                    Color = Color.FromArgb("#66000000"),
                    GestureRecognizers =
                    {
                        new TapGestureRecognizer().BindCommand(
                            nameof(CancelCommand),
                            source: this
                        ),
                    },
                },
                new Border
                {
                    Margin = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? new Thickness(0)
                        : new Thickness(24),
                    Padding = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? new Thickness(20, 16, 20, 24)
                        : new Thickness(24),
                    MaximumWidthRequest = 520,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                        ? LayoutOptions.End
                        : LayoutOptions.Center,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius = DeviceInfo.Current.Idiom == DeviceIdiom.Phone
                            ? new CornerRadius(24, 24, 0, 0)
                            : new CornerRadius(20),
                    },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 14,
                        Children =
                        {
                            new Label
                            {
                                Text = "Voice message",
                                FontSize = 20,
                                FontAttributes = FontAttributes.Bold,
                            },
                            new Label { HorizontalTextAlignment = TextAlignment.Center }.Bind(
                                Label.TextProperty,
                                nameof(Status),
                                source: this
                            ),
                            new Grid
                            {
                                ColumnSpacing = 12,
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Star),
                                },
                                Children =
                                {
                                    new Button { Text = "Cancel" }
                                        .BindCommand(nameof(CancelCommand), source: this)
                                        .Column(0),
                                    new Button { Text = "Record" }
                                        .BindCommand(nameof(ToggleRecordingCommand), source: this)
                                        .Bind(
                                            IsVisibleProperty,
                                            nameof(IsRecording),
                                            converter: new InvertedBoolConverter(),
                                            source: this
                                        )
                                        .Column(1),
                                    new Button { Text = "Stop & send" }
                                        .BindCommand(nameof(ToggleRecordingCommand), source: this)
                                        .Bind(IsVisibleProperty, nameof(IsRecording), source: this)
                                        .Column(1),
                                },
                            },
                        },
                    },
                }.DynamicResource(VisualElement.BackgroundColorProperty, "Surface"),
            },
        };
    }

    private static void OnIsOpenChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((VoiceRecorder)bindable).IsVisible = (bool)newValue;

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopAndSendAsync();
            return;
        }

        var permission = await Permissions.RequestAsync<Permissions.Microphone>();
        if (permission != PermissionStatus.Granted)
        {
            Status = "Microphone permission is required.";
            return;
        }

        _recorder = AudioManager?.CreateRecorder(
            new AudioRecorderOptions { Encoding = Encoding.Wav }
        );
        if (_recorder is not { CanRecordAudio: true })
        {
            Status = "Recording is not available on this device.";
            return;
        }

        await _recorder.StartAsync(new AudioRecorderOptions { Encoding = Encoding.Wav });
        IsRecording = true;
        Status = "Recording… tap Stop & send when you are done.";
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (_recorder is { IsRecording: true })
            await _recorder.StopAsync();
        _recorder = null;
        IsRecording = false;
        IsOpen = false;
        Status = "Ready to record";
    }

    private async Task StopAndSendAsync()
    {
        if (_recorder is null)
            return;
        var source = await _recorder.StopAsync();
        _recorder = null;
        IsRecording = false;
        await using var input = source.GetAudioStream();
        using var data = new MemoryStream();
        await input.CopyToAsync(data);
        var attachment = await AttachmentPicker.ConfirmAsync(
            new PickedAttachment(
                $"voice-{DateTime.UtcNow:yyyyMMdd-HHmmss}.wav",
                "audio/wav",
                data.ToArray()
            )
        );
        if (attachment is not null)
            SendCommand?.Execute(attachment);
        IsOpen = false;
        Status = "Ready to record";
    }
}
