using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ChatSession : ObservableModel, IAsyncDisposable
{
    private readonly ManagedMatrixClient _client;
    private readonly Room _room;
    private readonly ObservableTimeline _timeline;
    private readonly RoomTypingController _typing;
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

    private ChatSession(
        ManagedMatrixClient client,
        Room room,
        ObservableTimeline timeline)
    {
        _client = client;
        _room = room;
        _timeline = timeline;
        _synchronizationContext = SynchronizationContext.Current;
        _typing = new RoomTypingController(room);
        _typing.PropertyChanged += OnTypingChanged;
        ((INotifyPropertyChanged)_timeline).PropertyChanged +=
            OnTimelinePropertyChanged;
        _timeline.CollectionChanged += OnTimelineChanged;
        _typing.Start();
        ResetItems();
    }

    public ObservableCollection<ChatTimelineItem> Items { get; } = [];

    public ObservableCollection<MatrixEmote> Emotes { get; } = [];

    public ObservableCollection<RoomMember> Members { get; } = [];

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

    public bool CanSend =>
        !IsLoading &&
        !IsSending &&
        !string.IsNullOrWhiteSpace(DraftText);

    public static async Task<ChatSession> CreateAsync(
        ManagedMatrixClient client,
        Room room,
        CancellationToken cancellationToken = default)
    {
        var timeline = await client.GetObservableTimelineAsync(
            room,
            cancellationToken);

        var session = new ChatSession(client, room, timeline);
        _ = session.LoadMembersAsync();
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

    public async Task SendAttachmentAsync(
        string filename,
        string mimeType,
        byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(data);

        IsSending = true;
        ErrorMessage = string.Empty;

        try
        {
            var url = await _client.UploadMediaAsync(mimeType, data);
            using var source = MediaSource.FromUrl(url);
            using var content = _timeline.Timeline.CreateMessageContent(
                CreateAttachment(filename, mimeType, (ulong)data.Length, source))
                ?? throw new InvalidOperationException(
                    "Could not create attachment content.");

            await _timeline.Timeline.Send(content);
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

    public async Task LoadMoreHistoryAsync()
    {
        if (_timeline.IsLoadingHistory || _timeline.HasReachedStart)
        {
            return;
        }

        try
        {
            await _timeline.LoadMoreHistoryAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task MarkAsReadAsync()
    {
        var eventId = Items.LastOrDefault(item => item.EventId is not null)
            ?.EventId;

        if (eventId is null || eventId == _lastReadEventId)
        {
            return;
        }

        try
        {
            await _timeline.Timeline.MarkAsRead(ReceiptType.Read);
            _lastReadEventId = eventId;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not send read receipt: {exception}");
        }
    }

    public async Task ToggleReactionAsync(
        ChatTimelineItem item,
        string key)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _timeline.Timeline.ToggleReaction(
            item.EventId is { } eventId
                ? new EventOrTransactionId.EventId(eventId)
                : new EventOrTransactionId.TransactionId(
                    item.EventOrTransactionId),
            key);
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

    public void CancelReply() => ReplyTarget = null;

    public void CancelEdit()
    {
        EditTarget = null;
        DraftText = string.Empty;
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

    private void OnTimelineChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        RunOnCapturedContext(() => ApplyTimelineChange(eventArgs));
    }

    private void OnTimelinePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ObservableTimeline.HasReachedStart))
        {
            RunOnCapturedContext(() => Raise(nameof(CanLoadMoreHistory)));
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
                MoveItems(eventArgs.OldStartingIndex, eventArgs.NewStartingIndex,
                    eventArgs.NewItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Reset:
                ResetItems();
                break;
        }

        UpdateGroups();
    }

    private void ResetItems()
    {
        var timelineItems = _timeline.Select(CreateItem).ToArray();
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

        UpdateGroups();
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
            return new ChatTimelineItem(id)
            {
                EventType = "timeline marker",
                Body = "Unknown event",
                IsUnknown = true,
                SourceJson = FormatSource(null, new { timeline = timelineItem.FmtDebug() }),
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
                EventId = (eventItem.EventOrTransactionId as EventOrTransactionId.EventId)
                    ?.EventIdValue,
                EventOrTransactionId = EventOrTransactionIdValue(
                    eventItem.EventOrTransactionId),
                IsRemoteEvent = eventItem.IsRemote,
                SourceJson = FormatSource(
                    eventItem.LazyProvider.LatestJson(),
                    new
                    {
                        event_id = (eventItem.EventOrTransactionId as EventOrTransactionId.EventId)?.EventIdValue,
                        type = eventItem.EventTypeRaw,
                        sender = eventItem.Sender,
                    }),
            };

            UpdateEmotes(eventItem.EventTypeRaw, result.SourceJson);

            PopulateContent(result, eventItem);
            PopulateReactions(result, eventItem);
            return result;
        }
    }

    private void UpdateGroups()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            item.IsGroupStart = index == 0 || !IsSameMessageGroup(item, Items[index - 1]);
            item.IsGroupEnd = index == Items.Count - 1 || !IsSameMessageGroup(item, Items[index + 1]);
        }

        for (var index = 0; index < Items.Count;)
        {
            if (Items[index].IsMessage)
            {
                Items[index].EventGroup = null;
                Items[index].IsEventGroupHeader = false;
                index++;
                continue;
            }

            var start = index;
            while (index < Items.Count && !Items[index].IsMessage)
            {
                index++;
            }

            var group = new ChatEventGroup(index - start);
            for (var eventIndex = start; eventIndex < index; eventIndex++)
            {
                Items[eventIndex].EventGroup = group;
                Items[eventIndex].IsEventGroupHeader = eventIndex == start;
            }
        }
    }

    private static bool IsSameMessageGroup(ChatTimelineItem left, ChatTimelineItem right) =>
        left.IsMessage &&
        right.IsMessage &&
        left.IsOwn == right.IsOwn &&
        left.SenderId == right.SenderId;

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
            var images = content.TryGetProperty("images", out var imagePack)
                ? imagePack
                : content;

            if (images.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var image in images.EnumerateObject())
            {
                if (!image.Value.TryGetProperty("url", out var url) ||
                    url.GetString() is not { } mediaSource ||
                    Emotes.Any(emote => emote.Name == image.Name))
                {
                    continue;
                }

                Emotes.Add(new MatrixEmote(
                    image.Name,
                    image.Value.TryGetProperty("body", out var body)
                        ? body.GetString() ?? image.Name
                        : image.Name,
                    mediaSource));
            }
        }
        catch (JsonException)
        {
            // The SDK can expose local echoes without raw JSON.
        }
    }

    private static void PopulateContent(
        ChatTimelineItem result,
        EventTimelineItem eventItem)
    {
        switch (eventItem.Content)
        {
            case TimelineItemContent.MsgLike msg when msg.Content.Kind is MsgLikeKind.Message message:
                result.IsMessage = true;
                result.Body = message.Content.Body;
                result.FormattedBody = GetFormattedBody(message.Content.MsgType)?.Body;
                result.ReplyPreview = ReplyPreview(msg.Content.InReplyTo);
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
                    sticker.Info.ThumbnailSource?.ToJson());
                return;

            case TimelineItemContent.RoomMembership membership:
                result.Body = $"{membership.UserDisplayName ?? membership.UserId} {MembershipText(membership.Change)}";
                return;

            case TimelineItemContent.ProfileChange profile:
                result.Body = ProfileText(profile);
                return;

            case TimelineItemContent.State state:
                result.Body = StateText(state.Content);
                result.IsUnknown = state.Content is OtherState.Custom;
                return;

            case TimelineItemContent.RtcNotification:
                result.Body = "Call notification";
                return;

            case TimelineItemContent.CallInvite:
                result.Body = "Incoming call";
                return;

            default:
                result.Body = "Unknown event";
                result.IsUnknown = true;
                return;
        }
    }

    private void PopulateReactions(
        ChatTimelineItem result,
        EventTimelineItem eventItem)
    {
        if (eventItem.Content is not TimelineItemContent.MsgLike message)
        {
            return;
        }

        foreach (var reaction in message.Content.Reactions)
        {
            result.Reactions.Add(new ChatReaction(
                reaction.Key,
                reaction.Senders.Length,
                reaction.Senders.Any(sender => sender.SenderId == _room.OwnUserId())));
        }
    }

    private static ChatMedia? CreateMedia(MessageType type) => type switch
    {
        MessageType.Image image => new ChatMedia(
            ChatMediaKind.Image,
            image.Content.Source.ToJson(),
            image.Content.Filename,
            image.Content.Info?.Mimetype,
            image.Content.Info?.ThumbnailSource?.ToJson()),
        MessageType.Video video => new ChatMedia(
            ChatMediaKind.Video,
            video.Content.Source.ToJson(),
            video.Content.Filename,
            video.Content.Info?.Mimetype,
            video.Content.Info?.ThumbnailSource?.ToJson()),
        MessageType.File file => new ChatMedia(
            ChatMediaKind.File,
            file.Content.Source.ToJson(),
            file.Content.Filename,
            file.Content.Info?.Mimetype),
        _ => null,
    };

    private async Task LoadMembersAsync()
    {
        try
        {
            using var iterator = await _room.Members();

            // ponytail: one page feeds typeahead; add query-backed search for very large rooms.
            if (iterator.NextChunk(100) is { Length: > 0 } members)
            {
                var joined = members.Where(member => member.Membership is MembershipState.Join).ToArray();
                RunOnCapturedContext(() =>
                {
                    foreach (var member in joined.Where(member =>
                                 Members.All(existing => existing.UserId != member.UserId)))
                    {
                        Members.Add(member);
                    }
                });
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load room members: {exception}");
        }
    }

    private RoomMessageEventContentWithoutRelation CreateMarkdownContent(string markdown)
    {
        var userIds = new HashSet<string>(StringComparer.Ordinal);
        var content = MentionPattern.Replace(markdown, match =>
        {
            var value = match.Groups["value"].Value;
            var member = Members.FirstOrDefault(candidate =>
                candidate.UserId == $"@{value}" ||
                string.Equals(candidate.DisplayName, value, StringComparison.OrdinalIgnoreCase));

            if (member is null)
            {
                return match.Value;
            }

            userIds.Add(member.UserId);
            var label = WebUtility.HtmlEncode(match.Value);
            return $"<a href=\"https://matrix.to/#/{member.UserId}\">{label}</a>";
        });

        content = EmotePattern.Replace(content, match =>
        {
            var emote = Emotes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, match.Groups["name"].Value,
                    StringComparison.OrdinalIgnoreCase));

            return emote is null
                ? match.Value
                : $"<img data-mx-emoticon src=\"{WebUtility.HtmlEncode(emote.Source)}\" alt=\"{WebUtility.HtmlEncode(match.Value)}\" title=\"{WebUtility.HtmlEncode(emote.Body)}\" height=\"32\" />";
        });

        var result = MatrixSdkFfiMethods.MessageEventContentFromMarkdown(content);
        if (userIds.Count == 0)
        {
            return result;
        }

        var mentioned = result.WithMentions(new Mentions(userIds.ToArray(), false));
        result.Dispose();
        return mentioned;
    }

    private static FormattedBody? GetFormattedBody(MessageType type) => type switch
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

    private static string MarkdownNode(XNode node) => node switch
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
            "blockquote" => string.Join('\n', content.TrimEnd().Split('\n').Select(line => $"> {line}")) + "\n",
            "a" when element.Attribute("href")?.Value.Contains("matrix.to/#/@", StringComparison.OrdinalIgnoreCase) == true => content,
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
        RegexOptions.Compiled);

    private static readonly Regex EmotePattern = new(
        @":(?<name>[\w+\-]+):",
        RegexOptions.Compiled);

    private static readonly Regex EditVoidTag = new(
        @"<(br|hr|img)(\s[^>]*?)?(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static MessageType CreateAttachment(
        string filename,
        string mimeType,
        ulong size,
        MediaSource source) => mimeType switch
    {
        _ when mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) =>
            new MessageType.Image(new ImageMessageContent(
                filename, null, null, source,
                new ImageInfo(null, null, mimeType, size, null, null, null, null))),
        _ when mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) =>
            new MessageType.Video(new VideoMessageContent(
                filename, null, null, source,
                new VideoInfo(null, null, null, mimeType, size, null, null, null))),
        _ => new MessageType.File(new FileMessageContent(
            filename, null, null, source,
            new uniffi.matrix_sdk_ffi.FileInfo(mimeType, size, null, null))),
    };

    private static string DisplayName(EventTimelineItem item) =>
        item.SenderProfile is ProfileDetails.Ready ready &&
        !string.IsNullOrWhiteSpace(ready.DisplayName)
            ? ready.DisplayName
            : item.Sender;

    private static string? AvatarUrl(EventTimelineItem item) =>
        item.SenderProfile is ProfileDetails.Ready ready
            ? ready.AvatarUrl
            : null;

    private static string EventOrTransactionIdValue(EventOrTransactionId id) => id switch
    {
        EventOrTransactionId.EventId eventId => eventId.EventIdValue,
        EventOrTransactionId.TransactionId transactionId => transactionId.TransactionIdValue,
        _ => string.Empty,
    };

    private static string MembershipText(MembershipChange? change) => change switch
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

    private static string StateText(OtherState state) => state switch
    {
        OtherState.RoomName { Name: { } name } => $"changed the room name to {name}",
        OtherState.RoomTopic { Topic: { } topic } => $"changed the room topic to {topic}",
        OtherState.RoomAvatar => "changed the room avatar",
        OtherState.RoomEncryption => "enabled encryption",
        OtherState.RoomCreate => "created the room",
        OtherState.Custom { EventType: { } type } => $"Unknown event: {type}",
        _ => "Room settings changed",
    };

    private static string? ReplyPreview(InReplyToDetails? reply)
    {
        if (reply is null)
        {
            return null;
        }

        using var details = reply.Event();
        return details is EmbeddedEventDetails.Ready
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
        try
        {
            using var document = JsonDocument.Parse(source ?? string.Empty);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(fallback, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
    }

    private void RunOnCapturedContext(System.Action action)
    {
        if (_synchronizationContext is null ||
            ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            action();
            return;
        }

        _synchronizationContext.Post(
            static state => ((System.Action)state!).Invoke(),
            action);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timeline.CollectionChanged -= OnTimelineChanged;
        ((INotifyPropertyChanged)_timeline).PropertyChanged -=
            OnTimelinePropertyChanged;
        _typing.PropertyChanged -= OnTypingChanged;
        _typing.Dispose();
        _timeline.Dispose();
        await Task.CompletedTask;
    }
}
