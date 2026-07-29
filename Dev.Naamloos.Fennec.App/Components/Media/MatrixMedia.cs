using System.Windows.Input;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using MediaElement = CommunityToolkit.Maui.Views.MediaElement;
using PlaybackMediaSource = CommunityToolkit.Maui.Views.MediaSource;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class MatrixMedia : ContentView
{
    private readonly ContentView _videoHost;
    private readonly Image _image;
    private MediaElement? _videoElement;
    private int _loadVersion;
    private CancellationTokenSource? _loadCancellation;

    [BindableProperty(PropertyChangedMethodName = nameof(OnMediaChanged))]
    public partial ChatMedia? Media { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnClientChanged))]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnIsFullChanged))]
    public partial bool IsFull { get; set; }

    [BindableProperty]
    public partial ICommand? OpenCommand { get; set; }

    [BindableProperty]
    public partial ImageSource? ImageSource { get; set; }

    [BindableProperty]
    public partial PlaybackMediaSource? VideoSource { get; set; }

    [BindableProperty]
    public partial bool IsImageVisible { get; set; }

    [BindableProperty]
    public partial bool IsVideoVisible { get; set; }

    [BindableProperty]
    public partial bool IsFileVisible { get; set; }

    public MatrixMedia()
    {
        IsVisible = false;
        Loaded += (_, _) => Load();
        Unloaded += (_, _) =>
        {
            _loadVersion++;
            _loadCancellation?.Cancel();
            StopVideo();
            ImageSource = null;
        };

        HeightRequest = 160;
        _videoHost = new ContentView();
        _image = new Image
        {
            Aspect = Aspect.AspectFit,
            IsAnimationPlaying = true,
            GestureRecognizers =
            {
                new TapGestureRecognizer()
                    .BindCommand(nameof(OpenCommand), source: this)
                    .Bind(
                        TapGestureRecognizer.CommandParameterProperty,
                        nameof(Media),
                        source: this
                    ),
            },
        }
            .Bind(Image.SourceProperty, nameof(ImageSource), source: this)
            .Bind(IsVisibleProperty, nameof(IsImageVisible), source: this);

        Content = new Grid
        {
            Children =
            {
                _image,
                _videoHost,
                new Button { HorizontalOptions = LayoutOptions.Start }
                    .Bind(
                        Button.TextProperty,
                        $"{nameof(Media)}.{nameof(ChatMedia.Filename)}",
                        source: this
                    )
                    .Bind(IsVisibleProperty, nameof(IsFileVisible), source: this)
                    .BindCommand(nameof(OpenCommand), source: this)
                    .Bind(Button.CommandParameterProperty, nameof(Media), source: this),
                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                }.Bind(
                    IsVisibleProperty,
                    $"{nameof(Media)}.{nameof(ChatMedia.IsLoading)}",
                    source: this
                ),
            },
        };
    }

    private void Load()
    {
        if (!IsLoaded)
        {
            return;
        }

        StopVideo();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        _ = LoadAsync(++_loadVersion, _loadCancellation.Token);
    }

    private async Task LoadAsync(int version, CancellationToken cancellationToken)
    {
        IsImageVisible = false;
        IsVideoVisible = false;
        IsFileVisible =
            Media?.Kind is ChatMediaKind.File or ChatMediaKind.Audio
            || (Media?.Kind == ChatMediaKind.Video && !IsFull && !Media.HasPreview);

        if (Client is null || Media is null || IsFileVisible)
        {
            return;
        }

        if (Media.Kind == ChatMediaKind.Video)
        {
            EnsureVideoElement();
        }

        byte[]? imageData;
        try
        {
            if (IsFull)
            {
                await Media.LoadFullAsync(Client).ConfigureAwait(false);
                imageData = Media.FullImageData;
            }
            else
            {
                imageData = await Media
                    .LoadPreviewAsync(Client, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load media: {exception}");
            await Dispatcher.DispatchAsync(() =>
                IsFileVisible = Media.Kind == ChatMediaKind.Video && !IsFull
            );
            return;
        }

        if (version != _loadVersion)
        {
            return;
        }

        var imageSource = imageData is null
            ? null
            : Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(imageData));
        await Dispatcher.DispatchAsync(() =>
        {
            if (version != _loadVersion)
                return;

            if (Media.Kind == ChatMediaKind.Video && IsFull)
            {
                VideoSource = Media.VideoPath is { } path
                    ? PlaybackMediaSource.FromFile(path)
                    : null;
                IsVideoVisible = VideoSource is not null;
                IsFileVisible = !IsVideoVisible;
                return;
            }

            ImageSource = imageSource;
            IsImageVisible = imageSource is not null;
        });
    }

    private static void OnMediaChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var media = (MatrixMedia)bindable;
        media.IsVisible = newValue is ChatMedia { Kind: not ChatMediaKind.Audio };
        media.Load();
    }

    private static void OnClientChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((MatrixMedia)bindable).Load();

    private static void OnIsFullChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var media = (MatrixMedia)bindable;
        media.HeightRequest = media.IsFull ? -1 : 160;
        media.Load();
    }

    private void StopVideo()
    {
        _videoElement?.Stop();
        VideoSource = null;
    }

    private void EnsureVideoElement()
    {
        if (_videoElement is not null)
            return;

        _videoElement = new MediaElement
        {
            ShouldAutoPlay = false,
            ShouldShowPlaybackControls = true,
            Aspect = Aspect.AspectFit,
        }
            .Bind(MediaElement.SourceProperty, nameof(VideoSource), source: this)
            .Bind(IsVisibleProperty, nameof(IsVideoVisible), source: this);
        _videoHost.Content = _videoElement;
    }
}
