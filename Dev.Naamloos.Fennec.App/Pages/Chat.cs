using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Dev.Naamloos.Fennec.App.Components;
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
    private const int BottomFollowThreshold = 2;
    private const int HistoryLoadThreshold = 5;

#if DEBUG
    static Chat()
    {
        var size = FitMediaSize(1920, 1080, ChatMediaKind.Video);
        Debug.Assert(
            size.Width == 280 &&
            Math.Abs(size.Height - 157.5) < 0.01);
        Debug.Assert(
            FormatTypingText(["Alice"]) == "Alice is typing…");
        Debug.Assert(
            FormatTypingText(["Alice", "Bob"]) ==
            "Alice and Bob are typing…");
        Debug.Assert(
            FormatTypingText(["Alice", "Bob", "Carol"]) ==
            "Alice, Bob, and 1 other are typing…");
    }
#endif

    // Services
    private readonly ManagedMatrixClient _matrixClient;

    // UI
    private readonly CollectionView _timelineView;
    private readonly Entry _messageEntry;
    private readonly Label _typingLabel;
    private readonly Grid _loadingOverlay;
    private readonly BoxView _bottomSpacer = new()
    {
        BackgroundColor = Colors.Transparent,
    };

    // State
    private ObservableTimeline? _observableTimeline;
    private Room? _room;
    private TypingListener? _typingListener;
    private TaskHandle? _typingHandle;
    private int _typingUpdateVersion;

    private bool _isLoading;
    private bool _isSending;
    private bool _isLoadingMoreHistory;
    private bool _isNearBottom = true;
    private bool _ignoreScrollEvents;
    private bool _scrollQueued;
    private bool _disposed;
    private System.Action? _replyTargetChanged;
    private readonly Dictionary<string, double> _messageHeights = [];

    // Backing fields
    private string _messageText = string.Empty;
    private string _errorMessage = string.Empty;
    private ChatMessage? _replyTo;

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

            if (_room is { } room)
            {
                _ = SendTypingNoticeAsync(
                    room,
                    !string.IsNullOrWhiteSpace(value));
            }
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
            ItemTemplate = new DataTemplate(
                () => new ChatMessageView(CreateMessageItem)),
            EmptyView = CreateEmptyView(),

            /*
             * Preserve the visible scroll position when older messages are
             * inserted at the beginning of the collection.
             */
            ItemsUpdatingScrollMode =
                ItemsUpdatingScrollMode.KeepLastItemInView,

            ItemsLayout = new LinearItemsLayout(
                ItemsLayoutOrientation.Vertical)
            {
                ItemSpacing = 4,
            },
            Header = _bottomSpacer,
        };

        _timelineView.Scrolled += OnTimelineScrolled;
        _timelineView.SizeChanged += OnTimelineViewSizeChanged;

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

        _typingLabel = new Label
        {
            Margin = new Thickness(12, 2),
            FontSize = 12,
            Opacity = 0.7,
            IsVisible = false,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

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

            /*
             * A newly opened room should start at its newest message.
             */
            _isNearBottom = true;
            _ignoreScrollEvents = true;
            _isLoadingMoreHistory = false;

            Messages.Clear();
            _messageHeights.Clear();
            UpdateBottomSpacer();

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

            SubscribeToTypingNotifications(room);

            Debug.WriteLine(
                "ObservableTimeline created.");

            SynchronizeMessages();

            Debug.WriteLine(
                $"Timeline loaded with {_observableTimeline.Count} items.");

            await ScrollToBottomAfterLayoutAsync();
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

            if (_replyTo?.EventId is { } eventId)
            {
                await _observableTimeline.Timeline.SendReply(content, eventId);
            }
            else
            {
                await _observableTimeline.Timeline.Send(content);
            }

            MessageText = string.Empty;
            SetReplyTarget(null);

            _messageEntry.Focus();

            /*
             * Sending a message should always return the sender to the
             * newest part of the timeline.
             */
            _isNearBottom = true;

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

    private async Task AttachFileAsync()
    {
        if (_observableTimeline is null || IsSending)
        {
            return;
        }

        try
        {
            var file = await FilePicker.Default.PickAsync();
            if (file is null || _observableTimeline is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var data = new MemoryStream();
            await stream.CopyToAsync(data);

            IsSending = true;
            ErrorMessage = string.Empty;
            var mimeType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;
            var url = await _matrixClient.UploadMediaAsync(mimeType, data.ToArray());
            using var source = MediaSource.FromUrl(url);
            var content = _observableTimeline.Timeline.CreateMessageContent(
                CreateAttachmentMessage(
                    file.FileName,
                    mimeType,
                    (ulong)data.Length,
                    source))
                ?? throw new InvalidOperationException("Could not create attachment content.");
            await _observableTimeline.Timeline.Send(content);
            _isNearBottom = true;
            await ScrollToBottomAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Debug.WriteLine($"Failed to upload attachment: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    private static MessageType CreateAttachmentMessage(
        string filename,
        string mimeType,
        ulong size,
        MediaSource source) =>
        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? new MessageType.Image(new ImageMessageContent(
                filename, null, null, source,
                new ImageInfo(null, null, mimeType, size, null, null, null, null)))
            : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                ? new MessageType.Video(new VideoMessageContent(
                    filename, null, null, source,
                    new VideoInfo(null, null, null, mimeType, size, null, null, null)))
                : new MessageType.File(new FileMessageContent(
                    filename, null, null, source,
                    new uniffi.matrix_sdk_ffi.FileInfo(mimeType, size, null, null)));

    private void SetReplyTarget(ChatMessage? message)
    {
        _replyTo = message;
        _replyTargetChanged?.Invoke();
        _messageEntry.Focus();
    }

    private async Task ShowMessageMenuAsync(ChatMessage message)
    {
        if (message.EventId is null || Window?.Page is not Page page)
        {
            return;
        }

        if (await page.DisplayActionSheetAsync("Message", "Cancel", null, "Reply") == "Reply")
        {
            SetReplyTarget(message);
        }
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

        /*
         * Capture the current follow state before modifying Messages.
         *
         * When the user is reading older messages, incoming events and
         * timeline replacements must not pull the view back to the bottom.
         */
        var shouldFollowBottom = _isNearBottom;

        SynchronizeMessages();

        if (shouldFollowBottom)
        {
            _ = ScrollToBottomAsync();
        }
    }

    private void SynchronizeMessages()
    {
        if (_observableTimeline is null)
        {
            Messages.Clear();
            return;
        }

        var desiredMessages = _observableTimeline
            .Select(TryCreateChatMessage)
            .Where(message => message is not null)
            .Cast<ChatMessage>()
            .ToArray();

        /*
         * Reconcile by the stable Matrix timeline-item ID.
         *
         * Do not clear and repopulate the collection. Clearing causes the
         * CollectionView to discard its visible-item anchor and jump.
         */
        for (var desiredIndex = 0;
             desiredIndex < desiredMessages.Length;
             desiredIndex++)
        {
            var desiredMessage = desiredMessages[desiredIndex];

            if (desiredIndex < Messages.Count &&
                Messages[desiredIndex].Id == desiredMessage.Id)
            {
                /*
                 * Timeline Set updates may change send state, profile data,
                 * edited content or media metadata.
                 */
                if (Messages[desiredIndex] != desiredMessage)
                {
                    Messages[desiredIndex] = desiredMessage;
                }

                continue;
            }

            var existingIndex = FindMessageIndex(
                desiredMessage.Id, desiredIndex + 1);

            if (existingIndex >= 0)
            {
                Messages.Move(existingIndex, desiredIndex);

                if (Messages[desiredIndex] != desiredMessage)
                {
                    Messages[desiredIndex] = desiredMessage;
                }
            }
            else
            {
                Messages.Insert(desiredIndex, desiredMessage);
            }
        }

        while (Messages.Count > desiredMessages.Length)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        _messageHeights.Keys
            .Where(id => !Messages.Any(message => message.Id == id))
            .ToList()
            .ForEach(id => _messageHeights.Remove(id));
        UpdateBottomSpacer();
    }

    private int FindMessageIndex(
        string id,
        int startIndex)
    {
        for (var index = Math.Max(0, startIndex);
             index < Messages.Count;
             index++)
        {
            if (Messages[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private void OnTimelineScrolled(
        object? sender,
        ItemsViewScrolledEventArgs e)
    {
        if (_disposed ||
            _ignoreScrollEvents ||
            Messages.Count == 0)
        {
            return;
        }

        _isNearBottom =
            e.LastVisibleItemIndex >=
            Messages.Count - 1 - BottomFollowThreshold;

        _timelineView.ItemsUpdatingScrollMode = _isNearBottom
            ? ItemsUpdatingScrollMode.KeepLastItemInView
            : ItemsUpdatingScrollMode.KeepScrollOffset;

        /*
         * Matrix pagination runs backwards, so load more history when the
         * first visible item approaches the start of the collection.
         */
        if (e.FirstVisibleItemIndex <= HistoryLoadThreshold)
        {
            _ = LoadMoreHistoryAsync();
        }
    }

    private async Task LoadMoreHistoryAsync()
    {
        if (_disposed ||
            _isLoadingMoreHistory ||
            _observableTimeline is null ||
            _observableTimeline.IsLoadingHistory ||
            _observableTimeline.HasReachedStart)
        {
            return;
        }

        _isLoadingMoreHistory = true;

        try
        {
            await _observableTimeline.LoadMoreHistoryAsync();
        }
        catch (ObjectDisposedException)
        {
            /*
             * The active room changed while pagination was running.
             */
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Failed to load older messages for {_room?.Id()}: " +
                exception);
        }
        finally
        {
            _isLoadingMoreHistory = false;
        }
    }

    private void OnTimelineViewSizeChanged(
        object? sender,
        EventArgs e)
    {
        UpdateBottomSpacer();

        if (_disposed ||
            !_isNearBottom ||
            Messages.Count == 0)
        {
            return;
        }

        /*
         * The software keyboard changes the available height of the
         * CollectionView. Wait until the resized layout has been measured
         * before positioning the final message above the composer.
         */
        _timelineView.Dispatcher.Dispatch(
            () =>
            {
                if (!_disposed &&
                    _isNearBottom &&
                    Messages.Count > 0)
                {
                    _ = ScrollToBottomAsync();
                }
            });
    }

    private async Task ScrollToBottomAsync()
    {
        if (_disposed || Messages.Count == 0)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(QueueScrollToBottom);
    }

    private async Task ScrollToBottomAfterLayoutAsync()
    {
        if (_disposed || Messages.Count == 0)
        {
            return;
        }

        var completionSource =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed || Messages.Count == 0)
            {
                completionSource.TrySetResult(true);
                return;
            }

            /*
             * Dispatching after a short delay allows the CollectionView to
             * create and measure its initial item containers.
             */
            _timelineView.Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(50),
                () =>
                {
                    try
                    {
                        if (!_disposed && Messages.Count > 0)
                        {
                            QueueScrollToBottom();
                        }

                        completionSource.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        completionSource.TrySetException(exception);
                    }
                });
        });

        await completionSource.Task;
    }

    private void QueueScrollToBottom()
    {
        if (_scrollQueued)
        {
            return;
        }

        _scrollQueued = true;
        _timelineView.Dispatcher.Dispatch(() =>
        {
            _scrollQueued = false;
            ScrollToBottomCore();
        });
    }

    private void ScrollToBottomCore()
    {
        if (_disposed || Messages.Count == 0)
        {
            return;
        }

        _ignoreScrollEvents = true;
        _isNearBottom = true;
        _timelineView.ItemsUpdatingScrollMode =
            ItemsUpdatingScrollMode.KeepLastItemInView;

        _timelineView.ScrollTo(
            Messages.Count - 1,
            groupIndex: -1,
            position: ScrollToPosition.End,
            animate: false);

        /*
         * CollectionView may emit its Scrolled event asynchronously after
         * ScrollTo returns. Keep the guard active until that event settles.
         */
        _timelineView.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(75),
            () =>
            {
                if (!_disposed)
                {
                    _ignoreScrollEvents = false;
                    _isNearBottom = true;
                }
            });
    }

    private void UpdateBottomSpacer()
    {
        if (Messages.Count == 0 || Messages.Count > 12 || _timelineView.Height <= 0)
        {
            _bottomSpacer.HeightRequest = 0;
            return;
        }

        var contentHeight = Messages.Sum(message =>
            _messageHeights.TryGetValue(message.Id, out var height) ? height : 68d);
        _bottomSpacer.HeightRequest = Math.Max(0, _timelineView.Height - contentHeight);
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

        var avatarUrl =
            eventItem.SenderProfile is ProfileDetails.Ready ready
                ? ready.AvatarUrl
                : null;

        var id = timelineItem.UniqueId().Id;
        var eventId = (eventItem.EventOrTransactionId as EventOrTransactionId.EventId)?.EventIdValue;
        var readByUserIds = string.Join(
            '\n',
            eventItem.ReadReceipts.Keys
                .Where(userId => userId != eventItem.Sender)
                .Order(StringComparer.Ordinal));
        var replyTo = FormatReplyPreview(msgLike.Content.InReplyTo);

        return msgLike.Content.Kind switch
        {
            MsgLikeKind.Message message => message.Content.MsgType switch
            {
                MessageType.Image image => new ChatMessage(
                    id,
                    username,
                    image.Content.Caption ?? image.Content.Filename,
                    eventItem.IsOwn,
                    avatarUrl,
                    ChatMediaKind.Image,
                    image.Content.Source.ToJson(),
                    image.Content.Filename,
                    image.Content.Info?.Mimetype,
                    image.Content.Info?.Width,
                    image.Content.Info?.Height,
                    readByUserIds,
                    eventId,
                    replyTo),

                MessageType.Video video => new ChatMessage(
                    id,
                    username,
                    video.Content.Caption ?? video.Content.Filename,
                    eventItem.IsOwn,
                    avatarUrl,
                    ChatMediaKind.Video,
                    video.Content.Source.ToJson(),
                    video.Content.Filename,
                    video.Content.Info?.Mimetype,
                    video.Content.Info?.Width,
                    video.Content.Info?.Height,
                    readByUserIds,
                    eventId,
                    replyTo),

                MessageType.File file => new ChatMessage(
                    id,
                    username,
                    file.Content.Caption ?? file.Content.Filename,
                    eventItem.IsOwn,
                    avatarUrl,
                    ChatMediaKind.File,
                    file.Content.Source.ToJson(),
                    file.Content.Filename,
                    file.Content.Info?.Mimetype,
                    ReadByUserIds: readByUserIds,
                    EventId: eventId,
                    ReplyTo: replyTo),

                _ => new ChatMessage(
                    id,
                    username,
                    message.Content.Body,
                    eventItem.IsOwn,
                    avatarUrl,
                    ReadByUserIds: readByUserIds,
                    EventId: eventId,
                    ReplyTo: replyTo),
            },

            MsgLikeKind.Sticker sticker => new ChatMessage(
                id,
                username,
                sticker.Body,
                eventItem.IsOwn,
                avatarUrl,
                ChatMediaKind.Image,
                sticker.Source.ToJson(),
                sticker.Body,
                sticker.Info.Mimetype,
                sticker.Info.Width,
                sticker.Info.Height,
                readByUserIds,
                eventId,
                replyTo),

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

    private static string? FormatReplyPreview(InReplyToDetails? reply)
    {
        if (reply is null)
        {
            return null;
        }

        using var details = reply.Event();
        var body = details is EmbeddedEventDetails.Ready
        {
            Content: TimelineItemContent.MsgLike
            {
                Content.Kind: MsgLikeKind.Message message,
            },
        }
            ? message.Content.Body
            : null;

        return string.IsNullOrWhiteSpace(body)
            ? "Replying to a message"
            : $"Replying to: {body.ReplaceLineEndings(" ")}";
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

        var attachButton = new Button
        {
            Text = "+",
            FontSize = 24,
            WidthRequest = 40,
            HeightRequest = 40,
            CornerRadius = 20,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Command = new Command(async () => await AttachFileAsync()),
        };
        SemanticProperties.SetDescription(attachButton, "Attach a file");

        var composer = new Grid
        {
            Padding = 12,
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        composer.Add(attachButton, column: 0);
        composer.Add(_messageEntry, column: 1);
        composer.Add(sendButton, column: 2);

        var replyLabel = new Label
        {
            Margin = new Thickness(12, 0),
            FontSize = 12,
            Opacity = .75,
            IsVisible = false,
        };

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
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        layout.Add(_timelineView, row: 0);
        layout.Add(_typingLabel, row: 1);
        layout.Add(errorLabel, row: 2);
        layout.Add(replyLabel, row: 3);
        layout.Add(composer, row: 4);

        layout.AddWithSpan(
            _loadingOverlay,
            row: 0,
            column: 0,
            rowSpan: 5,
            columnSpan: 1);

        layout.SafeAreaEdges = SafeAreaEdges.All;

        Content = layout;

        void UpdateReplyLabel()
        {
            replyLabel.Text = _replyTo is null ? string.Empty : $"Replying to {_replyTo.Username}";
            replyLabel.IsVisible = _replyTo is not null;
        }

        _replyTargetChanged = UpdateReplyLabel;
    }

    private View CreateMessageItem()
    {
        var replyLabel = new Label
        {
            FontSize = 11,
            Opacity = .7,
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false,
        };
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
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 18,
            },
            Content = new Grid
            {
                Children =
                {
                    avatarFallback,
                    avatar,
                },
            },
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

        var readAvatars = new HorizontalStackLayout
        {
            Spacing = 3,
            HorizontalOptions = LayoutOptions.End,
            IsVisible = false,
        };

        var messageContent = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                replyLabel,
                usernameLabel,
                bodyLabel,
                mediaLoading,
                image,
                video,
                readAvatars,
            },
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

#if WINDOWS || MACCATALYST
        // MAUI desktop context menu: FlyoutBase.ContextFlyout.
        var replyMenuItem = new MenuFlyoutItem { Text = "Reply" };
        replyMenuItem.Clicked += (_, _) =>
        {
            if (root.BindingContext is ChatMessage message)
            {
                SetReplyTarget(message);
            }
        };
        var contextMenu = new MenuFlyout();
        contextMenu.Add(replyMenuItem);
        var copyMenuItem = new MenuFlyoutItem { Text = "Copy text" };
        copyMenuItem.Clicked += async (_, _) =>
        {
            if (root.BindingContext is ChatMessage message)
            {
                await Clipboard.Default.SetTextAsync(message.Body);
            }
        };
        contextMenu.Add(copyMenuItem);
#if WINDOWS
        var saveMenuItem = new MenuFlyoutItem { Text = "Save file" };
        saveMenuItem.Clicked += async (_, _) =>
        {
            if (root.BindingContext is ChatMessage
                { MediaSourceJson: not null } message)
            {
                await SaveMediaAsync(message);
            }
        };
        contextMenu.Add(saveMenuItem);
#endif
        FlyoutBase.SetContextFlyout(root, contextMenu);
#endif

        root.SizeChanged += (_, _) =>
        {
            if (root.BindingContext is ChatMessage message && root.Height > 0)
            {
                _messageHeights[message.Id] = root.Height;
                UpdateBottomSpacer();
            }
        };

        var longPress = new CommunityToolkit.Maui.Behaviors.TouchBehavior();
        longPress.LongPressCommand = new Command(async () =>
        {
            if (root.BindingContext is ChatMessage message)
            {
                await ShowMessageMenuAsync(message);
            }
        });
        root.Behaviors.Add(longPress);

        root.BindingContextChanged += async (_, _) =>
        {
            if (root.BindingContext is not ChatMessage message)
            {
                return;
            }

            usernameLabel.Text = message.Username;
            bodyLabel.Text = message.Body;
            replyLabel.Text = message.ReplyTo;
            replyLabel.IsVisible = message.ReplyTo is not null;
            ShowReadAvatars(message);

            avatarFallback.Text =
                string.IsNullOrWhiteSpace(message.Username)
                    ? "?"
                    : message.Username[..1].ToUpperInvariant();

            avatar.Source = null;

            image.Source = null;
            image.IsVisible = false;

            video.Source = null;
            video.IsVisible = false;

            var mediaSize = FitMediaSize(
                message.MediaWidth,
                message.MediaHeight,
                message.MediaKind);

            image.WidthRequest = mediaSize.Width;
            image.HeightRequest = mediaSize.Height;

            video.WidthRequest = mediaSize.Width;
            video.HeightRequest = mediaSize.Height;

            mediaLoading.IsVisible =
                message.MediaKind is not ChatMediaKind.None;

            var loads = new List<Task>();

            if (!string.IsNullOrWhiteSpace(message.AvatarUrl))
            {
                loads.Add(LoadAvatarAsync(message));
            }

            if (!string.IsNullOrWhiteSpace(
                    message.MediaSourceJson))
            {
                loads.Add(LoadMediaAsync(message));
            }

            await Task.WhenAll(loads);
        };

        return root;

        void ShowReadAvatars(ChatMessage message)
        {
            const int maximumAvatars = 5;

            readAvatars.Children.Clear();

            var userIds = message.ReadByUserIds?
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries) ?? [];

            readAvatars.IsVisible = userIds.Length > 0;

            foreach (var userId in userIds.Take(maximumAvatars))
            {
                var fallback = new Label
                {
                    Text = GetUserInitial(userId),
                    FontSize = 8,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                };
                var readerAvatar = new Image
                {
                    Aspect = Aspect.AspectFill,
                };
                var frame = new Border
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
                            fallback,
                            readerAvatar,
                        },
                    },
                };
                frame.SetDynamicResource(
                    VisualElement.BackgroundColorProperty,
                    "PrimaryContainer");
                SemanticProperties.SetDescription(
                    frame,
                    $"Read by {userId}");

                readAvatars.Children.Add(frame);
                _ = LoadReadAvatarAsync(
                    message,
                    userId,
                    readerAvatar);
            }

            if (userIds.Length > maximumAvatars)
            {
                readAvatars.Children.Add(new Label
                {
                    Text = $"+{userIds.Length - maximumAvatars}",
                    FontSize = 10,
                    VerticalTextAlignment = TextAlignment.Center,
                });
            }
        }

        static string GetUserInitial(string userId)
        {
            var localpart = userId.TrimStart('@');

            return string.IsNullOrEmpty(localpart)
                ? "?"
                : localpart[..1].ToUpperInvariant();
        }

        async Task LoadReadAvatarAsync(
            ChatMessage message,
            string userId,
            Image readerAvatar)
        {
            var room = _room;

            if (room is null)
            {
                return;
            }

            try
            {
                var avatarUrl =
                    await room.MemberAvatarUrl(userId);

                if (string.IsNullOrWhiteSpace(avatarUrl))
                {
                    return;
                }

                var bytes = await _matrixClient.GetThumbnailAsync(
                    avatarUrl,
                    36,
                    36,
                    isJson: false);

                if (root.BindingContext is ChatMessage current &&
                    current.Id == message.Id &&
                    current.ReadByUserIds == message.ReadByUserIds)
                {
                    readerAvatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load read avatar: {exception}");
            }
        }

        async Task LoadAvatarAsync(ChatMessage message)
        {
            try
            {
                var bytes = await _matrixClient.GetThumbnailAsync(
                    message.AvatarUrl!,
                    72,
                    72,
                    isJson: false);

                if (root.BindingContext is ChatMessage current &&
                    current.Id == message.Id)
                {
                    avatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load avatar: {exception}");
            }
        }

        async Task LoadMediaAsync(ChatMessage message)
        {
            try
            {
                if (message.MediaKind is ChatMediaKind.Image)
                {
                    var bytes =
                        await _matrixClient.GetThumbnailAsync(
                            message.MediaSourceJson!,
                            560,
                            440);

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
                    var path =
                        await _matrixClient.GetVideoFileAsync(
                            message.MediaSourceJson!,
                            message.Filename ?? "video",
                            message.MimeType ?? "video/mp4");

                    if (root.BindingContext is ChatMessage current &&
                        current.Id == message.Id)
                    {
                        video.Source =
                            PlaybackMediaSource.FromFile(path);

                        video.IsVisible = true;
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

#if WINDOWS
    private async Task SaveMediaAsync(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MediaSourceJson))
        {
            return;
        }

        var extension = System.IO.Path.GetExtension(message.Filename);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(
                    message.Filename ?? "attachment"),
            };
            picker.FileTypeChoices.Add("File", [extension]);
            var platformWindow = Application.Current?.Windows.FirstOrDefault()
                ?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
                ?? throw new InvalidOperationException("The application window is unavailable.");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(platformWindow));
            var destination = await picker.PickSaveFileAsync();
            if (destination is null)
            {
                return;
            }

            var data = await _matrixClient.GetMediaContentAsync(message.MediaSourceJson);
            await Windows.Storage.FileIO.WriteBytesAsync(destination, data);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not save file: {exception.Message}";
            Debug.WriteLine($"Could not save file: {exception}");
        }
    }
#endif

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
                    HorizontalTextAlignment =
                        TextAlignment.Center,
                },
                new Label
                {
                    Text = "Send the first message.",
                    Opacity = 0.7,
                    HorizontalTextAlignment =
                        TextAlignment.Center,
                },
            },
        };
    }

    private void DisposeTimeline()
    {
        DisposeTypingNotifications();

        if (_observableTimeline is null)
        {
            return;
        }

        _observableTimeline.CollectionChanged -=
            OnTimelineCollectionChanged;

        _observableTimeline.Dispose();
        _observableTimeline = null;

        _isLoadingMoreHistory = false;

        OnPropertyChanged(nameof(CanSend));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    private void SubscribeToTypingNotifications(Room room)
    {
        var roomId = room.Id();
        var ownUserId = room.OwnUserId();

        _typingListener = new TypingListener(
            userIds => _ = UpdateTypingIndicatorAsync(
                room,
                roomId,
                ownUserId,
                userIds));
        _typingHandle =
            room.SubscribeToTypingNotifications(_typingListener);

        if (!string.IsNullOrWhiteSpace(MessageText))
        {
            _ = SendTypingNoticeAsync(room, true);
        }
    }

    private async Task UpdateTypingIndicatorAsync(
        Room room,
        string roomId,
        string ownUserId,
        string[] userIds)
    {
        var version = Interlocked.Increment(
            ref _typingUpdateVersion);
        var names = new List<string>();

        foreach (var userId in userIds
            .Where(userId => userId != ownUserId)
            .Distinct())
        {
            try
            {
                names.Add(
                    await room.MemberDisplayName(userId) ??
                    userId);
            }
            catch
            {
                names.Add(userId);
            }
        }

        var text = FormatTypingText(names);

        Dispatcher.Dispatch(() =>
        {
            if (_disposed ||
                version != _typingUpdateVersion ||
                _room?.Id() != roomId)
            {
                return;
            }

            _typingLabel.Text = text;
            _typingLabel.IsVisible =
                !string.IsNullOrEmpty(text);
        });
    }

    private static string FormatTypingText(
        IReadOnlyList<string> names)
    {
        return names.Count switch
        {
            0 => string.Empty,
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]} and {names[1]} are typing…",
            _ => $"{names[0]}, {names[1]}, and " +
                $"{names.Count - 2} " +
                $"{(names.Count == 3 ? "other" : "others")} " +
                "are typing…",
        };
    }

    private static async Task SendTypingNoticeAsync(
        Room room,
        bool isTyping)
    {
        try
        {
            await room.TypingNotice(isTyping);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not update typing state: {exception}");
        }
    }

    private void DisposeTypingNotifications()
    {
        Interlocked.Increment(ref _typingUpdateVersion);

        if (_room is { } room)
        {
            _ = SendTypingNoticeAsync(room, false);
        }

        try
        {
            _typingHandle?.Cancel();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not stop typing updates: {exception}");
        }

        _typingHandle?.Dispose();
        _typingHandle = null;
        _typingListener = null;

        _typingLabel.Text = string.Empty;
        _typingLabel.IsVisible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _timelineView.Scrolled -=
            OnTimelineScrolled;

        _timelineView.SizeChanged -=
            OnTimelineViewSizeChanged;

        _messageEntry.Completed -=
            OnMessageEntryCompleted;

        DisposeTimeline();

        _loadingOverlay.IsVisible = false;
        _loadingOverlay.InputTransparent = true;

        Messages.Clear();

        _room = null;

        GC.SuppressFinalize(this);
    }

    private sealed class TypingListener(
        Action<string[]> callback) :
        TypingNotificationsListener
    {
        public void Call(string[] typingUserIds)
        {
            callback(typingUserIds);
        }
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
    ulong? MediaHeight = null,
    string? ReadByUserIds = null,
    string? EventId = null,
    string? ReplyTo = null);

public enum ChatMediaKind
{
    None,
    Image,
    Video,
    File,
}
