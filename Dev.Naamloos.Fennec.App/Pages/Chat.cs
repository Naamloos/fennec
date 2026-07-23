using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Dev.Naamloos.Fennec.App.Components;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView, IDisposable
{
    private readonly ChatTypingController _typingController;

    // State
    private ObservableTimeline? _observableTimeline;
    private Room? _room;

    private bool _isLoading;
    private bool _isSending;
    private bool _isLoadingMoreHistory;
    private bool _disposed;

    // Backing fields
    private string _messageText = string.Empty;
    private string _errorMessage = string.Empty;
    private ChatMessage? _replyTo;

    // Binding properties
    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial string TypingText { get; set; } = string.Empty;

    [BindableProperty]
    public partial int TimelineResetRequest { get; set; }

    [BindableProperty]
    public partial int TimelineScrollRequest { get; set; }

    [BindableProperty]
    public partial int TimelineScrollAfterLayoutRequest { get; set; }

    [BindableProperty]
    public partial int ComposerFocusRequest { get; set; }

    [BindableProperty]
    public partial bool TimelineIsNearBottom { get; set; } = true;

    [BindableProperty(PropertyChangedMethodName = nameof(OnSelectedRoomChanged))]
    public partial Room? SelectedRoom { get; set; }

    [BindableProperty]
    public partial string RoomLoadError { get; set; } = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public Room? CurrentRoom => _room;
    public bool HasTypingText =>
        !string.IsNullOrEmpty(TypingText);

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

            _typingController?.SetTyping(!string.IsNullOrWhiteSpace(value));
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

    public ChatMessage? ReplyTarget => _replyTo;

    public Chat()
    {
        BindingContext = this;

        _typingController = new ChatTypingController(
            Dispatcher,
            () => _room,
            SetTypingText);

        build();
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
        RoomLoadError = string.Empty;

        try
        {
            DisposeTimeline();

            _room = room;
            OnPropertyChanged(nameof(CurrentRoom));

            /*
             * A newly opened room should start at its newest message.
             */
            _isLoadingMoreHistory = false;
            TimelineResetRequest++;
            Messages.Clear();

            cancellationToken.ThrowIfCancellationRequested();

            Debug.WriteLine(
                "Creating ObservableTimeline...");

            var observableTimeline =
                await (MatrixClient ??
                    throw new InvalidOperationException(
                        "Matrix client is required."))
                    .GetObservableTimelineAsync(room);

            cancellationToken.ThrowIfCancellationRequested();

            if (_disposed)
            {
                observableTimeline.Dispose();
                return;
            }

            _observableTimeline = observableTimeline;

            _observableTimeline.CollectionChanged +=
                OnTimelineCollectionChanged;

            _typingController.Start(
                room,
                !string.IsNullOrWhiteSpace(MessageText));

            Debug.WriteLine(
                "ObservableTimeline created.");

            SynchronizeMessages();

            Debug.WriteLine(
                $"Timeline loaded with {_observableTimeline.Count} items.");

            TimelineScrollAfterLayoutRequest++;
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
            RoomLoadError = exception.Message;

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

            ComposerFocusRequest++;

            /*
             * Sending a message should always return the sender to the
             * newest part of the timeline.
             */
            TimelineScrollRequest++;
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

    [RelayCommand]
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
            var url = await (MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required."))
                .UploadMediaAsync(mimeType, data.ToArray());
            using var source = MediaSource.FromUrl(url);
            var content = _observableTimeline.Timeline.CreateMessageContent(
                CreateAttachmentMessage(
                    file.FileName,
                    mimeType,
                    (ulong)data.Length,
                    source))
                ?? throw new InvalidOperationException("Could not create attachment content.");
            await _observableTimeline.Timeline.Send(content);
            TimelineScrollRequest++;
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

    [RelayCommand]
    private void Reply(ChatMessage? message) => SetReplyTarget(message);

    [RelayCommand]
    private Task OpenMessageMenuAsync(ChatMessage? message) =>
        message is null ? Task.CompletedTask : ShowMessageMenuAsync(message);

    [RelayCommand]
    private Task SaveMessageMediaAsync(ChatMessage? message) =>
        message is null ? Task.CompletedTask : SaveMediaAsync(message);

    private void SetReplyTarget(ChatMessage? message)
    {
        _replyTo = message;
        OnPropertyChanged(nameof(ReplyTarget));
        ComposerFocusRequest++;
    }

    private async Task ShowMessageMenuAsync(ChatMessage message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
        {
            return;
        }

        var actions = new List<string> { "Copy text" };
        if (message.EventId is not null)
        {
            actions.Insert(0, "Reply");
        }
        if (!string.IsNullOrWhiteSpace(message.MediaSourceJson))
        {
            actions.Add("Save file");
        }
        var action = await page.DisplayActionSheetAsync(
            "Message", "Cancel", null, actions.ToArray());
        if (action == "Reply")
        {
            SetReplyTarget(message);
        }
        else if (action == "Copy text")
        {
            await Clipboard.Default.SetTextAsync(message.Body);
        }
        else if (action == "Save file")
        {
            await SaveMediaAsync(message);
        }
    }

    private void build()
    {
        Content = new Grid
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Unloaded),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(UnloadCommand)),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            Children =
            {
                new ChatTimeline()
                .Bind(
                    ChatTimeline.MessagesProperty,
                    nameof(Messages),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.HistoryCommandProperty,
                    nameof(LoadMoreHistoryCommand),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.MatrixClientProperty,
                    nameof(MatrixClient),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.RoomProperty,
                    nameof(CurrentRoom),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.ReplyCommandProperty,
                    nameof(ReplyCommand),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.MessageMenuCommandProperty,
                    nameof(OpenMessageMenuCommand),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.SaveMediaCommandProperty,
                    nameof(SaveMessageMediaCommand),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.ResetRequestProperty,
                    nameof(TimelineResetRequest),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.ScrollRequestProperty,
                    nameof(TimelineScrollRequest),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.ScrollAfterLayoutRequestProperty,
                    nameof(TimelineScrollAfterLayoutRequest),
                    source: BindingContext)
                .Bind(
                    ChatTimeline.IsNearBottomProperty,
                    nameof(TimelineIsNearBottom),
                    BindingMode.TwoWay,
                    source: BindingContext)
                .Row(0),
                new Label
                {
                    Margin = new Thickness(12, 2),
                    FontSize = 12,
                    Opacity = .7,
                    LineBreakMode = LineBreakMode.TailTruncation,
                }
                .Bind(Label.TextProperty, nameof(TypingText))
                .Bind(
                    IsVisibleProperty,
                    nameof(TypingText),
                    converter: new IsStringNotNullOrEmptyConverter())
                .Row(1),
                new ChatComposer()
                    .Bind(
                        ChatComposer.TextProperty,
                        nameof(MessageText),
                        BindingMode.TwoWay,
                        source: BindingContext)
                    .Bind(
                        ChatComposer.ReplyToProperty,
                        nameof(ReplyTarget),
                        source: BindingContext)
                    .Bind(
                        ChatComposer.ErrorMessageProperty,
                        nameof(ErrorMessage),
                        source: BindingContext)
                    .Bind(
                        ChatComposer.HasErrorProperty,
                        nameof(HasError),
                        source: BindingContext)
                    .Bind(
                        ChatComposer.SendCommandProperty,
                        nameof(SendMessageCommand),
                        source: BindingContext)
                    .Bind(
                        ChatComposer.AttachCommandProperty,
                        nameof(AttachFileCommand),
                        source: BindingContext)
                    .Bind(
                        ChatComposer.FocusRequestProperty,
                        nameof(ComposerFocusRequest),
                        source: BindingContext)
                    .Row(2),
                new Grid
                {
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
                }
                .Bind(IsVisibleProperty, nameof(IsLoading))
                .Bind(
                    InputTransparentProperty,
                    nameof(IsLoading),
                    converter: new InvertedBoolConverter())
                .RowSpan(3),
            },
        };
    }

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        if (newValue is Room room)
        {
            _ = ((Chat)bindable).SetRoomAsync(room);
        }
    }

    [RelayCommand]
    private void Unload() => Dispose();

    private async Task SaveMediaAsync(ChatMessage message)
    {
        // TODO implement mac catalyst file saving
#if MACCATALYST
        return;
#endif

#if WINDOWS || ANDROID
        if (string.IsNullOrWhiteSpace(message.MediaSourceJson))
        {
            return;
        }

#if WINDOWS
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

            var data = await (MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required."))
                .GetMediaContentAsync(message.MediaSourceJson);
            await Windows.Storage.FileIO.WriteBytesAsync(destination, data);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not save file: {exception.Message}";
            Debug.WriteLine($"Could not save file: {exception}");
        }
#elif ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                throw new PlatformNotSupportedException(
                    "Saving files requires Android 10 or newer.");
            }

            var values = new Android.Content.ContentValues();
            values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName,
                message.Filename ?? "attachment");
            values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType,
                message.MimeType ?? "application/octet-stream");
            values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath,
                Android.OS.Environment.DirectoryDownloads);
            var resolver = Android.App.Application.Context!.ContentResolver!;
            var destination = resolver.Insert(
                Android.Provider.MediaStore.Downloads.ExternalContentUri,
                values) ?? throw new IOException("Could not create the download.");
            await using var output = resolver.OpenOutputStream(destination)
                ?? throw new IOException("Could not open the download.");
            var data = await (MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required."))
                .GetMediaContentAsync(message.MediaSourceJson);
            await output.WriteAsync(data);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not save file: {exception.Message}";
            Debug.WriteLine($"Could not save file: {exception}");
        }
#endif
#endif
    }

    private void DisposeTimeline()
    {
        _typingController.Stop();

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DisposeTimeline();

        Messages.Clear();

        _room = null;
        OnPropertyChanged(nameof(CurrentRoom));

        GC.SuppressFinalize(this);
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
        var shouldFollowBottom = TimelineIsNearBottom;

        SynchronizeMessages();

        if (shouldFollowBottom)
        {
            TimelineScrollRequest++;
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

    [RelayCommand]
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

    private void SetTypingText(string text)
    {
        TypingText = text;
    }

}

public sealed record ChatMessageMeasurement(ChatMessage Message, double Height);

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
