using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Components;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed partial class Chat : ContentView, IDisposable
{
    private readonly ChatTypingController _typingController;

    private ObservableTimeline? _observableTimeline;
    private Room? _room;

    private CancellationTokenSource? _roomLoadCancellation;

    private bool _isLoading;
    private bool _isSending;
    private bool _isLoadingMoreHistory;
    private bool _reconcileQueued;
    private bool _disposed;

    private string _messageText = string.Empty;
    private string _errorMessage = string.Empty;
    private ChatMessage? _replyTo;

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial string TypingText { get; set; } =
        string.Empty;

    [BindableProperty]
    public partial int TimelineResetRequest { get; set; }

    [BindableProperty]
    public partial int TimelineScrollRequest { get; set; }

    [BindableProperty]
    public partial int TimelineScrollAfterLayoutRequest { get; set; }

    [BindableProperty]
    public partial bool TimelineIsNearBottom { get; set; } = true;

    [BindableProperty(
        PropertyChangedMethodName = nameof(OnSelectedRoomChanged))]
    public partial Room? SelectedRoom { get; set; }

    [BindableProperty]
    public partial string RoomLoadError { get; set; } =
        string.Empty;

    public ObservableCollection<ChatMessage> Messages
    {
        get;
    } = [];

    public Room? CurrentRoom =>
        _room;

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

            SendMessageCommand
                .NotifyCanExecuteChanged();

            _typingController.SetTyping(
                !string.IsNullOrWhiteSpace(value));
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

            SendMessageCommand
                .NotifyCanExecuteChanged();
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

            SendMessageCommand
                .NotifyCanExecuteChanged();
        }
    }

    public bool CanSend =>
        !IsLoading &&
        !IsSending &&
        _observableTimeline is not null &&
        !string.IsNullOrWhiteSpace(
            MessageText);

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
        !string.IsNullOrWhiteSpace(
            ErrorMessage);

    public ChatMessage? ReplyTarget =>
        _replyTo;

    public Chat()
    {
        _typingController =
            new ChatTypingController(
                Dispatcher,
                () => _room,
                text => TypingText = text);

        Build();
    }

    public async Task SetRoomAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (_room?.Id() == room.Id() &&
            _observableTimeline is not null)
        {
            return;
        }

        _roomLoadCancellation?.Cancel();
        _roomLoadCancellation?.Dispose();

        _roomLoadCancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        var effectiveToken =
            _roomLoadCancellation.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        RoomLoadError = string.Empty;

        try
        {
            DisposeTimeline();

            _room = room;
            OnPropertyChanged(
                nameof(CurrentRoom));

            Messages.Clear();
            TimelineIsNearBottom = true;
            TimelineResetRequest++;

            var matrixClient =
                MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required.");

            var timeline =
                await matrixClient
                    .GetObservableTimelineAsync(
                        room);

            effectiveToken
                .ThrowIfCancellationRequested();

            if (_disposed)
            {
                timeline.Dispose();
                return;
            }

            _observableTimeline = timeline;

            _observableTimeline.CollectionChanged +=
                OnTimelineCollectionChanged;

            ReconcileMessages();

            _typingController.Start(
                room,
                !string.IsNullOrWhiteSpace(
                    MessageText));

            TimelineScrollAfterLayoutRequest++;
        }
        catch (OperationCanceledException)
        {
            // A newer room selection superseded this load.
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            RoomLoadError = exception.Message;

            Debug.WriteLine(
                $"Could not open room {room.Id()}: " +
                exception);
        }
        finally
        {
            IsLoading = false;

            OnPropertyChanged(nameof(CanSend));

            SendMessageCommand
                .NotifyCanExecuteChanged();
        }
    }

    private void OnTimelineCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_disposed ||
            !ReferenceEquals(
                sender,
                _observableTimeline))
        {
            return;
        }

        QueueMessageReconciliation();
    }

    private void QueueMessageReconciliation()
    {
        if (_reconcileQueued)
        {
            return;
        }

        _reconcileQueued = true;

        /*
         * Reset currently arrives as Clear followed by many Add events.
         * Coalesce the complete burst into one stable-ID reconciliation.
         */
        Dispatcher.Dispatch(() =>
        {
            _reconcileQueued = false;

            if (!_disposed)
            {
                ReconcileMessages();
            }
        });
    }

    private void ReconcileMessages()
    {
        if (_observableTimeline is null)
        {
            Messages.Clear();
            return;
        }

        var desired =
            _observableTimeline
                .Select(TryCreateChatMessage)
                .Where(message =>
                    message is not null)
                .Cast<ChatMessage>()
                .ToArray();

        for (var desiredIndex = 0;
             desiredIndex < desired.Length;
             desiredIndex++)
        {
            var desiredMessage =
                desired[desiredIndex];

            if (desiredIndex < Messages.Count &&
                Messages[desiredIndex].Id ==
                desiredMessage.Id)
            {
                if (Messages[desiredIndex] !=
                    desiredMessage)
                {

                    Messages[desiredIndex].UpdateFrom(desiredMessage);
                }

                continue;
            }

            var existingIndex =
                FindMessageIndex(
                    desiredMessage.Id,
                    desiredIndex + 1);

            if (existingIndex >= 0)
            {
                Messages.Move(
                    existingIndex,
                    desiredIndex);

                if (Messages[desiredIndex] !=
                    desiredMessage)
                {
                    Messages[desiredIndex].UpdateFrom(desiredMessage);
                }

                continue;
            }

            Messages.Insert(
                desiredIndex,
                desiredMessage);
        }

        while (Messages.Count >
               desired.Length)
        {
            Messages.RemoveAt(
                Messages.Count - 1);
        }
    }

    private int FindMessageIndex(
        string id,
        int startIndex)
    {
        for (var index =
                 Math.Max(0, startIndex);
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

    [RelayCommand(
        CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (_observableTimeline is null)
        {
            return;
        }

        var text =
            MessageText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            var content =
                CreateTextMessageContent(
                    _observableTimeline.Timeline,
                    text);

            if (_replyTo?.EventId is
                { } eventId)
            {
                await _observableTimeline
                    .Timeline
                    .SendReply(
                        content,
                        eventId);
            }
            else
            {
                await _observableTimeline
                    .Timeline
                    .Send(content);
            }

            MessageText = string.Empty;
            SetReplyTarget(null);

            TimelineScrollRequest++;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Could not send message: " +
                exception);
        }
        finally
        {
            IsSending = false;
        }
    }

    private static
        RoomMessageEventContentWithoutRelation
        CreateTextMessageContent(
            Timeline timeline,
            string text)
    {
        return timeline.CreateMessageContent(
            new MessageType.Text(
                new TextMessageContent(
                    text,
                    null)))
            ?? throw new InvalidOperationException(
                "Could not create message content.");
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        if (_observableTimeline is null ||
            IsSending)
        {
            return;
        }

        try
        {
            var file =
                await FilePicker.Default
                    .PickAsync();

            if (file is null ||
                _observableTimeline is null)
            {
                return;
            }

            await using var stream =
                await file.OpenReadAsync();

            using var data =
                new MemoryStream();

            await stream.CopyToAsync(data);

            IsSending = true;
            ErrorMessage = string.Empty;

            var mimeType =
                string.IsNullOrWhiteSpace(
                    file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType;

            var matrixClient =
                MatrixClient ??
                throw new InvalidOperationException(
                    "Matrix client is required.");

            var url =
                await matrixClient.UploadMediaAsync(
                    mimeType,
                    data.ToArray());

            using var source =
                MediaSource.FromUrl(url);

            var content =
                _observableTimeline.Timeline
                    .CreateMessageContent(
                        CreateAttachmentMessage(
                            file.FileName,
                            mimeType,
                            (ulong)data.Length,
                            source))
                ?? throw new InvalidOperationException(
                    "Could not create attachment content.");

            await _observableTimeline
                .Timeline
                .Send(content);

            TimelineScrollRequest++;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Could not upload attachment: " +
                exception);
        }
        finally
        {
            IsSending = false;
        }
    }

    private static MessageType
        CreateAttachmentMessage(
            string filename,
            string mimeType,
            ulong size,
            MediaSource source)
    {
        if (mimeType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            return new MessageType.Image(
                new ImageMessageContent(
                    filename,
                    null,
                    null,
                    source,
                    new ImageInfo(
                        null,
                        null,
                        mimeType,
                        size,
                        null,
                        null,
                        null,
                        null)));
        }

        if (mimeType.StartsWith(
                "video/",
                StringComparison.OrdinalIgnoreCase))
        {
            return new MessageType.Video(
                new VideoMessageContent(
                    filename,
                    null,
                    null,
                    source,
                    new VideoInfo(
                        null,
                        null,
                        null,
                        mimeType,
                        size,
                        null,
                        null,
                        null)));
        }

        return new MessageType.File(
            new FileMessageContent(
                filename,
                null,
                null,
                source,
                new uniffi.matrix_sdk_ffi
                    .FileInfo(
                        mimeType,
                        size,
                        null,
                        null)));
    }

    [RelayCommand]
    private void Reply(
        ChatMessage? message)
    {
        SetReplyTarget(message);
    }

    [RelayCommand]
    private async Task OpenMessageMenuAsync(
        ChatMessage? message)
    {
        if (message is null)
        {
            return;
        }

        var page =
            Application.Current?
                .Windows
                .FirstOrDefault()?
                .Page;

        if (page is null)
        {
            return;
        }

        var actions =
            new List<string>
            {
                "Copy text",
            };

        if (message.EventId is not null)
        {
            actions.Insert(0, "Reply");
        }

        if (!string.IsNullOrWhiteSpace(
                message.MediaSourceJson))
        {
            actions.Add("Save file");
        }

        var action =
            await page.DisplayActionSheetAsync(
                "Message",
                "Cancel",
                null,
                actions.ToArray());

        switch (action)
        {
            case "Reply":
                SetReplyTarget(message);
                break;

            case "Copy text":
                await Clipboard.Default
                    .SetTextAsync(
                        message.Body);
                break;

            case "Save file":
                await SaveMediaAsync(message);
                break;
        }
    }

    [RelayCommand]
    private Task SaveMessageMediaAsync(
        ChatMessage? message)
    {
        return message is null
            ? Task.CompletedTask
            : SaveMediaAsync(message);
    }

    private void SetReplyTarget(
        ChatMessage? message)
    {
        _replyTo = message;
        OnPropertyChanged(
            nameof(ReplyTarget));
    }

    private void Build()
    {
        Content = new Grid
        {
            SafeAreaEdges =
                SafeAreaEdges.All,

            RowDefinitions =
            {
                new RowDefinition(
                    GridLength.Star),

                new RowDefinition(
                    GridLength.Auto),

                new RowDefinition(
                    GridLength.Auto),
            },

            Behaviors =
            {
                new EventToCommandBehavior
                {
                    EventName =
                        nameof(Unloaded),
                }
                .Bind(
                    EventToCommandBehavior
                        .CommandProperty,
                    nameof(UnloadCommand),
                    source: this),
            },

            Children =
            {
                new ChatTimeline()
                    .Bind(
                        ChatTimeline
                            .MessagesProperty,
                        nameof(Messages),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .HistoryCommandProperty,
                        nameof(
                            LoadMoreHistoryCommand),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .MatrixClientProperty,
                        nameof(MatrixClient),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .RoomProperty,
                        nameof(CurrentRoom),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .ReplyCommandProperty,
                        nameof(ReplyCommand),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .MessageMenuCommandProperty,
                        nameof(OpenMessageMenuCommand),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .SaveMediaCommandProperty,
                        nameof(SaveMessageMediaCommand),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .ResetRequestProperty,
                        nameof(
                            TimelineResetRequest),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .ScrollRequestProperty,
                        nameof(
                            TimelineScrollRequest),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .ScrollAfterLayoutRequestProperty,
                        nameof(
                            TimelineScrollAfterLayoutRequest),
                        source: this)
                    .Bind(
                        ChatTimeline
                            .IsNearBottomProperty,
                        nameof(
                            TimelineIsNearBottom),
                        BindingMode.TwoWay,
                        source: this)
                    .Row(0),

                new Label
                {
                    Margin =
                        new Thickness(12, 2),

                    FontSize = 12,
                    Opacity = .7,

                    LineBreakMode =
                        LineBreakMode
                            .TailTruncation,
                }
                .Bind(
                    Label.TextProperty,
                    nameof(TypingText),
                    source: this)
                .Bind(
                    IsVisibleProperty,
                    nameof(TypingText),
                    converter:
                        new IsStringNotNullOrEmptyConverter(),
                    source: this)
                .Row(1),

                new ChatComposer()
                    .Bind(
                        ChatComposer.TextProperty,
                        nameof(MessageText),
                        BindingMode.TwoWay,
                        source: this)
                    .Bind(
                        ChatComposer.ReplyToProperty,
                        nameof(ReplyTarget),
                        source: this)
                    .Bind(
                        ChatComposer.ErrorMessageProperty,
                        nameof(ErrorMessage),
                        source: this)
                    .Bind(
                        ChatComposer.HasErrorProperty,
                        nameof(HasError),
                        source: this)
                    .Bind(
                        ChatComposer.SendCommandProperty,
                        nameof(SendMessageCommand),
                        source: this)
                    .Bind(
                        ChatComposer.AttachCommandProperty,
                        nameof(AttachFileCommand),
                        source: this)
                    .Row(2),

                new Grid
                {
                    BackgroundColor =
                        Color.FromArgb(
                            "#66000000"),

                    Children =
                    {
                        new ActivityIndicator
                        {
                            IsRunning = true,
                            HorizontalOptions =
                                LayoutOptions.Center,
                            VerticalOptions =
                                LayoutOptions.Center,
                        },
                    },
                }
                .Bind(
                    IsVisibleProperty,
                    nameof(IsLoading),
                    source: this)
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
            _ = ((Chat)bindable)
                .SetRoomAsync(room);
        }
    }

    [RelayCommand]
    private void Unload()
    {
        Dispose();
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
            await _observableTimeline
                .LoadMoreHistoryAsync();
        }
        catch (ObjectDisposedException)
        {
            // The active room changed.
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;

            Debug.WriteLine(
                $"Could not load history: " +
                exception);
        }
        finally
        {
            _isLoadingMoreHistory = false;
        }
    }

    private async Task SaveMediaAsync(
        ChatMessage message)
    {
#if MACCATALYST
        await Task.CompletedTask;
        return;
#endif

#if WINDOWS || ANDROID
        if (string.IsNullOrWhiteSpace(
                message.MediaSourceJson))
        {
            return;
        }

        var matrixClient =
            MatrixClient ??
            throw new InvalidOperationException(
                "Matrix client is required.");

#if WINDOWS
        var extension =
            Path.GetExtension(
                message.Filename);

        if (string.IsNullOrWhiteSpace(
                extension))
        {
            extension = ".bin";
        }

        var picker =
            new Windows.Storage.Pickers
                .FileSavePicker
            {
                SuggestedFileName =
                    Path.GetFileNameWithoutExtension(
                        message.Filename ??
                        "attachment"),
            };

        picker.FileTypeChoices.Add(
            "File",
            [extension]);

        var platformWindow =
            Application.Current?
                .Windows
                .FirstOrDefault()?
                .Handler?
                .PlatformView as
                Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException(
                "Application window unavailable.");

        WinRT.Interop
            .InitializeWithWindow
            .Initialize(
                picker,
                WinRT.Interop.WindowNative
                    .GetWindowHandle(
                        platformWindow));

        var destination =
            await picker.PickSaveFileAsync();

        if (destination is null)
        {
            return;
        }

        var data =
            await matrixClient
                .GetMediaContentAsync(
                    message.MediaSourceJson);

        await Windows.Storage.FileIO
            .WriteBytesAsync(
                destination,
                data);
#elif ANDROID
        if (!OperatingSystem
                .IsAndroidVersionAtLeast(29))
        {
            throw new PlatformNotSupportedException(
                "Saving files requires Android 10 or newer.");
        }

        var values =
            new Android.Content
                .ContentValues();

        values.Put(
            Android.Provider.MediaStore
                .IMediaColumns.DisplayName,
            message.Filename ??
            "attachment");

        values.Put(
            Android.Provider.MediaStore
                .IMediaColumns.MimeType,
            message.MimeType ??
            "application/octet-stream");

        values.Put(
            Android.Provider.MediaStore
                .IMediaColumns.RelativePath,
            Android.OS.Environment
                .DirectoryDownloads);

        var resolver =
            Android.App.Application
                .Context!
                .ContentResolver!;

        var destination =
            resolver.Insert(
                Android.Provider.MediaStore
                    .Downloads
                    .ExternalContentUri,
                values)
            ?? throw new IOException(
                "Could not create download.");

        await using var output =
            resolver.OpenOutputStream(
                destination)
            ?? throw new IOException(
                "Could not open download.");

        var data =
            await matrixClient
                .GetMediaContentAsync(
                    message.MediaSourceJson);

        await output.WriteAsync(data);
#endif
#else
        await Task.CompletedTask;
#endif
    }

    private static ChatMessage?
        TryCreateChatMessage(
            TimelineItem timelineItem)
    {
        var eventItem =
            timelineItem.AsEvent();

        if (eventItem?.Content is not
            TimelineItemContent.MsgLike msgLike)
        {
            return null;
        }

        var username =
            eventItem.SenderProfile is
                ProfileDetails.Ready ready &&
            !string.IsNullOrWhiteSpace(
                ready.DisplayName)
                ? ready.DisplayName
                : eventItem.Sender;

        var avatarUrl =
            eventItem.SenderProfile is
                ProfileDetails.Ready profile
                ? profile.AvatarUrl
                : null;

        var id =
            timelineItem.UniqueId().Id;

        var eventId =
            (eventItem.EventOrTransactionId as
                EventOrTransactionId.EventId)
            ?.EventIdValue;

        var readByUserIds =
            string.Join(
                '\n',
                eventItem.ReadReceipts.Keys
                    .Where(userId =>
                        userId !=
                        eventItem.Sender)
                    .Order(
                        StringComparer.Ordinal));

        var replyTo =
            FormatReplyPreview(
                msgLike.Content.InReplyTo);

        return msgLike.Content.Kind switch
        {
            MsgLikeKind.Message message =>
                CreateMessage(
                    id,
                    username,
                    avatarUrl,
                    eventId,
                    readByUserIds,
                    replyTo,
                    eventItem.IsOwn,
                    message),

            MsgLikeKind.Sticker sticker =>
                new ChatMessage(
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

    private static ChatMessage CreateMessage(
        string id,
        string username,
        string? avatarUrl,
        string? eventId,
        string readByUserIds,
        string? replyTo,
        bool isOwn,
        MsgLikeKind.Message message)
    {
        return message.Content.MsgType switch
        {
            MessageType.Image image =>
                new ChatMessage(
                    id,
                    username,
                    image.Content.Caption ??
                    image.Content.Filename,
                    isOwn,
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

            MessageType.Video video =>
                new ChatMessage(
                    id,
                    username,
                    video.Content.Caption ??
                    video.Content.Filename,
                    isOwn,
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

            MessageType.File file =>
                new ChatMessage(
                    id,
                    username,
                    file.Content.Caption ??
                    file.Content.Filename,
                    isOwn,
                    avatarUrl,
                    ChatMediaKind.File,
                    file.Content.Source.ToJson(),
                    file.Content.Filename,
                    file.Content.Info?.Mimetype,
                    readByUserIds:
                        readByUserIds,
                    eventId:
                        eventId,
                    replyTo:
                        replyTo),

            _ =>
                new ChatMessage(
                    id,
                    username,
                    message.Content.Body,
                    isOwn,
                    avatarUrl,
                    readByUserIds: readByUserIds,
                    eventId: eventId,
                    replyTo: replyTo),
        };
    }

    private static string? FormatReplyPreview(
        InReplyToDetails? reply)
    {
        if (reply is null)
        {
            return null;
        }

        using var details =
            reply.Event();

        var body = details is
        EmbeddedEventDetails.Ready
        {
            Content:
                TimelineItemContent.MsgLike
            {
                Content.Kind:
                        MsgLikeKind.Message message,
            },
        }
            ? message.Content.Body
            : null;

        return string.IsNullOrWhiteSpace(body)
            ? "Replying to a message"
            : $"Replying to: " +
              body.ReplaceLineEndings(" ");
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

        Messages.Clear();

        _isLoadingMoreHistory = false;

        OnPropertyChanged(nameof(CanSend));

        SendMessageCommand
            .NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _roomLoadCancellation?.Cancel();
        _roomLoadCancellation?.Dispose();
        _roomLoadCancellation = null;

        DisposeTimeline();

        _typingController.Dispose();

        _room = null;
        OnPropertyChanged(nameof(CurrentRoom));

        GC.SuppressFinalize(this);
    }
}

public sealed partial class ChatMessage : ObservableObject
{
    public ChatMessage(
        string id,
        string username,
        string body,
        bool isOwn,
        string? avatarUrl = null,
        ChatMediaKind mediaKind = ChatMediaKind.None,
        string? mediaSourceJson = null,
        string? filename = null,
        string? mimeType = null,
        ulong? mediaWidth = null,
        ulong? mediaHeight = null,
        string? readByUserIds = null,
        string? eventId = null,
        string? replyTo = null)
    {
        Id = id;
        Username = username;
        Body = body;
        IsOwn = isOwn;
        AvatarUrl = avatarUrl;
        MediaKind = mediaKind;
        MediaSourceJson = mediaSourceJson;
        Filename = filename;
        MimeType = mimeType;
        MediaWidth = mediaWidth;
        MediaHeight = mediaHeight;
        ReadByUserIds = readByUserIds;
        EventId = eventId;
        ReplyTo = replyTo;
    }

    public string Id { get; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Body { get; set; }

    [ObservableProperty]
    public partial bool IsOwn { get; set; }

    [ObservableProperty]
    public partial string? AvatarUrl { get; set; }

    [ObservableProperty]
    public partial ChatMediaKind MediaKind { get; set; }

    [ObservableProperty]
    public partial string? MediaSourceJson { get; set; }

    [ObservableProperty]
    public partial string? Filename { get; set; }

    [ObservableProperty]
    public partial string? MimeType { get; set; }

    [ObservableProperty]
    public partial ulong? MediaWidth { get; set; }

    [ObservableProperty]
    public partial ulong? MediaHeight { get; set; }

    [ObservableProperty]
    public partial string? ReadByUserIds { get; set; }

    [ObservableProperty]
    public partial string? EventId { get; set; }

    [ObservableProperty]
    public partial string? ReplyTo { get; set; }

    public void UpdateFrom(ChatMessage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Id != Id)
        {
            throw new InvalidOperationException(
                "Cannot update a message from a different timeline item.");
        }

        Username = source.Username;
        Body = source.Body;
        IsOwn = source.IsOwn;
        AvatarUrl = source.AvatarUrl;
        MediaKind = source.MediaKind;
        MediaSourceJson = source.MediaSourceJson;
        Filename = source.Filename;
        MimeType = source.MimeType;
        MediaWidth = source.MediaWidth;
        MediaHeight = source.MediaHeight;
        ReadByUserIds = source.ReadByUserIds;
        EventId = source.EventId;
        ReplyTo = source.ReplyTo;
    }
}

public enum ChatMediaKind
{
    None,
    Image,
    Video,
    File,
}

