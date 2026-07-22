using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;
using MediaElement = CommunityToolkit.Maui.Views.MediaElement;
using PlaybackMediaSource = CommunityToolkit.Maui.Views.MediaSource;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView, IDisposable
{
#if DEBUG
    static Chat()
    {
        var size = FitMediaSize(1920, 1080, ChatMediaKind.Video);
        Debug.Assert(size.Width == 280 && Math.Abs(size.Height - 157.5) < 0.01);
    }
#endif

    // Services
    private readonly ManagedMatrixClient _matrixClient;

    // UI
    private readonly CollectionView _timelineView;
    private readonly Entry _messageEntry;
    private readonly Grid _loadingOverlay;

    // State
    private ObservableTimeline? _observableTimeline;
    private Room? _room;

    private bool _isLoading;
    private bool _isSending;
    private bool _disposed;

    // Backing fields
    private string _messageText = string.Empty;
    private string _errorMessage = string.Empty;

    // Binding properties
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string MessageText
    {
        get => _messageText;
        set
        {
            if (_messageText == value)
            {
                return;
            }

            _messageText = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            _loadingOverlay.IsVisible = value;
            _loadingOverlay.InputTransparent = !value;

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (_isSending == value)
            {
                return;
            }

            _isSending = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));

            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanSend =>
        !IsLoading &&
        !IsSending &&
        _observableTimeline is not null &&
        !string.IsNullOrWhiteSpace(MessageText);

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorMessage);

    public Chat(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        BindingContext = this;

        _timelineView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsSource = Messages,
            ItemTemplate = new DataTemplate(CreateMessageItem),
            EmptyView = CreateEmptyView(),
            ItemsLayout = new LinearItemsLayout(
                ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 4,
            },
        };

        _messageEntry = new Entry
        {
            Placeholder = "Message",
            ReturnType = ReturnType.Send,
        };

        _messageEntry.SetBinding(
            Entry.TextProperty,
            new Binding(
                nameof(MessageText),
                source: this,
                mode: BindingMode.TwoWay));

        _messageEntry.Completed += OnMessageEntryCompleted;

        _loadingOverlay = new Grid
        {
            IsVisible = false,
            InputTransparent = true,
            BackgroundColor = Color.FromArgb("#66000000"),
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };

        Build();
    }

    public async Task SetRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_room?.Id() == room.Id() &&
            _observableTimeline is not null)
        {
            return;
        }

        Debug.WriteLine(
            $"Opening timeline for {room.Id()}");

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            DisposeTimeline();

            _room = room;

            Messages.Clear();

            cancellationToken.ThrowIfCancellationRequested();

            Debug.WriteLine(
                "Creating ObservableTimeline...");

            var observableTimeline =
                await _matrixClient.GetObservableTimelineAsync(room);

            cancellationToken.ThrowIfCancellationRequested();

            if (_disposed)
            {
                observableTimeline.Dispose();
                return;
            }

            _observableTimeline = observableTimeline;

            _observableTimeline.CollectionChanged +=
                OnTimelineCollectionChanged;

            Debug.WriteLine(
                "ObservableTimeline created.");

            RebuildMessages();

            Debug.WriteLine(
                $"Timeline loaded with {_observableTimeline.Count} items.");

            await ScrollToBottomAsync();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine(
                $"Timeline loading cancelled for {room.Id()}.");

            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Failed to load timeline for {room.Id()}: {exception}");
        }
        finally
        {
            Debug.WriteLine(
                "Timeline loading completed.");

            IsLoading = false;

            OnPropertyChanged(nameof(CanSend));
            SendMessageCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (_observableTimeline is null)
        {
            return;
        }

        var text = MessageText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            var content = CreateTextMessageContent(
                _observableTimeline.Timeline,
                text);

            await _observableTimeline.Timeline.Send(content);

            MessageText = string.Empty;

            _messageEntry.Focus();

            await ScrollToBottomAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Failed to send message to {_room?.Id()}: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    private static RoomMessageEventContentWithoutRelation
        CreateTextMessageContent(
            Timeline timeline,
            string text)
    {
        var content = timeline.CreateMessageContent(
            new MessageType.Text(
                new TextMessageContent(
                    text,
                    null)));

        return content
            ?? throw new InvalidOperationException(
                "Could not create Matrix text-message content.");
    }

    private void OnMessageEntryCompleted(
        object? sender,
        EventArgs e)
    {
        if (SendMessageCommand.CanExecute(null))
        {
            SendMessageCommand.Execute(null);
        }
    }

    private void OnTimelineCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        RebuildMessages();

        _ = ScrollToBottomAsync();
    }

    private void RebuildMessages()
    {
        if (_observableTimeline is null)
        {
            Messages.Clear();
            return;
        }

        var parsedMessages = _observableTimeline
            .Select(TryCreateChatMessage)
            .Where(message => message is not null)
            .Cast<ChatMessage>()
            .ToArray();

        Messages.Clear();

        foreach (var message in parsedMessages)
        {
            Messages.Add(message);
        }
    }

    private static ChatMessage? TryCreateChatMessage(
        TimelineItem timelineItem)
    {
        var eventItem = timelineItem.AsEvent();

        if (eventItem is null)
        {
            return null;
        }

        if (eventItem.Content is not TimelineItemContent.MsgLike msgLike)
        {
            return null;
        }

        var username = ResolveUsername(eventItem);
        var avatarUrl = eventItem.SenderProfile is ProfileDetails.Ready ready
            ? ready.AvatarUrl
            : null;
        var id = timelineItem.UniqueId().Id;

        return msgLike.Content.Kind switch
        {
            MsgLikeKind.Message message => message.Content.MsgType switch
            {
                MessageType.Image image => new ChatMessage(
                    id, username, image.Content.Caption ?? image.Content.Filename,
                    eventItem.IsOwn, avatarUrl, ChatMediaKind.Image,
                    image.Content.Source.ToJson(), image.Content.Filename,
                    image.Content.Info?.Mimetype, image.Content.Info?.Width,
                    image.Content.Info?.Height),
                MessageType.Video video => new ChatMessage(
                    id, username, video.Content.Caption ?? video.Content.Filename,
                    eventItem.IsOwn, avatarUrl, ChatMediaKind.Video,
                    video.Content.Source.ToJson(), video.Content.Filename,
                    video.Content.Info?.Mimetype, video.Content.Info?.Width,
                    video.Content.Info?.Height),
                _ => new ChatMessage(
                    id, username, message.Content.Body,
                    eventItem.IsOwn, avatarUrl),
            },
            MsgLikeKind.Sticker sticker => new ChatMessage(
                id, username, sticker.Body, eventItem.IsOwn, avatarUrl,
                ChatMediaKind.Image, sticker.Source.ToJson(), sticker.Body,
                sticker.Info.Mimetype, sticker.Info.Width,
                sticker.Info.Height),
            _ => null,
        };
    }

    private static string ResolveUsername(
        EventTimelineItem eventItem)
    {
        if (eventItem.SenderProfile is ProfileDetails.Ready ready &&
            !string.IsNullOrWhiteSpace(ready.DisplayName))
        {
            return ready.DisplayName;
        }

        return eventItem.Sender;
    }

    private async Task ScrollToBottomAsync()
    {
        if (Messages.Count == 0)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed || Messages.Count == 0)
            {
                return;
            }

            _timelineView.ScrollTo(
                Messages[^1],
                position: ScrollToPosition.End,
                animate: false);
        });
    }

    private void Build()
    {
        var sendButton = new Button
        {
            Text = "Send",
            VerticalOptions = LayoutOptions.Center,
        };

        sendButton.SetBinding(
            Button.CommandProperty,
            new Binding(
                nameof(SendMessageCommand),
                source: this));

        var composer = new Grid
        {
            Padding = 12,
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        composer.Add(_messageEntry, column: 0);
        composer.Add(sendButton, column: 1);

        var errorLabel = new Label
        {
            TextColor = Colors.Red,
            Margin = new Thickness(12, 4),
            LineBreakMode = LineBreakMode.WordWrap,
        };

        errorLabel.SetBinding(
            Label.TextProperty,
            new Binding(
                nameof(ErrorMessage),
                source: this));

        errorLabel.SetBinding(
            IsVisibleProperty,
            new Binding(
                nameof(HasError),
                source: this));

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        layout.Add(_timelineView, row: 0);
        layout.Add(errorLabel, row: 1);
        layout.Add(composer, row: 2);

        layout.AddWithSpan(
            _loadingOverlay,
            row: 0,
            column: 0,
            rowSpan: 3,
            columnSpan: 1);

        layout.SafeAreaEdges = SafeAreaEdges.All;

        Content = layout;
    }

    private View CreateMessageItem()
    {
        var usernameLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        var bodyLabel = new Label
        {
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        var avatar = new Image
        {
            WidthRequest = 36,
            HeightRequest = 36,
            Aspect = Aspect.AspectFill,
        };
        var avatarFallback = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var avatarFrame = new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Content = new Grid { Children = { avatarFallback, avatar } },
        };
        avatarFrame.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "PrimaryContainer");

        var image = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false,
        };

        var video = new MediaElement
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            ShouldAutoPlay = false,
            ShouldShowPlaybackControls = true,
            IsVisible = false,
        };

        var mediaLoading = new ActivityIndicator
        {
            IsRunning = true,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Start,
        };

        var messageContent = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { usernameLabel, bodyLabel, mediaLoading, image, video },
        };

        var messageGrid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        messageGrid.Add(avatarFrame, column: 0);
        messageGrid.Add(messageContent, column: 1);

        var root = new Border
        {
            Margin = new Thickness(12, 2),
            Padding = new Thickness(10, 8),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 8,
            },
            Content = messageGrid,
        };

        root.BindingContextChanged += async (_, _) =>
        {
            if (root.BindingContext is not ChatMessage message)
            {
                return;
            }

            usernameLabel.Text = message.Username;
            bodyLabel.Text = message.Body;
            avatarFallback.Text = message.Username[..1].ToUpperInvariant();
            avatar.Source = null;
            image.Source = null;
            image.IsVisible = false;
            video.Source = null;
            video.IsVisible = false;
            var mediaSize = FitMediaSize(
                message.MediaWidth,
                message.MediaHeight,
                message.MediaKind);
            image.WidthRequest = video.WidthRequest = mediaSize.Width;
            image.HeightRequest = video.HeightRequest = mediaSize.Height;
            mediaLoading.IsVisible = message.MediaKind is not ChatMediaKind.None;

            var loads = new List<Task>();
            if (!string.IsNullOrWhiteSpace(message.AvatarUrl))
            {
                loads.Add(LoadAvatarAsync(message));
            }
            if (!string.IsNullOrWhiteSpace(message.MediaSourceJson))
            {
                loads.Add(LoadMediaAsync(message));
            }
            await Task.WhenAll(loads);
        };

        return root;

        async Task LoadAvatarAsync(ChatMessage message)
        {
            try
            {
                var bytes = await _matrixClient.GetThumbnailAsync(
                    message.AvatarUrl!, 72, 72, isJson: false);
                if (root.BindingContext is ChatMessage current &&
                    current.Id == message.Id)
                {
                    avatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not load avatar: {exception}");
            }
        }

        async Task LoadMediaAsync(ChatMessage message)
        {
            try
            {
                if (message.MediaKind is ChatMediaKind.Image)
                {
                    var bytes = await _matrixClient.GetThumbnailAsync(
                        message.MediaSourceJson!, 560, 440);
                    if (root.BindingContext is ChatMessage current &&
                        current.Id == message.Id)
                    {
                        image.Source = ImageSource.FromStream(
                            () => new MemoryStream(bytes));
                        image.IsVisible = true;
                    }
                }
                else if (message.MediaKind is ChatMediaKind.Video)
                {
                    var path = await _matrixClient.GetVideoFileAsync(
                        message.MediaSourceJson!,
                        message.Filename ?? "video",
                        message.MimeType ?? "video/mp4");
                    if (root.BindingContext is ChatMessage current &&
                        current.Id == message.Id)
                    {
                        video.Source = PlaybackMediaSource.FromFile(path);
                        video.IsVisible = true;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not load inline media: {exception}");
            }
            finally
            {
                if (root.BindingContext is ChatMessage current &&
                    current.Id == message.Id)
                {
                    mediaLoading.IsVisible = false;
                }
            }
        }
    }

    private static Size FitMediaSize(
        ulong? width,
        ulong? height,
        ChatMediaKind kind)
    {
        const double maxWidth = 280;
        const double maxHeight = 220;

        if (width is > 0 && height is > 0)
        {
            var scale = Math.Min(
                1,
                Math.Min(maxWidth / width.Value, maxHeight / height.Value));
            return new Size(width.Value * scale, height.Value * scale);
        }

        return kind is ChatMediaKind.Video
            ? new Size(maxWidth, 157.5)
            : new Size(maxWidth, 210);
    }

    private static View CreateEmptyView()
    {
        return new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = "No messages yet",
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new Label
                {
                    Text = "Send the first message.",
                    Opacity = 0.7,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }

    private void DisposeTimeline()
    {
        if (_observableTimeline is null)
        {
            return;
        }

        _observableTimeline.CollectionChanged -=
            OnTimelineCollectionChanged;

        _observableTimeline.Dispose();
        _observableTimeline = null;

        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _messageEntry.Completed -=
            OnMessageEntryCompleted;

        DisposeTimeline();

        _loadingOverlay.IsVisible = false;
        _loadingOverlay.InputTransparent = true;

        Messages.Clear();

        _room = null;

        GC.SuppressFinalize(this);
    }
}

public sealed record ChatMessage(
    string Id,
    string Username,
    string Body,
    bool IsOwn,
    string? AvatarUrl = null,
    ChatMediaKind MediaKind = ChatMediaKind.None,
    string? MediaSourceJson = null,
    string? Filename = null,
    string? MimeType = null,
    ulong? MediaWidth = null,
    ulong? MediaHeight = null);

public enum ChatMediaKind
{
    None,
    Image,
    Video,
}
