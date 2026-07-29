using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Plugin.Maui.Audio;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class InlineAudioPlayer : ContentView
{
    private AsyncAudioPlayer? _player;
    private MemoryStream? _stream;
    private CancellationTokenSource? _playbackCancellation;

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnMediaChanged))]
    public partial ChatMedia? Media { get; set; }

    [BindableProperty]
    public partial IAudioManager? AudioManager { get; set; }

    [BindableProperty]
    public partial bool IsPlaying { get; set; }

    [BindableProperty]
    public partial bool IsLoading { get; set; }

    public InlineAudioPlayer()
    {
        IsVisible = false;
        this.BindService<IAudioManager, InlineAudioPlayer>(AudioManagerProperty);
        Unloaded += (_, _) => ReleasePlayer();
        Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
            Children =
            {
                new Button
                {
                    Text = "Play",
                    WidthRequest = 76,
                    Command = TogglePlaybackCommand,
                }
                    .Bind(
                        IsVisibleProperty,
                        nameof(IsPlaying),
                        converter: new CommunityToolkit.Maui.Converters.InvertedBoolConverter(),
                        source: this
                    )
                    .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
                    .DynamicResource(Button.TextColorProperty, "OnPrimary")
                    .Column(0),
                new Button
                {
                    Text = "Stop",
                    WidthRequest = 76,
                    Command = TogglePlaybackCommand,
                }
                    .Bind(IsVisibleProperty, nameof(IsPlaying), source: this)
                    .DynamicResource(VisualElement.BackgroundColorProperty, "Primary")
                    .DynamicResource(Button.TextColorProperty, "OnPrimary")
                    .Column(0),
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = "Audio message", FontAttributes = FontAttributes.Bold },
                        new Label { FontSize = 12, Opacity = .7 }.Bind(
                            Label.TextProperty,
                            $"{nameof(Media)}.{nameof(ChatMedia.Filename)}",
                            source: this
                        ),
                    },
                }.Column(1),
                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }.Bind(IsVisibleProperty, nameof(IsLoading), source: this),
            },
        };
    }

    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        if (_player?.IsPlaying == true)
        {
            Stop();
            return;
        }

        if (_player is null)
        {
            if (Client is null || Media is null || AudioManager is null)
                return;
            IsLoading = true;
            try
            {
                _stream = new MemoryStream(
                    await Client.GetMediaContentAsync(Media.SourceJson),
                    writable: false
                );
                _player = AudioManager.CreateAsyncPlayer(_stream);
            }
            finally
            {
                IsLoading = false;
            }
        }

        IsPlaying = true;
        _playbackCancellation = new CancellationTokenSource();
        _ = PlayAsync(_player, _playbackCancellation);
    }

    private async Task PlayAsync(AsyncAudioPlayer player, CancellationTokenSource cancellation)
    {
        try
        {
            await player.PlayAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (
                ReferenceEquals(_player, player)
                && ReferenceEquals(_playbackCancellation, cancellation)
            )
            {
                Dispatcher.Dispatch(() => IsPlaying = false);
            }
        }
    }

    private void Stop()
    {
        _playbackCancellation?.Cancel();
        _player?.Stop();
        IsPlaying = false;
    }

    private void ReleasePlayer()
    {
        Stop();
        _playbackCancellation?.Dispose();
        _playbackCancellation = null;
        _player?.Dispose();
        _player = null;
        _stream?.Dispose();
        _stream = null;
    }

    private static void OnMediaChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var player = (InlineAudioPlayer)bindable;
        player.ReleasePlayer();
        player.IsVisible = newValue is ChatMedia { Kind: ChatMediaKind.Audio };
    }
}
