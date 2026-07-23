using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
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
    [BindableProperty(PropertyChangedMethodName = nameof(OnMessageChanged))]
    public partial ChatMessage? Message { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnInputChanged))]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnInputChanged))]
    public partial Room? Room { get; set; }

    [BindableProperty]
    public partial ICommand? ReplyCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MessageMenuCommand { get; set; }

    [BindableProperty]
    public partial ICommand? SaveMediaCommand { get; set; }

    [BindableProperty]
    public partial ICommand? MessageMeasuredCommand { get; set; }

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

    public ObservableCollection<ReadAvatar> ReadAvatars { get; } = [];

    public ChatMessageView()
    {
        BindingContext = this;
        build();
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
            var scale = Math.Min(
                1,
                Math.Min(
                    maxWidth / width.Value,
                    maxHeight / height.Value));
            return new Size(
                width.Value * scale,
                height.Value * scale);
        }

        return kind is ChatMediaKind.Video
            ? new Size(maxWidth, 157.5)
            : new Size(maxWidth, 210);
    }

    private void build()
    {
        Content = new Border
        {
            Margin = new Thickness(12, 2),
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Behaviors =
            {
                new TouchBehavior
                {
                    BindingContext = this,
                    LongPressDuration = 500,
                    ShouldMakeChildrenInputTransparent = true,
                    LongPressCommand = OpenMessageMenuCommand,
                },
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(SizeChanged),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(MeasureCommand)),
            },
            Content = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                Children =
                {
                    new Border
                    {
                        WidthRequest = 36,
                        HeightRequest = 36,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 18 },
                        Content = new Grid
                        {
                            Children =
                            {
                                new Label
                                {
                                    FontAttributes = FontAttributes.Bold,
                                    HorizontalTextAlignment = TextAlignment.Center,
                                    VerticalTextAlignment = TextAlignment.Center,
                                }.Bind(
                                    Label.TextProperty,
                                    $"{nameof(Message)}.{nameof(ChatMessage.Username)}",
                                    converter: InitialConverter.Instance),
                                new Image
                                {
                                    WidthRequest = 36,
                                    HeightRequest = 36,
                                    Aspect = Aspect.AspectFill,
                                }.Bind(
                                    Image.SourceProperty,
                                    nameof(AvatarSource)),
                            },
                        },
                    }
                    .DynamicResource(BackgroundColorProperty, "PrimaryContainer")
                    .Column(0),
                    new VerticalStackLayout
                    {
                        Spacing = 4,
                        Children =
                        {
                            new Label
                            {
                                FontSize = 11,
                                Opacity = .7,
                                LineBreakMode = LineBreakMode.TailTruncation,
                            }
                            .Bind(
                                Label.TextProperty,
                                $"{nameof(Message)}.{nameof(ChatMessage.ReplyTo)}")
                            .Bind(
                                IsVisibleProperty,
                                $"{nameof(Message)}.{nameof(ChatMessage.ReplyTo)}",
                                converter: new IsNotNullConverter()),
                            new Label
                            {
                                FontAttributes = FontAttributes.Bold,
                                FontSize = 14,
                                LineBreakMode = LineBreakMode.TailTruncation,
                            }.Bind(
                                Label.TextProperty,
                                $"{nameof(Message)}.{nameof(ChatMessage.Username)}"),
                            new Label
                            {
                                FontSize = 14,
                                LineBreakMode = LineBreakMode.WordWrap,
                            }.Bind(
                                Label.TextProperty,
                                $"{nameof(Message)}.{nameof(ChatMessage.Body)}"),
                            new ActivityIndicator
                            {
                                IsRunning = true,
                                HorizontalOptions = LayoutOptions.Start,
                            }.Bind(
                                IsVisibleProperty,
                                nameof(IsMediaLoading)),
                            new Image
                            {
                                Aspect = Aspect.AspectFit,
                                HorizontalOptions = LayoutOptions.Start,
                            }
                            .Bind(
                                Image.SourceProperty,
                                nameof(MediaImageSource))
                            .Bind(
                                WidthRequestProperty,
                                nameof(MediaWidthRequest))
                            .Bind(
                                HeightRequestProperty,
                                nameof(MediaHeightRequest))
                            .Bind(
                                IsVisibleProperty,
                                nameof(IsImageVisible)),
                            new MediaElement
                            {
                                Aspect = Aspect.AspectFit,
                                HorizontalOptions = LayoutOptions.Start,
                                ShouldAutoPlay = false,
                                ShouldShowPlaybackControls = true,
                            }
                            .Bind(
                                MediaElement.SourceProperty,
                                nameof(VideoSource))
                            .Bind(
                                WidthRequestProperty,
                                nameof(MediaWidthRequest))
                            .Bind(
                                HeightRequestProperty,
                                nameof(MediaHeightRequest))
                            .Bind(
                                IsVisibleProperty,
                                nameof(IsVideoVisible)),
                            new HorizontalStackLayout
                            {
                                Spacing = 3,
                                HorizontalOptions = LayoutOptions.End,
                                Children =
                                {
                                    new Label
                                    {
                                        FontSize = 10,
                                        VerticalTextAlignment = TextAlignment.Center,
                                    }
                                    .Bind(
                                        Label.TextProperty,
                                        nameof(AdditionalReadCount),
                                        stringFormat: "+{0}")
                                    .Bind(
                                        IsVisibleProperty,
                                        nameof(AdditionalReadCount),
                                        converter: new IsNotEqualConverter(),
                                        converterParameter: 0),
                                },
                            }
                            .Bind(
                                IsVisibleProperty,
                                nameof(HasReadAvatars))
                            .Bind(
                                BindableLayout.ItemsSourceProperty,
                                nameof(ReadAvatars))
                            .Invoke(layout =>
                                BindableLayout.SetItemTemplate(
                                    layout,
                                    new DataTemplate(() =>
                                        new Border
                                        {
                                            WidthRequest = 18,
                                            HeightRequest = 18,
                                            StrokeThickness = 0,
                                            StrokeShape = new RoundRectangle
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
                                                        FontAttributes = FontAttributes.Bold,
                                                        HorizontalTextAlignment = TextAlignment.Center,
                                                        VerticalTextAlignment = TextAlignment.Center,
                                                    }.Bind(
                                                        Label.TextProperty,
                                                        nameof(ReadAvatar.Initial)),
                                                    new Image
                                                    {
                                                        Aspect = Aspect.AspectFill,
                                                    }.Bind(
                                                        Image.SourceProperty,
                                                        nameof(ReadAvatar.Source)),
                                                },
                                            },
                                        }
                                        .DynamicResource(
                                            BackgroundColorProperty,
                                            "PrimaryContainer")
                                        .Bind(
                                            SemanticProperties.DescriptionProperty,
                                            nameof(ReadAvatar.UserId),
                                            stringFormat: "Read by {0}")))),
                        },
                    }.Column(1),
                },
            },
        }
