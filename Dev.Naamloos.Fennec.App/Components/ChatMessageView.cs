using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Pages;
using Dev.Naamloos.Fennec.Sdk;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using uniffi.matrix_sdk_ffi;
using MediaElement = CommunityToolkit.Maui.Views.MediaElement;
using PlaybackMediaSource = CommunityToolkit.Maui.Views.MediaSource;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class ChatMessageView : ContentView
{
    private static readonly SemaphoreSlim MediaLoadLimiter =
        new(4, 4);

    private CancellationTokenSource? _loadCancellation;
    private Grid? _videoHost;

    [BindableProperty(
        PropertyChangedMethodName = nameof(OnMessageChanged))]
    public partial ChatMessage? Message { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial Room? Room { get; set; }

    [BindableProperty]
    public partial ICommand? ReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MessageMenuCommand { get; set; }

    [BindableProperty]
    public partial ICommand? SaveMediaCommand { get; set; }

    [BindableProperty]
    public partial ImageSource? AvatarSource { get; set; }

    [BindableProperty]
    public partial ImageSource? MediaImageSource { get; set; }

    [BindableProperty]
    public partial PlaybackMediaSource? VideoSource { get; set; }

    [BindableProperty]
    public partial bool IsMediaLoading { get; set; }

    [BindableProperty]
    public partial bool IsImageVisible { get; set; }

    [BindableProperty]
    public partial bool IsVideoVisible { get; set; }

    [BindableProperty]
    public partial bool HasReadAvatars { get; set; }

    [BindableProperty]
    public partial int AdditionalReadCount { get; set; }

    [BindableProperty]
    public partial double MediaWidthRequest { get; set; }

    [BindableProperty]
    public partial double MediaHeightRequest { get; set; }

    public ObservableCollection<ReadAvatar> ReadAvatars
    {
        get;
    } = [];

    public ChatMessageView()
    {
        Build();
    }

    private void Build()
    {
        var root = new Border
        {
            Margin = new Thickness(12, 2),
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape =
                new RoundRectangle
                {
                    CornerRadius = 8,
                },

            Content = new Grid
            {
                ColumnSpacing = 10,

                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Auto),

                    new ColumnDefinition(
                        GridLength.Star),
                },

                Children =
                {
                    CreateAvatar()
                        .Column(0),

                    CreateMessageContent()
                        .Column(1),
                },
            },
        };

        var longPress =
            new CommunityToolkit.Maui.Behaviors.TouchBehavior
            {
                LongPressDuration = 500,
                ShouldMakeChildrenInputTransparent = true,
            };

        longPress.Bind(
            CommunityToolkit.Maui.Behaviors.TouchBehavior
                .LongPressCommandProperty,
            nameof(OpenMessageMenuCommand),
            source: this);

        root.Behaviors.Add(longPress);

        Content = root;
    }

    private View CreateAvatar()
    {
        return new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeThickness = 0,
            StrokeShape =
                new RoundRectangle
                {
                    CornerRadius = 18,
                },

            Content = new Grid
            {
                Children =
                {
                    new Label
                    {
                        FontAttributes =
                            FontAttributes.Bold,

                        HorizontalTextAlignment =
                            TextAlignment.Center,

                        VerticalTextAlignment =
                            TextAlignment.Center,
                    }
                    .Bind(
                        Label.TextProperty,
                        $"{nameof(Message)}." +
                        $"{nameof(ChatMessage.Username)}",
                        converter:
                            InitialConverter.Instance,
                        source: this),

                    new Image
                    {
                        WidthRequest = 36,
                        HeightRequest = 36,
                        Aspect = Aspect.AspectFill,
                    }
                    .Bind(
                        Image.SourceProperty,
                        nameof(AvatarSource),
                        source: this),
                },
            },
        }
        .DynamicResource(
            BackgroundColorProperty,
            "PrimaryContainer");
    }

    private View CreateMessageContent()
    {
        var videoHost = new Grid
        {
            IsVisible = false,
            HorizontalOptions =
                LayoutOptions.Start,
        }
        .Bind(
            WidthRequestProperty,
            nameof(MediaWidthRequest),
            source: this)
        .Bind(
            HeightRequestProperty,
            nameof(MediaHeightRequest),
            source: this)
        .Bind(
            IsVisibleProperty,
            nameof(IsVideoVisible),
            source: this);

        _videoHost = videoHost;

        var readReceipts =
            new HorizontalStackLayout
            {
                Spacing = 3,
                HorizontalOptions =
                    LayoutOptions.End,

                Children =
                {
                    new Label
                    {
                        FontSize = 10,
                        VerticalTextAlignment =
                            TextAlignment.Center,
                    }
                    .Bind(
                        Label.TextProperty,
                        nameof(AdditionalReadCount),
                        stringFormat: "+{0}",
                        source: this)
                    .Bind(
                        IsVisibleProperty,
                        nameof(AdditionalReadCount),
                        converter:
                            new NonZeroConverter(),
                        source: this),
                },
            }
            .Bind(
                IsVisibleProperty,
                nameof(HasReadAvatars),
                source: this)
            .Bind(
                BindableLayout.ItemsSourceProperty,
                nameof(ReadAvatars),
                source: this);

        BindableLayout.SetItemTemplate(
            readReceipts,
            new DataTemplate(() =>
                new Border
                {
                    WidthRequest = 18,
                    HeightRequest = 18,
                    StrokeThickness = 0,
                    StrokeShape =
                        new RoundRectangle
                        {
                            CornerRadius = 9,
                        },

                    Content = new Grid
                    {
                        Children =
                        {
                            new Label
                            {
                                FontSize = 8,
                                FontAttributes =
                                    FontAttributes.Bold,

                                HorizontalTextAlignment =
                                    TextAlignment.Center,

                                VerticalTextAlignment =
                                    TextAlignment.Center,
                            }
                            .Bind(
                                Label.TextProperty,
                                nameof(ReadAvatar.Initial)),

                            new Image
                            {
                                Aspect =
                                    Aspect.AspectFill,
                            }
                            .Bind(
                                Image.SourceProperty,
                                nameof(ReadAvatar.Source)),
                        },
                    },
                }
                .DynamicResource(
                    BackgroundColorProperty,
                    "PrimaryContainer")));

        return new VerticalStackLayout
        {
            Spacing = 4,

            Children =
            {
                new Label
                {
                    FontSize = 11,
                    Opacity = .7,
                    LineBreakMode =
                        LineBreakMode.TailTruncation,
                }
                .Bind(
                    Label.TextProperty,
                    $"{nameof(Message)}." +
                    $"{nameof(ChatMessage.ReplyTo)}",
                    source: this)
                .Bind(
                    IsVisibleProperty,
                    $"{nameof(Message)}." +
                    $"{nameof(ChatMessage.ReplyTo)}",
                    converter:
                        new NotNullConverter(),
                    source: this),

                new Label
                {
                    FontAttributes =
                        FontAttributes.Bold,

                    FontSize = 14,
                    LineBreakMode =
                        LineBreakMode.TailTruncation,
                }
                .Bind(
                    Label.TextProperty,
                    $"{nameof(Message)}." +
                    $"{nameof(ChatMessage.Username)}",
                    source: this),

                new Label
                {
                    FontSize = 14,
                    LineBreakMode =
                        LineBreakMode.WordWrap,
                }
                .Bind(
                    Label.TextProperty,
                    $"{nameof(Message)}." +
                    $"{nameof(ChatMessage.Body)}",
                    source: this),

                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions =
                        LayoutOptions.Start,
                }
                .Bind(
                    IsVisibleProperty,
                    nameof(IsMediaLoading),
                    source: this),

                new Image
                {
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions =
                        LayoutOptions.Start,
                }
                .Bind(
                    Image.SourceProperty,
                    nameof(MediaImageSource),
                    source: this)
                .Bind(
                    WidthRequestProperty,
                    nameof(MediaWidthRequest),
                    source: this)
                .Bind(
                    HeightRequestProperty,
                    nameof(MediaHeightRequest),
                    source: this)
                .Bind(
                    IsVisibleProperty,
                    nameof(IsImageVisible),
                    source: this),

                videoHost,
                readReceipts,
            },
        };
    }

    private static void OnMessageChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((ChatMessageView)bindable)
            .StartLoadingMessage();
    }

    private void StartLoadingMessage()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();

        var cancellation =
            new CancellationTokenSource();

        _loadCancellation = cancellation;

        _ = LoadMessageSafelyAsync(
            cancellation.Token);
    }

    private async Task LoadMessageSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await LoadMessageAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The CollectionView recycled this row.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not load chat row: {exception}");
        }
    }

    private async Task LoadMessageAsync(
        CancellationToken cancellationToken)
    {
        var message = Message;

        ResetVisualState();

        if (message is null)
        {
            return;
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        var mediaSize =
            FitMediaSize(
                message.MediaWidth,
                message.MediaHeight,
                message.MediaKind);

        MediaWidthRequest =
            mediaSize.Width;

        MediaHeightRequest =
            mediaSize.Height;

        var loads = new List<Task>
        {
            LoadReadAvatarsAsync(
                message,
                cancellationToken),
        };

        if (!string.IsNullOrWhiteSpace(
                message.AvatarUrl))
        {
            loads.Add(
                LoadAvatarAsync(
                    message,
                    cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(
                message.MediaSourceJson))
        {
            loads.Add(
                LoadMediaAsync(
                    message,
                    cancellationToken));
        }

        await Task.WhenAll(loads);
    }

    private void ResetVisualState()
    {
        AvatarSource = null;
        MediaImageSource = null;
        VideoSource = null;

        _videoHost?.Children.Clear();

        IsImageVisible = false;
        IsVideoVisible = false;

        IsMediaLoading =
            Message?.MediaKind is
                not null and
                not ChatMediaKind.None;

        ReadAvatars.Clear();
        AdditionalReadCount = 0;
        HasReadAvatars = false;
    }

    private async Task LoadAvatarAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        if (MatrixClient is null)
        {
            return;
        }

        await MediaLoadLimiter.WaitAsync(
            cancellationToken);

        try
        {
            var bytes =
                await MatrixClient.GetThumbnailAsync(
                    message.AvatarUrl!,
                    72,
                    72,
                    isJson: false);

            cancellationToken
                .ThrowIfCancellationRequested();

            if (Message?.Id == message.Id)
            {
                AvatarSource =
                    ImageSource.FromStream(
                        () => new MemoryStream(bytes));
            }
        }
        finally
        {
            MediaLoadLimiter.Release();
        }
    }

    private async Task LoadMediaAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        if (MatrixClient is null)
        {
            return;
        }

        await MediaLoadLimiter.WaitAsync(
            cancellationToken);

        try
        {
            if (message.MediaKind ==
                ChatMediaKind.Image)
            {
                var bytes =
                    await MatrixClient.GetThumbnailAsync(
                        message.MediaSourceJson!,
                        560,
                        440);

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (Message?.Id == message.Id)
                {
                    MediaImageSource =
                        ImageSource.FromStream(
                            () =>
                                new MemoryStream(bytes));

                    IsImageVisible = true;
                }
            }
            else if (message.MediaKind ==
                     ChatMediaKind.Video)
            {
                var path =
                    await MatrixClient.GetVideoFileAsync(
                        message.MediaSourceJson!,
                        message.Filename ?? "video",
                        message.MimeType ??
                        "video/mp4");

                cancellationToken
                    .ThrowIfCancellationRequested();

                if (Message?.Id == message.Id)
                {
                    VideoSource =
                        PlaybackMediaSource.FromFile(
                            path);

                    EnsureVideoElement();
                    IsVideoVisible = true;
                }
            }
        }
        finally
        {
            MediaLoadLimiter.Release();

            if (Message?.Id == message.Id)
            {
                IsMediaLoading = false;
            }
        }
    }

    private async Task LoadReadAvatarsAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        const int maximumAvatars = 5;

        var userIds =
            message.ReadByUserIds?.Split(
                '\n',
                StringSplitOptions
                    .RemoveEmptyEntries) ??
            [];

        foreach (var userId in
                 userIds.Take(maximumAvatars))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var avatar =
                new ReadAvatar(
                    userId,
                    GetUserInitial(userId));

            ReadAvatars.Add(avatar);

            if (Room is null ||
                MatrixClient is null)
            {
                continue;
            }

            try
            {
                var avatarUrl =
                    await Room.MemberAvatarUrl(
                        userId);

                if (string.IsNullOrWhiteSpace(
                        avatarUrl))
                {
                    continue;
                }

                await MediaLoadLimiter.WaitAsync(
                    cancellationToken);

                try
                {
                    var bytes =
                        await MatrixClient
                            .GetThumbnailAsync(
                                avatarUrl,
                                36,
                                36,
                                isJson: false);

                    cancellationToken
                        .ThrowIfCancellationRequested();

                    if (Message?.Id == message.Id)
                    {
                        avatar.Source =
                            ImageSource.FromStream(
                                () =>
                                    new MemoryStream(
                                        bytes));
                    }
                }
                finally
                {
                    MediaLoadLimiter.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load read avatar: " +
                    exception);
            }
        }

        if (Message?.Id != message.Id)
        {
            return;
        }

        AdditionalReadCount =
            Math.Max(
                0,
                userIds.Length -
                maximumAvatars);

        HasReadAvatars =
            userIds.Length > 0;
    }

    private void EnsureVideoElement()
    {
        if (_videoHost is null ||
            _videoHost.Children.Count > 0)
        {
            return;
        }

        _videoHost.Children.Add(
            new MediaElement
            {
                Aspect = Aspect.AspectFit,
                HorizontalOptions =
                    LayoutOptions.Start,
                ShouldAutoPlay = false,
                ShouldShowPlaybackControls = true,
            }
            .Bind(
                MediaElement.SourceProperty,
                nameof(VideoSource),
                source: this));
    }

    internal static Size FitMediaSize(
        ulong? width,
        ulong? height,
        ChatMediaKind kind)
    {
        const double maxWidth = 280;
        const double maxHeight = 220;

        if (width is > 0 &&
            height is > 0)
        {
            var scale =
                Math.Min(
                    1,
                    Math.Min(
                        maxWidth / width.Value,
                        maxHeight / height.Value));

            return new Size(
                width.Value * scale,
                height.Value * scale);
        }

        return kind == ChatMediaKind.Video
            ? new Size(maxWidth, 157.5)
            : new Size(maxWidth, 210);
    }

    [RelayCommand]
    private void OpenMessageMenu()
    {
        Execute(
            MessageMenuCommand,
            Message);
    }

    [RelayCommand]
    private void RequestReply()
    {
        Execute(
            ReplyCommand,
            Message);
    }

    [RelayCommand]
    private void SaveSelectedMedia()
    {
        Execute(
            SaveMediaCommand,
            Message);
    }

    private static void Execute(
        ICommand? command,
        object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private static string GetUserInitial(
        string userId)
    {
        var localpart =
            userId.TrimStart('@');

        return string.IsNullOrEmpty(localpart)
            ? "?"
            : localpart[..1]
                .ToUpperInvariant();
    }

    private sealed class InitialConverter
        : IValueConverter
    {
        public static InitialConverter Instance
        {
            get;
        } = new();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            var text = value?.ToString();

            return string.IsNullOrWhiteSpace(text)
                ? "?"
                : text.Trim()[..1]
                    .ToUpperInvariant();
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NotNullConverter
        : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            return value is not null;
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NonZeroConverter
        : IValueConverter
    {
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            return value is int number &&
                   number != 0;
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

public sealed partial class ReadAvatar(
    string userId,
    string initial)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string UserId
    {
        get;
    } = userId;

    public string Initial
    {
        get;
    } = initial;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial ImageSource? Source { get; set; }
}
