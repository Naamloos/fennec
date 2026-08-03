using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ChatSession : ObservableModel, IAsyncDisposable
{
    private readonly ManagedMatrixClient _client;
    private readonly Room _room;
    private readonly ObservableTimeline _timeline;
    private readonly RoomTypingController _typing;
    private readonly RoomInfoListener _roomInfoListener;
    private readonly TaskHandle _roomInfoSubscription;
    private readonly SemaphoreSlim _membersLoadGate = new(1, 1);
    private readonly SemaphoreSlim _customEmotesLoadGate = new(1, 1);
    private readonly SynchronizationContext? _synchronizationContext;
    private string? _lastReadEventId;
    private bool _disposed;
    private bool _isLoading;
    private bool _isSending;
    private string _errorMessage = string.Empty;
    private string _typingText = string.Empty;
    private string _draftText = string.Empty;
    private ChatTimelineItem? _replyTarget;
    private ChatTimelineItem? _editTarget;
    private string? _roomAvatarUrl;
    private bool _canInvalidateAvatars;
    private bool _customEmotesLoaded;
    private bool _isServerNoticeRoom;

    private ChatSession(ManagedMatrixClient client, Room room, ObservableTimeline timeline)
    {
        _client = client;
        _room = room;
        _timeline = timeline;
        _synchronizationContext = SynchronizationContext.Current;
        _typing = new RoomTypingController(room);
        _roomInfoListener = new MemberListUpdateListener(this);
        _roomInfoSubscription = room.SubscribeToRoomInfoUpdates(_roomInfoListener);
        _roomAvatarUrl = room.AvatarUrl();
        _typing.PropertyChanged += OnTypingChanged;
        ((INotifyPropertyChanged)_timeline).PropertyChanged += OnTimelinePropertyChanged;
        _timeline.CollectionChanged += OnTimelineChanged;
        _typing.Start();
        ResetItems();
        UpdateMessageGroups();
        _canInvalidateAvatars = true;
    }

    public ObservableRangeCollection<ChatTimelineItem> Items { get; } = [];

    public ObservableCollection<MatrixEmote> Emotes { get; } = [];

    public ObservableRangeCollection<RoomMember> Members { get; } = [];

    public ObservableRangeCollection<ManagedRoom> Rooms { get; } = [];

    public Room Room => _room;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (Set(ref _isLoading, value))
            {
                Raise(nameof(CanSend));
            }
        }
    }

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (Set(ref _isSending, value))
            {
                Raise(nameof(CanSend));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (Set(ref _errorMessage, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public string TypingText
    {
        get => _typingText;
        private set => Set(ref _typingText, value);
    }

    public string DraftText
    {
        get => _draftText;
        set
        {
            if (!Set(ref _draftText, value))
            {
                return;
            }

            _typing.SetTyping(!string.IsNullOrWhiteSpace(value));
            Raise(nameof(CanSend));
        }
    }

    public ChatTimelineItem? ReplyTarget
    {
        get => _replyTarget;
        private set => Set(ref _replyTarget, value);
    }

    public ChatTimelineItem? EditTarget
    {
        get => _editTarget;
        private set => Set(ref _editTarget, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanLoadMoreHistory => !_timeline.HasReachedStart;

    public bool IsLoadingHistory => _timeline.IsLoadingHistory;

    public bool CanSend => !IsLoading && !IsSending && !string.IsNullOrWhiteSpace(DraftText);

    public bool IsServerNoticeRoom
    {
        get => _isServerNoticeRoom;
        private set => Set(ref _isServerNoticeRoom, value);
    }

    public static async Task<ChatSession> CreateAsync(
        ManagedMatrixClient client,
        Room room,
        CancellationToken cancellationToken = default,
        TimelineFocus? focus = null
    )
    {
        var timeline = await client.GetObservableTimelineAsync(room, focus, cancellationToken);

        var session = new ChatSession(client, room, timeline);
        _ = session.LoadRoomsAsync();
        _ = session.LoadMembersAsync();
        _ = session.LoadServerNoticeStateAsync();
        return session;
    }

    public async Task SendMessageAsync()
    {
        var text = DraftText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            using var content = CreateMarkdownContent(text);

            if (EditTarget?.EventId is { } editEventId)
            {
                await _room.Edit(editEventId, content);
            }
            else if (ReplyTarget?.EventId is { } eventId)
            {
                await _timeline.Timeline.SendReply(content, eventId);
            }
            else
            {
                await _timeline.Timeline.Send(content);
            }

            DraftText = string.Empty;
            ReplyTarget = null;
            EditTarget = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Debug.WriteLine($"Could not send message: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    public async Task SendAttachmentAsync(string filename, string mimeType, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(data);

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            var upload = new UploadParameters(
                new UploadSource.Data(data, filename),
                null,
                null,
                null,
                ReplyTarget?.EventId
            );
            using var handle = mimeType.Split('/', 2)[0] switch
            {
                "audio" => _timeline.Timeline.SendAudio(
                    upload,
                    new AudioInfo(null, (ulong)data.Length, mimeType)
                ),
                "image" => _timeline.Timeline.SendImage(
                    upload,
                    null,
                    new ImageInfo(null, null, mimeType, (ulong)data.Length, null, null, null, null)
                ),
                "video" => _timeline.Timeline.SendVideo(
                    upload,
                    null,
                    new VideoInfo(null, null, null, mimeType, (ulong)data.Length, null, null, null)
                ),
                _ => _timeline.Timeline.SendFile(
                    upload,
                    new uniffi.matrix_sdk_ffi.FileInfo(mimeType, (ulong)data.Length, null, null)
                ),
            };
            await handle.Join();
            ReplyTarget = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Debug.WriteLine($"Could not send attachment: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    public async Task LoadMoreHistoryAsync(
        ushort eventCount = 50,
        CancellationToken cancellationToken = default
    )
    {
        if (_timeline.IsLoadingHistory || _timeline.HasReachedStart)
        {
            return;
        }

        try
        {
            await _timeline.LoadMoreHistoryAsync(eventCount, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task MarkAsReadAsync()
    {
        var eventId = Items.LastOrDefault(item => item.EventId is not null)?.EventId;

        if (eventId is null || eventId == _lastReadEventId)
        {
            return;
        }

        try
        {
            await _timeline.Timeline.MarkAsRead(ReceiptType.Read);
            await _timeline.Timeline.MarkAsRead(ReceiptType.FullyRead);
            await _room.SetUnreadFlag(false);
            _lastReadEventId = eventId;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not send read receipt: {exception}");
        }
    }

    public async Task ToggleReactionAsync(ChatTimelineItem item, string key)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!UnicodeEmoji.IsValid(key))
        {
            Debug.WriteLine("Rejected a non-Unicode Matrix reaction key.");
            return;
        }

        await _timeline.Timeline.ToggleReaction(
            item.EventId is { } eventId
                ? new EventOrTransactionId.EventId(eventId)
                : new EventOrTransactionId.TransactionId(item.EventOrTransactionId),
            key
        );
    }

    /// <summary>Loads optional custom-emote metadata only when the composer asks for it.</summary>
    public async Task LoadCustomEmotesAsync(CancellationToken cancellationToken = default)
    {
        await _customEmotesLoadGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || _customEmotesLoaded)
                return;

            // Room packs already present in the local timeline require no extra request.
            foreach (var item in Items)
                UpdateEmotes(item.EventType, item.SourceJson);

            var userEmotes = await _client.GetUserEmotesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var emote in userEmotes)
            {
                if (!Emotes.Any(existing => existing.Name == emote.Name))
                    Emotes.Add(emote);
            }

            _customEmotesLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load custom emotes: {exception}");
        }
        finally
        {
            _customEmotesLoadGate.Release();
        }
    }

    public void ReplyTo(ChatTimelineItem? item)
    {
        EditTarget = null;
        ReplyTarget = item;
    }

    public void Edit(ChatTimelineItem? item)
    {
        if (item is not { IsOwn: true, IsMessage: true, EventId: not null })
        {
            return;
        }

        ReplyTarget = null;
        EditTarget = item;
        DraftText = MarkdownFromHtml(item.FormattedBody, item.Body);
    }

    public void CancelReply()
    {
        ReplyTarget = null;
    }

    public void CancelEdit()
    {
        EditTarget = null;
        DraftText = string.Empty;
    }

    public async Task CreatePollAsync(
        string question,
        IEnumerable<string> answers,
        byte maxSelections = 1
    )
    {
        var choices = answers
            .Select(answer => answer.Trim())
            .Where(answer => !string.IsNullOrWhiteSpace(answer))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(question) || choices.Length < 2)
        {
            throw new ArgumentException(
                "A poll needs a question and at least two distinct answers."
            );
        }

        await _timeline.Timeline.CreatePoll(
            question.Trim(),
            choices,
            Math.Clamp(maxSelections, (byte)1, (byte)choices.Length),
            PollKind.Disclosed
        );
    }

    public async Task VoteInPollAsync(ChatTimelineItem item, string answerId)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(answerId);
        if (item.EventId is null || item.Poll is not { IsClosed: false } poll)
        {
            return;
        }

        var snapshot = poll.Snapshot();
        var answers = poll.Select(answerId);
        if (answers.Length == 0)
            return;

        IsSending = true;
        ErrorMessage = string.Empty;
        try
        {
            await _timeline.Timeline.SendPollResponse(item.EventId, answers);
        }
        catch (Exception exception)
        {
            poll.Restore(snapshot);
            ErrorMessage = exception.Message;
            Debug.WriteLine($"Could not vote in poll: {exception}");
        }
        finally
        {
            IsSending = false;
        }
    }

    public Task SendLocationAsync(string geoUri, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoUri);
        if (!geoUri.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Use a geo: URI, for example geo:52.3676,4.9041.",
                nameof(geoUri)
            );
        }

        return _room.SendRaw(
            "m.room.message",
            JsonSerializer.Serialize(
                new
                {
                    body = description ?? geoUri,
                    msgtype = "m.location",
                    geo_uri = geoUri,
                }
            )
        );
    }

    public Task SendStickerAsync(MatrixEmote emote)
    {
        ArgumentNullException.ThrowIfNull(emote);
        return _room.SendRaw(
            "m.sticker",
            JsonSerializer.Serialize(
                new
                {
                    body = emote.Body,
                    url = emote.Source,
                    info = new { },
                }
            )
        );
    }

    public async Task<bool> CanDeleteAsync(ChatTimelineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.EventId is null)
        {
            return false;
        }

        try
        {
            using var powerLevels = await _room.GetPowerLevels();
            return item.IsOwn
                ? powerLevels.CanOwnUserRedactOwn()
                : powerLevels.CanOwnUserRedactOther();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(ChatTimelineItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.EventId is not { } eventId || !await CanDeleteAsync(item))
        {
            return false;
        }

        await _room.Redact(eventId, null);
        return true;
    }

    private void OnTypingChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RoomTypingController.Text))
        {
            RunOnCapturedContext(() => TypingText = _typing.Text);
        }
    }

    private void OnTimelineChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        RunOnCapturedContext(() => ApplyTimelineChange(eventArgs));
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ObservableTimeline.HasReachedStart))
        {
            RunOnCapturedContext(() => Raise(nameof(CanLoadMoreHistory)));
        }
        else if (eventArgs.PropertyName == nameof(ObservableTimeline.IsLoadingHistory))
        {
            RunOnCapturedContext(() => Raise(nameof(IsLoadingHistory)));
        }
    }

    private void ApplyTimelineChange(NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        switch (eventArgs.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertItems(eventArgs.NewStartingIndex, eventArgs.NewItems);
                break;

            case NotifyCollectionChangedAction.Remove:
                RemoveItems(eventArgs.OldStartingIndex, eventArgs.OldItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Replace:
                ReplaceItems(eventArgs.NewStartingIndex, eventArgs.NewItems);
                break;

            case NotifyCollectionChangedAction.Move:
                MoveItems(
                    eventArgs.OldStartingIndex,
                    eventArgs.NewStartingIndex,
                    eventArgs.NewItems?.Count ?? 1
                );
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetItems();
                break;
        }

        switch (eventArgs.Action)
        {
            case NotifyCollectionChangedAction.Add when eventArgs.NewStartingIndex >= 0:
            case NotifyCollectionChangedAction.Replace when eventArgs.NewStartingIndex >= 0:
                UpdateMessageGroups(eventArgs.NewStartingIndex, eventArgs.NewItems?.Count ?? 1);
                break;
            case NotifyCollectionChangedAction.Remove when eventArgs.OldStartingIndex >= 0:
                UpdateMessageGroups(eventArgs.OldStartingIndex, 0);
                break;
            default:
                UpdateMessageGroups();
                break;
        }

    }

    private void ResetItems()
    {
        var timelineItems = _timeline.Select(CreateItem).ToArray();
        if (
            Items.Count <= timelineItems.Length
            && Items.Select((item, index) => item.Id == timelineItems[index].Id).All(matches => matches)
        )
        {
            for (var i = 0; i < Items.Count; i++)
                Items[i].UpdateFrom(timelineItems[i]);
            Items.AddRange(timelineItems.Skip(Items.Count));
            return;
        }

        var offset = timelineItems.Length - Items.Count;
        if (
            offset >= 0
            && Items.Select((item, index) => item.Id == timelineItems[index + offset].Id)
                .All(matches => matches)
        )
        {
            for (var i = 0; i < Items.Count; i++)
                Items[i].UpdateFrom(timelineItems[i + offset]);
            Items.InsertRange(0, timelineItems.Take(offset));
            return;
        }

        var index = 0;

        foreach (var timelineItem in timelineItems)
        {
            var existingIndex = FindItemIndex(timelineItem.Id, index);
            if (existingIndex < 0)
            {
                Items.Insert(index++, timelineItem);
                continue;
            }

            while (index < existingIndex)
            {
                Items.RemoveAt(index);
                existingIndex--;
            }

            Items[index++].UpdateFrom(timelineItem);
        }

        while (Items.Count > index)
        {
            Items.RemoveAt(index);
        }
    }

    private void UpdateMessageGroups(int start, int count)
    {
        for (
            var index = Math.Max(0, start - 1);
            index < Math.Min(Items.Count, start + count + 1);
            index++
        )
        {
            var item = Items[index];
            item.IsGroupStart = index == 0 || !IsSameMessageGroup(item, Items[index - 1]);
            item.IsGroupEnd =
                index == Items.Count - 1 || !IsSameMessageGroup(item, Items[index + 1]);
        }
    }

    private int FindItemIndex(string id, int startIndex)
    {
        for (var index = startIndex; index < Items.Count; index++)
        {
            if (Items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private void InsertItems(int index, System.Collections.IList? values)
    {
        if (values is null)
        {
            return;
        }

        var target = index < 0 ? Items.Count : index;

        foreach (TimelineItem value in values)
        {
            Items.Insert(target++, CreateItem(value));
        }
    }

    private void RemoveItems(int index, int count)
    {
        if (index < 0)
        {
            return;
        }

        while (count-- > 0 && index < Items.Count)
        {
            Items.RemoveAt(index);
        }
    }

    private void ReplaceItems(int index, System.Collections.IList? values)
    {
        if (values is null || index < 0)
        {
            return;
        }

        foreach (TimelineItem value in values)
        {
            var replacement = CreateItem(value);

            if (index < Items.Count && Items[index].Id == replacement.Id)
            {
                Items[index].UpdateFrom(replacement);
            }
            else if (index < Items.Count)
            {
                Items[index] = replacement;
            }
            else
            {
                Items.Add(replacement);
            }

            index++;
        }
    }

    private void MoveItems(int oldIndex, int newIndex, int count)
    {
        while (count-- > 0 && oldIndex >= 0 && oldIndex < Items.Count)
        {
            Items.Move(oldIndex, newIndex);
        }
    }

    private ChatTimelineItem CreateItem(TimelineItem timelineItem)
    {
        var id = timelineItem.UniqueId().Id;
        var eventItem = timelineItem.AsEvent();

        if (eventItem is null)
        {
            return timelineItem.AsVirtual() switch
            {
                VirtualTimelineItem.ReadMarker => new ChatTimelineItem(id)
                {
                    EventType = "read marker",
                    Body = "Unread",
                    IsReadMarker = true,
                },
                VirtualTimelineItem.DateDivider divider => new ChatTimelineItem(id)
                {
                    EventType = "date divider",
                    Body = DateTimeOffset
                        .FromUnixTimeMilliseconds((long)Math.Min(divider.Ts, 253402300799999UL))
                        .ToLocalTime()
                        .ToString("D"),
                },
                VirtualTimelineItem.TimelineStart => new ChatTimelineItem(id)
                {
                    EventType = "timeline start",
                    Body = "Beginning of chat",
                },
                _ => new ChatTimelineItem(id)
                {
                    EventType = "timeline marker",
                    Body = "Timeline marker",
                    SourceJson = FormatSource(null, new { timeline = timelineItem.FmtDebug() }),
                },
            };
        }

        using (eventItem)
        {
            var result = new ChatTimelineItem(id)
            {
                IsOwn = eventItem.IsOwn,
                CanReply = eventItem.CanBeRepliedTo,
                Sender = DisplayName(eventItem),
                SenderId = eventItem.Sender,
                SenderAvatarUrl = AvatarUrl(eventItem),
                EventType = eventItem.EventTypeRaw ?? "unknown",
                EventId = (
                    eventItem.EventOrTransactionId as EventOrTransactionId.EventId
                )?.EventIdValue,
                EventOrTransactionId = EventOrTransactionIdValue(eventItem.EventOrTransactionId),
                IsRemoteEvent = eventItem.IsRemote,
                SourceJson = FormatSource(
                    eventItem.LazyProvider.LatestJson(),
                    new
                    {
                        event_id = (
                            eventItem.EventOrTransactionId as EventOrTransactionId.EventId
                        )?.EventIdValue,
                        type = eventItem.EventTypeRaw,
                        sender = eventItem.Sender,
                    }
                ),
            };

            if (_customEmotesLoaded)
                UpdateEmotes(eventItem.EventTypeRaw, result.SourceJson);

            PopulateContent(result, eventItem);
            if (
                !result.IsMessage
                && (
                    eventItem.Content is not TimelineItemContent.RoomMembership membership
                    || membership.UserId != result.SenderId
                )
            )
            {
                result.Body = $"{DisplayNameAndId(result.Sender, result.SenderId)} — {result.Body}";
            }
            PopulateReactions(result, eventItem);
            PopulateReadReceipts(result, eventItem);
            return result;
        }
    }

    private void UpdateMessageGroups()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            item.IsGroupStart = index == 0 || !IsSameMessageGroup(item, Items[index - 1]);
            item.IsGroupEnd =
                index == Items.Count - 1 || !IsSameMessageGroup(item, Items[index + 1]);
        }
    }

    private static bool IsSameMessageGroup(ChatTimelineItem left, ChatTimelineItem right) =>
        left.IsMessage
        && right.IsMessage
        && left.IsOwn == right.IsOwn
        && left.SenderId == right.SenderId;

    private void UpdateEmotes(string? eventType, string source)
    {
        if (eventType is not "im.ponies.room_emotes" and not "m.emoji")
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(source);
            var content = document.RootElement.GetProperty("content");
            var images = content.TryGetProperty("images", out var imagePack) ? imagePack : content;

            if (images.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var image in images.EnumerateObject())
            {
                if (
                    !image.Value.TryGetProperty("url", out var url)
                    || url.GetString() is not { } mediaSource
                    || Emotes.Any(emote => emote.Name == image.Name)
                )
                {
                    continue;
                }

                Emotes.Add(
                    new MatrixEmote(
                        image.Name,
                        image.Value.TryGetProperty("body", out var body)
                            ? body.GetString() ?? image.Name
                            : image.Name,
                        mediaSource
                    )
                );
            }
        }
        catch (JsonException)
        {
            // The SDK can expose local echoes without raw JSON.
        }
    }

    private async Task LoadRoomsAsync()
    {
        await Task.Yield();
        if (_disposed)
            return;

        try
        {
            var currentRoomId = _room.Id();
            Rooms.AddRange(
                _client
                    .GetRooms()
                    .Select(room => new ManagedRoom(room, includeRoomInfo: false))
                    .Where(room => !room.IsSpace && room.Id != currentRoomId)
            );
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load composer rooms: {exception}");
        }
    }

    private void PopulateContent(ChatTimelineItem result, EventTimelineItem eventItem)
    {
        switch (eventItem.Content)
        {
            case TimelineItemContent.MsgLike msg
                when msg.Content.Kind is MsgLikeKind.Message message:
                result.IsMessage = true;
                result.Body = message.Content.Body;
                result.FormattedBody = GetFormattedBody(message.Content.MsgType)?.Body;
                result.ReplyPreview = ReplyPreview(msg.Content.InReplyTo);
                result.ReplyToEventId = msg.Content.InReplyTo?.EventId();
                result.ThreadRootEventId = msg.Content.ThreadRoot;
                result.ThreadReplyCount = msg.Content.ThreadSummary?.NumReplies() ?? 0;
                result.IsServerNotice = message.Content.MsgType is MessageType.Other
                {
                    Msgtype: "m.server_notice",
                };
                result.Media = CreateMedia(message.Content.MsgType);
                return;

            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Sticker sticker }:
                result.IsMessage = true;
                result.Body = sticker.Body;
                result.Media = new ChatMedia(
                    ChatMediaKind.Image,
                    sticker.Source.ToJson(),
                    sticker.Body,
                    sticker.Info.Mimetype,
                    sticker.Info.ThumbnailSource?.ToJson()
                );
                return;

            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Poll poll }:
                result.IsMessage = true;
                result.Body = poll.Question;
                result.Poll = new ChatPoll(
                    poll.Question,
                    poll.Answers.Select(answer => new ChatPollAnswer(
                        answer.Id,
                        answer.Text,
                        poll.Votes.TryGetValue(answer.Id, out var voters) ? voters.Length : 0,
                        poll.Votes.TryGetValue(answer.Id, out voters)
                            && voters.Contains(_room.OwnUserId())
                    )),
                    poll.MaxSelections,
                    poll.EndTime is not null
                );
                return;

            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Redacted }:
                result.IsMessage = true;
                result.Body = "Message removed";
                return;

            case TimelineItemContent.MsgLike
            {
                Content.Kind: MsgLikeKind.UnableToDecrypt,
            }:
                result.IsMessage = true;
                result.Body =
                    "Unable to decrypt this message. Encryption keys are unavailable on this device.";
                return;

            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.LiveLocation live }:
                result.IsMessage = true;
                result.Body =
                    live.Content.Description
                    ?? (live.Content.IsLive
                        ? "Started sharing live location"
                        : "Stopped sharing live location");
                return;

            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Other other }:
                result.Body =
                    RawEventBody(result.SourceJson) ?? MessageLikeEventText(other.EventType);
                return;

            case TimelineItemContent.RoomMembership membership:
                result.Body =
                    $"{DisplayNameAndId(membership.UserDisplayName, membership.UserId)} {MembershipText(membership.Change)}";
                return;

            case TimelineItemContent.ProfileChange profile:
                RefreshAvatar(profile.PrevAvatarUrl, profile.AvatarUrl);
                result.Body = ProfileText(profile);
                return;

            case TimelineItemContent.State { Content: OtherState.RoomAvatar avatar }:
                RefreshAvatar(_roomAvatarUrl, avatar.Url);
                _roomAvatarUrl = avatar.Url;
                result.Body = StateText(avatar);
                return;

            case TimelineItemContent.State state:
                result.Body =
                    state.Content is OtherState.Custom custom
                        ? RawEventBody(result.SourceJson) ?? $"Updated {custom.EventType}"
                        : StateText(state.Content);
                return;

            case TimelineItemContent.RtcNotification:
                result.Body = "Call notification";
                return;

            case TimelineItemContent.CallInvite:
                result.Body = "Incoming call";
                return;

            case TimelineItemContent.FailedToParseMessageLike failed:
                result.Body =
                    RawEventBody(result.SourceJson) ?? $"Unsupported event: {failed.EventType}";
                return;

            case TimelineItemContent.FailedToParseState failed:
                result.Body =
                    RawEventBody(result.SourceJson)
                    ?? $"Unsupported room update: {failed.EventType}";
                return;

            default:
                result.Body = $"Event: {result.EventType}";
                return;
        }
    }

    private void PopulateReactions(ChatTimelineItem result, EventTimelineItem eventItem)
    {
        if (eventItem.Content is not TimelineItemContent.MsgLike message)
        {
            return;
        }

        foreach (var reaction in message.Content.Reactions)
        {
            result.Reactions.Add(
                new ChatReaction(
                    reaction.Key,
                    reaction.Senders.Length,
                    reaction.Senders.Any(sender => sender.SenderId == _room.OwnUserId())
                )
            );
        }
    }

    private void PopulateReadReceipts(ChatTimelineItem result, EventTimelineItem eventItem)
    {
        foreach (var userId in eventItem.ReadReceipts.Keys)
        {
            var member = Members.FirstOrDefault(candidate => candidate.UserId == userId);
            result.ReadReceipts.Add(
                new ChatReadReceipt(
                    userId,
                    member?.DisplayName ?? userId,
                    member?.AvatarUrl
                )
            );
        }
    }

    private static ChatMedia? CreateMedia(MessageType type) =>
        type switch
        {
            MessageType.Image image => new ChatMedia(
                ChatMediaKind.Image,
                image.Content.Source.ToJson(),
                image.Content.Filename,
                image.Content.Info?.Mimetype,
                image.Content.Info?.ThumbnailSource?.ToJson()
            ),
            MessageType.Video video => new ChatMedia(
                ChatMediaKind.Video,
                video.Content.Source.ToJson(),
                video.Content.Filename,
                video.Content.Info?.Mimetype,
                video.Content.Info?.ThumbnailSource?.ToJson()
            ),
            MessageType.Audio audio => new ChatMedia(
                ChatMediaKind.Audio,
                audio.Content.Source.ToJson(),
                audio.Content.Filename,
                audio.Content.Info?.Mimetype
            ),
            MessageType.File file => new ChatMedia(
                ChatMediaKind.File,
                file.Content.Source.ToJson(),
                file.Content.Filename,
                file.Content.Info?.Mimetype
            ),
            _ => null,
        };

    private async Task LoadMembersAsync()
    {
        await _membersLoadGate.WaitAsync();
        try
        {
            if (_disposed)
                return;
            using var iterator = await _room.Members();

            // ponytail: one page feeds typeahead; add query-backed search for very large rooms.
            if (iterator.NextChunk(100) is { Length: > 0 } members)
            {
                var joined = members
                    .Where(member => member.Membership is MembershipState.Join)
                    .ToArray();
                var profiles = joined.ToDictionary(member => member.UserId);
                RunOnCapturedContext(() =>
                {
                    if (_disposed)
                        return;

                    if (!Members.SequenceEqual(joined))
                        Members.ReplaceAll(joined);

                    foreach (
                        var item in Items.Where(item =>
                            string.IsNullOrWhiteSpace(item.SenderAvatarUrl)
                        )
                    )
                    {
                        if (!profiles.TryGetValue(item.SenderId, out var member))
                            continue;
                        item.SenderAvatarUrl = member.AvatarUrl;
                        item.Sender = member.DisplayName ?? member.UserId;
                    }

                    foreach (var receipt in Items.SelectMany(item => item.ReadReceipts))
                    {
                        if (!profiles.TryGetValue(receipt.UserId, out var member))
                            continue;
                        receipt.Name = member.DisplayName ?? member.UserId;
                        receipt.AvatarUrl = member.AvatarUrl;
                    }
                });
            }
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                Debug.WriteLine($"Could not load room members: {exception}");
            }
        }
        finally
        {
            _membersLoadGate.Release();
        }
    }

    private async Task LoadServerNoticeStateAsync()
    {
        IsServerNoticeRoom = await _client.IsServerNoticeRoomAsync(_room.Id());
    }

    private sealed class MemberListUpdateListener(ChatSession session) : RoomInfoListener
    {
        public void Call(RoomInfo roomInfo) => _ = session.LoadMembersAsync();
    }

    private RoomMessageEventContentWithoutRelation CreateMarkdownContent(string markdown)
    {
        var userIds = new HashSet<string>(StringComparer.Ordinal);
        var roomMention = markdown.Contains("@room", StringComparison.Ordinal);
        var content = MentionPattern.Replace(
            markdown,
            match =>
            {
                var value = match.Groups["value"].Value;
                var member = Members.FirstOrDefault(candidate =>
                    candidate.UserId == $"@{value}"
                    || string.Equals(
                        candidate.DisplayName,
                        value,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (member is null)
                {
                    return match.Value;
                }

                userIds.Add(member.UserId);
                var label = WebUtility.HtmlEncode(match.Value);
                return $"<a href=\"https://matrix.to/#/{member.UserId}\">{label}</a>";
            }
        );

        content = EmotePattern.Replace(
            content,
            match =>
            {
                var emote = Emotes.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Name,
                        match.Groups["name"].Value,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                return emote is null
                    ? match.Value
                    : $"<img data-mx-emoticon src=\"{WebUtility.HtmlEncode(emote.Source)}\" alt=\"{WebUtility.HtmlEncode(match.Value)}\" title=\"{WebUtility.HtmlEncode(emote.Body)}\" height=\"32\" />";
            }
        );

        var result = MatrixSdkFfiMethods.MessageEventContentFromMarkdown(content);
        if (userIds.Count == 0 && !roomMention)
        {
            return result;
        }

        var mentioned = result.WithMentions(new Mentions(userIds.ToArray(), roomMention));
        result.Dispose();
        return mentioned;
    }

    private static FormattedBody? GetFormattedBody(MessageType type) =>
        type switch
        {
            MessageType.Text text => text.Content.Formatted,
            MessageType.Notice notice => notice.Content.Formatted,
            MessageType.Emote emote => emote.Content.Formatted,
            _ => null,
        };

    private static string MarkdownFromHtml(string? html, string fallback)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return fallback;
        }

        try
        {
            var document = XDocument.Parse($"<root>{EditVoidTag.Replace(html, "<$1$2 />")}</root>");
            return string.Concat(document.Root!.Nodes().Select(MarkdownNode)).Trim();
        }
        catch
        {
            return fallback;
        }
    }

    private static string MarkdownNode(XNode node) =>
        node switch
        {
            XText text => text.Value,
            XElement element => MarkdownElement(element),
            _ => string.Empty,
        };

    private static string MarkdownElement(XElement element)
    {
        var content = string.Concat(element.Nodes().Select(MarkdownNode));
        return element.Name.LocalName.ToLowerInvariant() switch
        {
            "br" => "\n",
            "p" or "div" => $"{content}\n\n",
            "b" or "strong" => $"**{content}**",
            "i" or "em" => $"*{content}*",
            "s" or "del" or "strike" => $"~~{content}~~",
            "code" => $"`{content}`",
            "pre" => $"```\n{element.Value}\n```\n",
            "blockquote" => string.Join(
                '\n',
                content.TrimEnd().Split('\n').Select(line => $"> {line}")
            ) + "\n",
            "a"
                when element
                    .Attribute("href")
                    ?.Value.Contains("matrix.to/#/@", StringComparison.OrdinalIgnoreCase) == true =>
                content,
            "a" when element.Attribute("href")?.Value is { } href => $"[{content}]({href})",
            "img" => element.Attribute("alt")?.Value ?? string.Empty,
            "li" => $"- {content.Trim()}\n",
            "ul" or "ol" => $"{content}\n",
            "mx-reply" => string.Empty,
            _ => content,
        };
    }

    private static readonly Regex MentionPattern = new(
        @"(?<![\w@])@(?<value>[\w.=/\-]+(?::[\w.\-]+)?)",
        RegexOptions.Compiled
    );

    private static readonly Regex EmotePattern = new(@":(?<name>[\w+\-]+):", RegexOptions.Compiled);

    private static readonly Regex EditVoidTag = new(
        @"<(br|hr|img)(\s[^>]*?)?(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static string DisplayName(EventTimelineItem item) =>
        item.SenderProfile is ProfileDetails.Ready ready
        && !string.IsNullOrWhiteSpace(ready.DisplayName)
            ? ready.DisplayName
            : item.Sender;

    private static string DisplayNameAndId(string? displayName, string userId) =>
        string.IsNullOrWhiteSpace(displayName) || displayName == userId
            ? userId
            : $"{displayName} ({userId})";

    private static string? AvatarUrl(EventTimelineItem item) =>
        item.SenderProfile is ProfileDetails.Ready ready ? ready.AvatarUrl : null;

    private void RefreshAvatar(string? previous, string? current)
    {
        if (_canInvalidateAvatars && !_timeline.IsLoadingHistory)
        {
            _client.RefreshAvatar(previous, current);
        }
    }

    private static string EventOrTransactionIdValue(EventOrTransactionId id) =>
        id switch
        {
            EventOrTransactionId.EventId eventId => eventId.EventIdValue,
            EventOrTransactionId.TransactionId transactionId => transactionId.TransactionIdValue,
            _ => string.Empty,
        };

    private static string MembershipText(MembershipChange? change) =>
        change switch
        {
            MembershipChange.Joined => "joined the room",
            MembershipChange.Left => "left the room",
            MembershipChange.Invited => "was invited",
            MembershipChange.Kicked => "was removed",
            MembershipChange.Banned => "was banned",
            _ => "changed membership",
        };

    private static string ProfileText(TimelineItemContent.ProfileChange profile) =>
        profile.DisplayName is not null
            ? $"changed their display name to {profile.DisplayName}"
            : "updated their profile";

    private static string StateText(OtherState state) =>
        state switch
        {
            OtherState.PolicyRuleRoom => "Updated a room moderation rule",
            OtherState.PolicyRuleServer => "Updated a server moderation rule",
            OtherState.PolicyRuleUser => "Updated a user moderation rule",
            OtherState.RoomName { Name: { } name } => $"changed the room name to {name}",
            OtherState.RoomTopic { Topic: { } topic } => $"changed the room topic to {topic}",
            OtherState.RoomAvatar => "changed the room avatar",
            OtherState.RoomCanonicalAlias => "changed the room address",
            OtherState.RoomEncryption => "enabled encryption",
            OtherState.RoomCreate => "created the room",
            OtherState.RoomGuestAccess => "changed guest access",
            OtherState.RoomHistoryVisibility visibility =>
                $"changed history visibility to {visibility.HistoryVisibility}",
            OtherState.RoomJoinRules rules =>
                $"changed join rules to {JoinRuleText(rules.JoinRule)}",
            OtherState.RoomPinnedEvents => "updated pinned messages",
            OtherState.RoomPowerLevels => "changed room permissions",
            OtherState.RoomServerAcl => "changed the server access rules",
            OtherState.RoomThirdPartyInvite invite =>
                invite.DisplayName is { } name
                    ? $"invited {name}"
                    : "updated a third-party invite",
            OtherState.RoomTombstone => "upgraded the room",
            OtherState.SpaceChild => "updated a room in this space",
            OtherState.SpaceParent => "updated this room's parent space",
            OtherState.Custom { EventType: { } type } => $"Updated {type}",
            _ => "Room settings changed",
        };

    private static string JoinRuleText(JoinRule? rule) =>
        rule switch
        {
            JoinRule.Public => "public",
            JoinRule.Invite => "invite only",
            JoinRule.Knock => "knock",
            JoinRule.Private => "private",
            JoinRule.Restricted => "restricted",
            JoinRule.KnockRestricted => "knock restricted",
            JoinRule.Custom custom => custom.Repr,
            _ => "unknown",
        };

    private static string MessageLikeEventText(MessageLikeEventType eventType) =>
        eventType switch
        {
            MessageLikeEventType.Audio or MessageLikeEventType.Voice => "Shared audio",
            MessageLikeEventType.Image => "Shared an image",
            MessageLikeEventType.Video => "Shared a video",
            MessageLikeEventType.File => "Shared a file",
            MessageLikeEventType.Location or MessageLikeEventType.Beacon => "Shared a location",
            MessageLikeEventType.Emote => "Sent an emote",
            MessageLikeEventType.CallInvite => "Incoming call",
            MessageLikeEventType.CallAnswer or MessageLikeEventType.CallSelectAnswer =>
                "Call answered",
            MessageLikeEventType.CallHangup => "Call ended",
            MessageLikeEventType.CallReject or MessageLikeEventType.RtcDecline => "Call declined",
            MessageLikeEventType.CallCandidates
                or MessageLikeEventType.CallNegotiate
                or MessageLikeEventType.CallSdpStreamMetadataChanged
                or MessageLikeEventType.CallNotify
                or MessageLikeEventType.RtcNotification => "Call updated",
            MessageLikeEventType.KeyVerificationAccept
                or MessageLikeEventType.KeyVerificationCancel
                or MessageLikeEventType.KeyVerificationDone
                or MessageLikeEventType.KeyVerificationKey
                or MessageLikeEventType.KeyVerificationMac
                or MessageLikeEventType.KeyVerificationReady
                or MessageLikeEventType.KeyVerificationStart => "Key verification updated",
            MessageLikeEventType.PollStart or MessageLikeEventType.UnstablePollStart =>
                "Started a poll",
            MessageLikeEventType.PollEnd or MessageLikeEventType.UnstablePollEnd =>
                "Ended a poll",
            MessageLikeEventType.PollResponse or MessageLikeEventType.UnstablePollResponse =>
                "Voted in a poll",
            MessageLikeEventType.Reaction => "Reacted to a message",
            MessageLikeEventType.RoomRedaction => "Removed an event",
            MessageLikeEventType.Encrypted or MessageLikeEventType.RoomEncrypted =>
                "Encrypted event",
            MessageLikeEventType.Sticker => "Sent a sticker",
            MessageLikeEventType.Message or MessageLikeEventType.RoomMessage => "Sent a message",
            MessageLikeEventType.Other other => $"Event: {other.V1}",
            _ => "Room event",
        };

    private static string? RawEventBody(string source)
    {
        try
        {
            using var document = JsonDocument.Parse(source);
            return document.RootElement.TryGetProperty("content", out var content)
                && content.TryGetProperty("body", out var body)
                && body.ValueKind is JsonValueKind.String
                ? body.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReplyPreview(InReplyToDetails? reply)
    {
        if (reply is null)
        {
            return null;
        }

        using var details = reply.Event();
        return
            details
                is EmbeddedEventDetails.Ready
                {
                    Content: TimelineItemContent.MsgLike
                    {
                        Content.Kind: MsgLikeKind.Message message,
                    },
                }
            ? $"Replying to: {message.Content.Body.ReplaceLineEndings(" ")}"
            : "Replying to a message";
    }

    private static string FormatSource(string? source, object fallback)
    {
        if (!string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        return JsonSerializer.Serialize(
            fallback,
            new JsonSerializerOptions { WriteIndented = true }
        );
    }

    private void RunOnCapturedContext(System.Action action)
    {
        if (
            _synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext)
        )
        {
            action();
            return;
        }

        _synchronizationContext.Post(static state => ((System.Action)state!).Invoke(), action);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timeline.CollectionChanged -= OnTimelineChanged;
        ((INotifyPropertyChanged)_timeline).PropertyChanged -= OnTimelinePropertyChanged;
        _typing.PropertyChanged -= OnTypingChanged;
        _roomInfoSubscription.Cancel();
        _roomInfoSubscription.Dispose();
        _typing.Dispose();
        _timeline.Dispose();
        await Task.CompletedTask;
    }
}