#if WINDOWS || MACCATALYST
        .Invoke(root =>
            FlyoutBase.SetContextFlyout(
                root,
                new MenuFlyout
                {
                    new MenuFlyoutItem
                    {
                        Text = "Reply",
                        Command = RequestReplyCommand,
                    },
                    new MenuFlyoutItem
                    {
                        Text = "Copy text",
                        Command = CopyTextCommand,
                    },
#if WINDOWS
                    new MenuFlyoutItem
                    {
                        Text = "Save file",
                        Command = SaveSelectedMediaCommand,
                    },
#endif
                }))
#endif
        ;
    }

    private static void OnMessageChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((ChatMessageView)bindable).LoadMessageAsync();

    private static void OnInputChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((ChatMessageView)bindable).LoadMessageAsync();

    private async Task LoadMessageAsync()
    {
        var message = Message;

        AvatarSource = null;
        MediaImageSource = null;
        VideoSource = null;
        IsImageVisible = false;
        IsVideoVisible = false;
        IsMediaLoading =
            message?.MediaKind is not null and not ChatMediaKind.None;
        ReadAvatars.Clear();
        AdditionalReadCount = 0;
        HasReadAvatars = false;

        if (message is null)
        {
            return;
        }

        var mediaSize = FitMediaSize(
            message.MediaWidth,
            message.MediaHeight,
            message.MediaKind);
        MediaWidthRequest = mediaSize.Width;
        MediaHeightRequest = mediaSize.Height;

        var loads = new List<Task>
        {
            LoadReadAvatarsAsync(message),
        };

        if (!string.IsNullOrWhiteSpace(message.AvatarUrl))
        {
            loads.Add(LoadAvatarAsync(message));
        }

        if (!string.IsNullOrWhiteSpace(message.MediaSourceJson))
        {
            loads.Add(LoadMediaAsync(message));
        }

        await Task.WhenAll(loads);
    }

    private async Task LoadReadAvatarsAsync(ChatMessage message)
    {
        const int maximumAvatars = 5;
        var userIds = message.ReadByUserIds?.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries) ?? [];

        foreach (var userId in userIds.Take(maximumAvatars))
        {
            var avatar = new ReadAvatar(
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
                var avatarUrl = await Room.MemberAvatarUrl(userId);

                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    continue;
                }

                var bytes = await MatrixClient.GetThumbnailAsync(
                    avatarUrl,
                    36,
                    36,
                    isJson: false);

                if (Message?.Id == message.Id &&
                    Message.ReadByUserIds == message.ReadByUserIds)
                {
                    avatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load read avatar: {exception}");
            }
        }

        AdditionalReadCount = Math.Max(
            0,
            userIds.Length - maximumAvatars);
        HasReadAvatars = userIds.Length > 0;
    }

    private async Task LoadAvatarAsync(ChatMessage message)
    {
        if (MatrixClient is null)
        {
            return;
        }

        try
        {
            var bytes = await MatrixClient.GetThumbnailAsync(
                message.AvatarUrl!,
                72,
                72,
                isJson: false);

            if (Message?.Id == message.Id)
            {
                AvatarSource = ImageSource.FromStream(
                    () => new MemoryStream(bytes));
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not load avatar: {exception}");
        }
    }

    private async Task LoadMediaAsync(ChatMessage message)
    {
        if (MatrixClient is null)
        {
            return;
        }

        try
        {
            if (message.MediaKind is ChatMediaKind.Image)
            {
                var bytes = await MatrixClient.GetThumbnailAsync(
                    message.MediaSourceJson!,
                    560,
                    440);

                if (Message?.Id == message.Id)
                {
                    MediaImageSource = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                    IsImageVisible = true;
                }
            }
            else if (message.MediaKind is ChatMediaKind.Video)
            {
                var path = await MatrixClient.GetVideoFileAsync(
                    message.MediaSourceJson!,
                    message.Filename ?? "video",
                    message.MimeType ?? "video/mp4");

                if (Message?.Id == message.Id)
                {
                    VideoSource = PlaybackMediaSource.FromFile(path);
                    IsVideoVisible = true;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not load inline media: {exception}");
        }
        finally
        {
            if (Message?.Id == message.Id)
            {
                IsMediaLoading = false;
            }
        }
    }

    [RelayCommand]
    private void Measure()
    {
        if (Message is not null &&
            Content.Height > 0)
        {
            Execute(
                MessageMeasuredCommand,
                new ChatMessageMeasurement(
                    Message,
                    Content.Height));
        }
    }

    [RelayCommand]
    private void RequestReply() =>
        Execute(ReplyCommand, Message);

    [RelayCommand]
    private async Task CopyTextAsync()
    {
        if (Message is not null)
        {
            await Clipboard.Default.SetTextAsync(Message.Body);
        }
    }

    [RelayCommand]
    private void SaveSelectedMedia() =>
        Execute(SaveMediaCommand, Message);

    [RelayCommand]
    private void OpenMessageMenu() =>
        Execute(MessageMenuCommand, Message);

    private static void Execute(
        ICommand? command,
        object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private static string GetUserInitial(string userId)
    {
        var localpart = userId.TrimStart('@');
        return string.IsNullOrEmpty(localpart)
            ? "?"
            : localpart[..1].ToUpperInvariant();
    }
}

public sealed partial class ReadAvatar(
    string userId,
    string initial) : ObservableObject
{
    public string UserId { get; } = userId;
    public string Initial { get; } = initial;

    [ObservableProperty]
    public partial ImageSource? Source { get; set; }
}
