using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Plugin.Maui.Audio;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.ViewModels;

public sealed partial class ChatViewModel(
    MatrixService matrixService,
    AppNavigationService navigation,
    IAudioManager audioManager) : ObservableObject
{
    private const ushort PageSize = 25;
    private readonly List<MessageData?> _timelineItems = [];
    private readonly Dictionary<string, ChatMessage> _messageCache = [];
    private readonly Dictionary<string, MessageIdentifier> _messageIdentifiers = [];
    private readonly HashSet<string> _pendingMentionIds = [];
    private Timeline? _timeline;
    private Room? _room;
    private TaskHandle? _timelineListener;
    private TaskHandle? _typingListener;
    private TypingListener? _typingCallback;
    private string? _selectedRoomId;
    private int _timelineGeneration;
    private int _previewGeneration;
    private int _markingRead;
    private long _lastReadReceipt;
    private int _typingGeneration;
    private bool _typingSent;
    private IAudioRecorder? _audioRecorder;
    private readonly System.Diagnostics.Stopwatch _recordingTimer = new();
    private PaginationState _pagination = new();

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<RoomMemberItem> Members { get; } = [];
    public ObservableCollection<RoomMemberItem> MentionSuggestions { get; } = [];
    public ObservableCollection<MatrixImageAsset> CustomEmojis { get; } = [];
    public ObservableCollection<MatrixImageAsset> Stickers { get; } = [];
    public string[] ReactionChoices { get; } = ["👍", "❤️", "😂", "😮", "😢", "🎉", "🔥", "👀"];
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? RoomOpened;
    public event EventHandler? MessagesUpdated;
    public event EventHandler<RoomListItem>? OverviewRoomSelected;
    public event EventHandler<RoomListItem>? JoinRoomRequested;
    public event EventHandler<ModerationRequest>? ModerationRequested;
    public event EventHandler<ChatMessage>? RedactRequested;

    [ObservableProperty]
    public partial string Title { get; set; } = "Fennec";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickImageCommand))]
    public partial bool IsRoomSelected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string SendText { get; set; } = string.Empty;

    [ObservableProperty] public partial int ComposerCursorPosition { get; set; }
    [ObservableProperty] public partial int ComposerSelectionLength { get; set; }

    [ObservableProperty]
    public partial bool IsSpaceOverview { get; set; }

    [ObservableProperty]
    public partial RoomGroup? Space { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingOlder { get; set; }

    [ObservableProperty]
    public partial bool IsImagePreviewOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPreview { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickImageCommand))]
    public partial bool IsUploadingImage { get; set; }

    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    [ObservableProperty]
    public partial bool IsSourceInspectorOpen { get; set; }

    [ObservableProperty]
    public partial string InspectedEventType { get; set; } = string.Empty;

    public string InspectedSource { get => string.IsNullOrEmpty(_inspectedSource)? _inspectedSource : JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(_inspectedSource), new JsonSerializerOptions { WriteIndented = true }); set => SetProperty(ref _inspectedSource, value); }
    private string _inspectedSource = string.Empty;

    [ObservableProperty] public partial bool IsAttachmentMenuOpen { get; set; }
    [ObservableProperty] public partial bool IsEmojiPickerOpen { get; set; }
    [ObservableProperty] public partial bool IsStickerPickerOpen { get; set; }
    [ObservableProperty] public partial bool IsMemberListOpen { get; set; }
    [ObservableProperty] public partial bool IsProfileOpen { get; set; }
    [ObservableProperty] public partial bool IsRecording { get; set; }
    [ObservableProperty] public partial ChatMessage? ReplyingTo { get; set; }
    [ObservableProperty] public partial bool CanRedactSelectedMessage { get; set; }
    [ObservableProperty] public partial ChatMessage? SelectedMessage { get; set; }
    [ObservableProperty] public partial string TypingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProfileUserId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProfileDisplayName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProfileRole { get; set; } = string.Empty;
    [ObservableProperty] public partial ImageSource? ProfileAvatar { get; set; }
    [ObservableProperty] public partial bool CanKickProfile { get; set; }
    [ObservableProperty] public partial bool CanBanProfile { get; set; }
    [ObservableProperty] public partial bool IsMentionPickerOpen { get; set; }
    [ObservableProperty] public partial FormattedString? ComposerPreview { get; set; }
    [ObservableProperty] public partial bool HasComposerPreview { get; set; }

    public bool IsEmpty => !IsRoomSelected && !IsSpaceOverview;
    public bool IsReplying => ReplyingTo is not null;
    public string VoiceActionLabel => IsRecording ? "Send recording" : "Record voice";

    partial void OnIsRoomSelectedChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsSpaceOverviewChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnSendTextChanged(string value)
    {
        _ = UpdateTypingNoticeAsync(!string.IsNullOrWhiteSpace(value));
        var html = MessageFormatter.MarkdownToHtml(value) ?? WebUtility.HtmlEncode(value).Replace("\r\n", "<br>").Replace("\n", "<br>");
        ComposerPreview = string.IsNullOrWhiteSpace(value) ? null : MessageFormatter.HtmlToFormattedString(html);
        HasComposerPreview = ComposerPreview is not null;
        UpdateMentionSuggestions(value);
    }
    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(VoiceActionLabel));
    partial void OnReplyingToChanged(ChatMessage? value) => OnPropertyChanged(nameof(IsReplying));

    public void EnsureAuthenticated()
    {
        if (!matrixService.IsLoggedIn) navigation.ShowLogin();
    }

    public async Task OpenRoomAsync(RoomListItem roomItem)
    {
        if (_selectedRoomId == roomItem.RoomId ||
            matrixService.Client?.GetRoom(roomItem.RoomId) is not { } room)
            return;

        CloseImagePreview();
        CloseSourceInspector();
        var generation = ++_timelineGeneration;
        _pagination = new();
        _selectedRoomId = roomItem.RoomId;
        Title = roomItem.Name;
        IsSpaceOverview = false;
        Space = null;
        IsRoomSelected = true;
        RoomOpened?.Invoke(this, EventArgs.Empty);
        Messages.Clear();
        _messageCache.Clear();
        lock (_timelineItems) _timelineItems.Clear();
        _timelineListener?.Dispose();
        _timeline?.Dispose();
        _typingListener?.Dispose();
        _room?.Dispose();
        _room = room;

        try
        {
            _timeline = await room.Timeline();
            _timelineListener = await _timeline.AddListener(
                new ChatTimelineListener(diffs => ApplyTimelineDiffs(diffs, generation)));
            _typingCallback = new TypingListener(ids => UpdateTyping(ids, generation));
            _typingListener = room.SubscribeToTypingNotifications(_typingCallback);
            _ = FetchMembersAsync(_timeline);
            _ = LoadMembersAsync(room, generation);
            _ = LoadImagePackAsync(room.Id(), generation);
            await LoadOlderMessagesAsync();
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_room, room)) _room = null;
            room.Dispose();
            ErrorOccurred?.Invoke(this, exception.Message);
        }
    }

    public void CloseRoom()
    {
        CloseImagePreview();
        CloseSourceInspector();
        CloseTimeline();
        Messages.Clear();
        _messageCache.Clear();
        IsRoomSelected = false;
        IsSpaceOverview = false;
        Space = null;
        Title = "Fennec";
    }

    public void OpenSpace(RoomGroup space)
    {
        CloseImagePreview();
        CloseSourceInspector();
        CloseTimeline();
        Messages.Clear();
        _messageCache.Clear();
        Space = space;
        Title = space.Name;
        IsRoomSelected = false;
        IsSpaceOverview = true;
    }

    [RelayCommand]
    private void OpenOverviewRoom(RoomListItem room)
    {
        if (room.IsJoined) OverviewRoomSelected?.Invoke(this, room);
        else JoinRoomRequested?.Invoke(this, room);
    }

    public async Task JoinRoomAsync(RoomListItem room)
    {
        if (matrixService.Client is not { } client) return;
        using var joined = await client.JoinRoomById(room.RoomId);
        OverviewRoomSelected?.Invoke(this, new RoomListItem(
            room.RoomId, room.Name, room.AvatarUrl, room.Topic, true, room.MemberCount));
    }

    [RelayCommand]
    private void OpenSource(ChatMessage message)
    {
        CloseImagePreview();
        InspectedEventType = message.EventType;
        InspectedSource = message.Source;
        IsSourceInspectorOpen = true;
    }

    [RelayCommand]
    private async Task OpenMessageActionsAsync(ChatMessage? message)
    {
        if (message is null) return;
        if (SelectedMessage is { } previous) previous.IsActionsOpen = false;
        SelectedMessage = message;
        message.IsActionsOpen = true;
        CanRedactSelectedMessage = message.IsOwn;
        if (!message.IsOwn && _room is { } room)
        {
            try
            {
                using var power = await room.GetPowerLevels();
                if (SelectedMessage == message) CanRedactSelectedMessage = power.CanOwnUserRedactOther();
            }
            catch { }
        }
    }

    [RelayCommand]
    private void CloseMessageActions()
    {
        if (SelectedMessage is { } message) message.IsActionsOpen = false;
        SelectedMessage = null;
    }

    private void UpdateMentionSuggestions(string text)
    {
        var at = text.LastIndexOf('@');
        if (at < 0 || (at > 0 && !char.IsWhiteSpace(text[at - 1])))
        {
            IsMentionPickerOpen = false;
            return;
        }
        var query = text[(at + 1)..];
        if (query.Any(char.IsWhiteSpace))
        {
            IsMentionPickerOpen = false;
            return;
        }
        MentionSuggestions.Clear();
        foreach (var member in Members.Where(member =>
                     member.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     member.UserId.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(6))
            MentionSuggestions.Add(member);
        IsMentionPickerOpen = MentionSuggestions.Count > 0;
    }

    [RelayCommand]
    private void SelectMention(RoomMemberItem member)
    {
        var at = SendText.LastIndexOf('@');
        if (at < 0) return;
        _pendingMentionIds.Add(member.UserId);
        SendText = SendText[..at] + $"@{member.DisplayName} ";
        ComposerCursorPosition = SendText.Length;
        IsMentionPickerOpen = false;
    }

    [RelayCommand]
    private void ReplyToSelected()
    {
        if (SelectedMessage is not { IsMessage: true } message ||
            !_messageIdentifiers.TryGetValue(message.Id, out var identifier) || identifier.IsTransaction)
            return;
        ReplyingTo = message;
        CloseMessageActions();
    }

    [RelayCommand]
    private void CancelReply() => ReplyingTo = null;

    [RelayCommand]
    private void RequestRedact()
    {
        if (SelectedMessage is { } message && CanRedactSelectedMessage)
            RedactRequested?.Invoke(this, message);
    }

    public async Task RedactAsync(ChatMessage message)
    {
        if (_timeline is null || !_messageIdentifiers.TryGetValue(message.Id, out var identifier)) return;
        EventOrTransactionId eventId = identifier.IsTransaction
            ? new EventOrTransactionId.TransactionId(identifier.Value)
            : new EventOrTransactionId.EventId(identifier.Value);
        await _timeline.RedactEvent(eventId, null);
        CloseMessageActions();
    }

    [RelayCommand]
    private void ViewSelectedSource()
    {
        if (SelectedMessage is not { } message) return;
        CloseMessageActions();
        OpenSource(message);
    }

    [RelayCommand]
    private async Task ReactAsync(string emoji)
    {
        if (SelectedMessage is not { } message) return;
        await ToggleReactionAsync(message.Id, emoji);
        CloseMessageActions();
    }

    [RelayCommand]
    private Task ToggleReactionAsync(ChatReaction reaction) =>
        ToggleReactionAsync(reaction.MessageId, reaction.Key);

    private async Task ToggleReactionAsync(string messageId, string emoji)
    {
        if (_timeline is null || !_messageIdentifiers.TryGetValue(messageId, out var identifier)) return;
        try
        {
            EventOrTransactionId itemId = identifier.IsTransaction
                ? new EventOrTransactionId.TransactionId(identifier.Value)
                : new EventOrTransactionId.EventId(identifier.Value);
            await _timeline.ToggleReaction(itemId, emoji);
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
    }

    [RelayCommand]
    private void ToggleAttachments() => IsAttachmentMenuOpen = !IsAttachmentMenuOpen;

    [RelayCommand]
    private void ToggleEmojiPicker() => IsEmojiPickerOpen = !IsEmojiPickerOpen;

    [RelayCommand]
    private void InsertEmoji(string emoji)
    {
        SendText += emoji;
        IsEmojiPickerOpen = false;
    }

    [RelayCommand]
    private void FormatSelection(string format)
    {
        var start = Math.Clamp(ComposerCursorPosition, 0, SendText.Length);
        var length = Math.Clamp(ComposerSelectionLength, 0, SendText.Length - start);
        var selected = SendText.Substring(start, length);
        var (prefix, suffix) = format switch
        {
            "bold" => ("**", "**"),
            "italic" => ("*", "*"),
            "strike" => ("~~", "~~"),
            "code" => ("`", "`"),
            "link" => ("[", "](https://)"),
            "quote" => ("> ", ""),
            "list" => ("- ", ""),
            _ => ("", "")
        };
        SendText = SendText[..start] + prefix + selected + suffix + SendText[(start + length)..];
        ComposerCursorPosition = start + prefix.Length;
        ComposerSelectionLength = selected.Length;
    }

    [RelayCommand]
    private void InsertCustomEmoji(MatrixImageAsset emoji)
    {
        SendText += $":{emoji.Shortcode}:";
        IsEmojiPickerOpen = false;
    }

    [RelayCommand]
    private void ToggleStickerPicker() => IsStickerPickerOpen = !IsStickerPickerOpen;

    [RelayCommand]
    private async Task SendStickerAsync(MatrixImageAsset sticker)
    {
        if (_room is null) return;
        var content = JsonSerializer.Serialize(new
        {
            body = sticker.Body,
            url = sticker.Url,
            info = new { mimetype = "image/png" }
        });
        await _room.SendRaw("m.sticker", content);
        IsStickerPickerOpen = false;
        IsAttachmentMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleMemberList() => IsMemberListOpen = !IsMemberListOpen;

    [RelayCommand]
    private Task OpenProfileAsync(ChatMessage message) =>
        OpenProfileAsync(message.SenderId, message.SenderName, message.AvatarUrl, string.Empty);

    [RelayCommand]
    private Task OpenMemberProfileAsync(RoomMemberItem member) =>
        OpenProfileAsync(member.UserId, member.DisplayName, member.AvatarUrl, member.Role);

    private async Task OpenProfileAsync(string userId, string displayName, string? avatarUrl, string role)
    {
        CloseMessageActions();
        ProfileUserId = userId;
        ProfileDisplayName = displayName;
        ProfileRole = role;
        ProfileAvatar = null;
        IsProfileOpen = true;
        if (_room is { } room)
        {
            try
            {
                using var power = await room.GetPowerLevels();
                CanKickProfile = power.CanOwnUserKick() && userId != matrixService.Client?.UserId();
                CanBanProfile = power.CanOwnUserBan() && userId != matrixService.Client?.UserId();
            }
            catch
            {
                CanKickProfile = CanBanProfile = false;
            }
        }
        if (avatarUrl is null) return;
        try
        {
            var bytes = await matrixService.GetThumbnailAsync(avatarUrl, 160, 160);
            if (ProfileUserId == userId)
                ProfileAvatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch { }
    }

    [RelayCommand]
    private void CloseProfile() => IsProfileOpen = false;

    [RelayCommand]
    private void MentionProfile()
    {
        if (string.IsNullOrWhiteSpace(ProfileUserId)) return;
        _pendingMentionIds.Add(ProfileUserId);
        SendText += $"@{ProfileDisplayName} ";
        IsProfileOpen = false;
    }

    [RelayCommand]
    private void RequestKick() => ModerationRequested?.Invoke(this, new(ProfileUserId, ProfileDisplayName, false));

    [RelayCommand]
    private void RequestBan() => ModerationRequested?.Invoke(this, new(ProfileUserId, ProfileDisplayName, true));

    public async Task ModerateAsync(ModerationRequest request)
    {
        if (_room is null) return;
        if (request.Ban) await _room.BanUser(request.UserId, null);
        else await _room.KickUser(request.UserId, null);
        IsProfileOpen = false;
    }

    [RelayCommand]
    private void CloseSourceInspector()
    {
        IsSourceInspectorOpen = false;
        InspectedEventType = string.Empty;
        InspectedSource = string.Empty;
    }

    [RelayCommand]
    private Task CopySourceAsync() => Clipboard.Default.SetTextAsync(InspectedSource);

    [RelayCommand]
    private async Task OpenImagePreviewAsync(ChatMessage message)
    {
        if (message.ImageSourceJson is not { } sourceJson) return;

        CloseSourceInspector();
        var generation = ++_previewGeneration;
        PreviewImage = message.ContentImage;
        IsImagePreviewOpen = true;
        IsLoadingPreview = true;
        try
        {
            var bytes = await matrixService.GetMediaContentAsync(sourceJson);
            if (generation == _previewGeneration && IsImagePreviewOpen)
                PreviewImage = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception exception)
        {
            if (generation == _previewGeneration)
                ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            if (generation == _previewGeneration) IsLoadingPreview = false;
        }
    }

    [RelayCommand]
    private void CloseImagePreview()
    {
        ++_previewGeneration;
        IsImagePreviewOpen = false;
        IsLoadingPreview = false;
        PreviewImage = null;
#if DEBUG
        System.Diagnostics.Debug.Assert(!IsImagePreviewOpen && !IsLoadingPreview && PreviewImage is null);
#endif
    }

    private void CloseTimeline()
    {
        _ = StopRecordingAndDiscardAsync();
        ++_timelineGeneration;
        _pagination = new();
        ++_typingGeneration;
        _typingSent = false;
        TypingText = string.Empty;
        ReplyingTo = null;
        if (SelectedMessage is { } selected) selected.IsActionsOpen = false;
        SelectedMessage = null;
        IsLoadingOlder = false;
        _selectedRoomId = null;
        _timelineListener?.Dispose();
        _timelineListener = null;
        _timeline?.Dispose();
        _timeline = null;
        _typingListener?.Dispose();
        _typingListener = null;
        _typingCallback = null;
        _room?.Dispose();
        _room = null;
        Members.Clear();
        CustomEmojis.Clear();
        Stickers.Clear();
        _messageIdentifiers.Clear();
    }

    public async Task LoadOlderMessagesAsync()
    {
        var timeline = _timeline;
        var pagination = _pagination;
        var generation = _timelineGeneration;
        if (timeline is null || pagination.ReachedStart ||
            Interlocked.Exchange(ref pagination.IsLoading, 1) != 0)
            return;

#if DEBUG
        System.Diagnostics.Debug.Assert(Volatile.Read(ref pagination.IsLoading) == 1);
#endif
        IsLoadingOlder = true;
        try
        {
            var reachedStart = await timeline.PaginateBackwards(PageSize);
            if (generation == _timelineGeneration && ReferenceEquals(pagination, _pagination))
                pagination.ReachedStart = reachedStart;
        }
        catch (Exception exception)
        {
            if (generation == _timelineGeneration)
                ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref pagination.IsLoading, 0);
#if DEBUG
            System.Diagnostics.Debug.Assert(Volatile.Read(ref pagination.IsLoading) == 0);
#endif
            if (ReferenceEquals(pagination, _pagination)) IsLoadingOlder = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var message = SendText.Trim();
        if (_timeline is null || message.Length == 0) return;

        try
        {
            var html = MessageFormatter.MarkdownToHtml(message);
            foreach (var emoji in CustomEmojis.Where(emoji => message.Contains($":{emoji.Shortcode}:")))
            {
                html ??= WebUtility.HtmlEncode(message).Replace("\r\n", "<br>").Replace("\n", "<br>");
                var token = WebUtility.HtmlEncode($":{emoji.Shortcode}:");
                var alt = WebUtility.HtmlEncode(emoji.Body);
                var url = WebUtility.HtmlEncode(emoji.Url);
                html = html.Replace(token, $"<img data-mx-emoticon src=\"{url}\" alt=\"{alt}\" title=\"{alt}\" height=\"32\">");
            }
            var content = _timeline.CreateMessageContent(
                new MessageType.Text(new TextMessageContent(
                    message,
                    html is null ? null : new FormattedBody(new MessageFormat.Html(), html))))
                ?? throw new InvalidOperationException("Could not create the message.");
            if (_pendingMentionIds.Count > 0 || message.Contains("@room", StringComparison.OrdinalIgnoreCase))
            {
                var withMentions = content.WithMentions(new Mentions(
                    _pendingMentionIds.ToArray(),
                    message.Contains("@room", StringComparison.OrdinalIgnoreCase)));
                content.Dispose();
                content = withMentions;
            }
            using (content)
            {
                if (ReplyingTo is { } reply &&
                    _messageIdentifiers.TryGetValue(reply.Id, out var replyIdentifier) &&
                    !replyIdentifier.IsTransaction)
                    await _timeline.SendReply(content, replyIdentifier.Value);
                else
                    using (var send = await _timeline.Send(content)) { }
            }
            _pendingMentionIds.Clear();
            ReplyingTo = null;
            SendText = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
    }

    private bool CanSend() => IsRoomSelected && !string.IsNullOrWhiteSpace(SendText);

    private async Task UpdateTypingNoticeAsync(bool isTyping)
    {
        var room = _room;
        var generation = ++_typingGeneration;
        if (room is null) return;
        try
        {
            if (isTyping && !_typingSent)
            {
                await room.TypingNotice(true);
                _typingSent = true;
            }
            if (!isTyping && _typingSent)
            {
                await room.TypingNotice(false);
                _typingSent = false;
                return;
            }
            if (!isTyping) return;
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (generation == _typingGeneration && _typingSent)
            {
                await room.TypingNotice(false);
                _typingSent = false;
            }
        }
        catch
        {
            // Typing notifications are ephemeral and best-effort.
        }
    }

    [RelayCommand(CanExecute = nameof(CanPickImage))]
    private async Task PickImageAsync()
    {
        var timeline = _timeline;
        if (timeline is null) return;
        IsUploadingImage = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Send an image",
                FileTypes = FilePickerFileType.Images
            });
            if (file is null || !ReferenceEquals(timeline, _timeline)) return;

            await using var stream = await file.OpenReadAsync();
            var bytes = await ReadImageAsync(stream);
            using var image = PlatformImage.FromStream(
                new MemoryStream(bytes), ImageFormatFor(file.ContentType, file.FileName));
            if (!float.IsFinite(image.Width) || !float.IsFinite(image.Height) ||
                image.Width <= 0 || image.Height <= 0)
                throw new InvalidOperationException("The selected file is not a valid image.");
            var caption = string.IsNullOrWhiteSpace(SendText) ? null : SendText.Trim();
            var html = caption is null ? null : MessageFormatter.MarkdownToHtml(caption);
            var parameters = new UploadParameters(
                new UploadSource.Data(bytes, file.FileName),
                caption,
                html is null ? null : new FormattedBody(new MessageFormat.Html(), html),
                null,
                null);
            using var info = new ImageInfo(
                (ulong)Math.Max(1, Math.Round(image.Height)),
                (ulong)Math.Max(1, Math.Round(image.Width)),
                file.ContentType,
                (ulong)bytes.LongLength,
                null,
                null,
                // ponytail: neutral placeholder; generate a content-derived blurhash if previews need it.
                "LEHV6nWB2yk8pyo0adR*.7kCMdnj",
                null);
            using var send = timeline.SendImage(parameters, null, info);
            await send.Join();
            SendText = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    private bool CanPickImage() => IsRoomSelected && !IsUploadingImage;

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Send a file" });
        if (file is not null) await SendFileAsync(file);
    }

    [RelayCommand]
    private async Task PickVideoAsync()
    {
        var file = (await MediaPicker.Default.PickVideosAsync()).FirstOrDefault();
        if (file is not null) await SendFileAsync(file);
    }

    [RelayCommand]
    private async Task RecordVoiceAsync()
    {
        try
        {
            if (IsRecording)
            {
                await StopAndSendVoiceAsync();
                return;
            }

            if (!IsRoomSelected || _timeline is null) return;
            var permission = await Permissions.RequestAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
                throw new InvalidOperationException("Microphone access is required to record a voice message.");

            _audioRecorder = audioManager.CreateRecorder();
            if (!_audioRecorder.CanRecordAudio)
                throw new InvalidOperationException("Audio recording is not supported on this device.");

            await _audioRecorder.StartAsync();
            _recordingTimer.Restart();
            IsRecording = true;
        }
        catch (Exception exception)
        {
            _audioRecorder = null;
            IsRecording = false;
            ErrorOccurred?.Invoke(this, exception.Message);
        }
    }

    private async Task StopAndSendVoiceAsync()
    {
        var recorder = _audioRecorder;
        var timeline = _timeline;
        if (recorder is null || timeline is null) return;

        _audioRecorder = null;
        IsRecording = false;
        _recordingTimer.Stop();
        var duration = _recordingTimer.Elapsed;
        IAudioSource? source = null;
        string? temporaryPath = null;
        IsUploadingImage = true;
        try
        {
            source = await recorder.StopAsync();
            if (source is null) throw new InvalidOperationException("The recording could not be read.");
            temporaryPath = (source as FileAudioSource)?.GetFilePath();
            await using var stream = source.GetAudioStream();
            var bytes = await ReadAttachmentAsync(stream);
            var extension = Path.GetExtension(temporaryPath);
            var fileName = $"voice-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{(string.IsNullOrWhiteSpace(extension) ? ".wav" : extension)}";
            var mimeType = extension?.Equals(".m4a", StringComparison.OrdinalIgnoreCase) == true ? "audio/mp4" : "audio/wav";
            var parameters = new UploadParameters(new UploadSource.Data(bytes, fileName), null, null, null, null);
            var info = new AudioInfo(duration, (ulong)bytes.LongLength, mimeType);
            using var send = timeline.SendVoiceMessage(parameters, info, BuildWaveform(bytes));
            await send.Join();
            IsAttachmentMenuOpen = false;
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            IsUploadingImage = false;
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task StopRecordingAndDiscardAsync()
    {
        var recorder = _audioRecorder;
        if (recorder is null) return;
        _audioRecorder = null;
        IsRecording = false;
        _recordingTimer.Reset();
        try
        {
            var source = await recorder.StopAsync();
            var temporaryPath = (source as FileAudioSource)?.GetFilePath();
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch
        {
        }
    }

    private static float[] BuildWaveform(byte[] bytes)
    {
        const int bars = 30;
        var waveform = new float[bars];
        if (bytes.Length <= 44) return waveform;
        var samplesPerBar = Math.Max(1, (bytes.Length - 44) / 2 / bars);
        for (var bar = 0; bar < bars; bar++)
        {
            var firstSample = bar * samplesPerBar;
            var lastSample = Math.Min((bytes.Length - 44) / 2, firstSample + samplesPerBar);
            var peak = 0;
            for (var sample = firstSample; sample < lastSample; sample++)
            {
                var offset = 44 + sample * 2;
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(bytes, offset)));
            }
            waveform[bar] = peak / 32768f;
        }
        return waveform;
    }

    private async Task SendFileAsync(FileResult file)
    {
        var timeline = _timeline;
        if (timeline is null) return;
        IsUploadingImage = true;
        try
        {
            await using var stream = await file.OpenReadAsync();
            var bytes = await ReadAttachmentAsync(stream);
            var parameters = new UploadParameters(new UploadSource.Data(bytes, file.FileName), null, null, null, null);
            using var info = new uniffi.matrix_sdk_ffi.FileInfo(file.ContentType, (ulong)bytes.LongLength, null, null);
            using var send = timeline.SendFile(parameters, info);
            await send.Join();
            IsAttachmentMenuOpen = false;
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
        finally
        {
            IsUploadingImage = false;
        }
    }

    private static async Task<byte[]> ReadAttachmentAsync(Stream stream)
    {
        const int maximumBytes = 100 * 1024 * 1024;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException("Attachments must be 100 MB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    private static ImageFormat ImageFormatFor(string? contentType, string filename) =>
        (contentType ?? Path.GetExtension(filename).ToLowerInvariant()) switch
        {
            "image/jpeg" or ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            "image/gif" or ".gif" => ImageFormat.Gif,
            "image/tiff" or ".tif" or ".tiff" => ImageFormat.Tiff,
            "image/bmp" or ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };

    private static async Task<byte[]> ReadImageAsync(Stream stream)
    {
        const int maximumBytes = 25 * 1024 * 1024;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException("Images must be 25 MB or smaller.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return output.ToArray();
    }

    private static async Task FetchMembersAsync(Timeline timeline)
    {
        try { await timeline.FetchMembers(); }
        catch { }
    }

    private void ApplyTimelineDiffs(TimelineDiff[] diffs, int generation)
    {
        MessageData[] snapshot;
        lock (_timelineItems)
        {
            if (generation != _timelineGeneration) return;

            foreach (var diff in diffs)
            {
                switch (diff)
                {
                    case TimelineDiff.Append append:
                        _timelineItems.AddRange(append.Values.Select(ToMessageData));
                        break;
                    case TimelineDiff.Clear:
                        _timelineItems.Clear();
                        break;
                    case TimelineDiff.PushFront push:
                        _timelineItems.Insert(0, ToMessageData(push.Value));
                        break;
                    case TimelineDiff.PushBack push:
                        _timelineItems.Add(ToMessageData(push.Value));
                        break;
                    case TimelineDiff.PopFront when _timelineItems.Count > 0:
                        _timelineItems.RemoveAt(0);
                        break;
                    case TimelineDiff.PopBack when _timelineItems.Count > 0:
                        _timelineItems.RemoveAt(_timelineItems.Count - 1);
                        break;
                    case TimelineDiff.Insert insert:
                        _timelineItems.Insert((int)insert.Index, ToMessageData(insert.Value));
                        break;
                    case TimelineDiff.Set set:
                        _timelineItems[(int)set.Index] = ToMessageData(set.Value);
                        break;
                    case TimelineDiff.Remove remove:
                        _timelineItems.RemoveAt((int)remove.Index);
                        break;
                    case TimelineDiff.Truncate truncate:
                        _timelineItems.RemoveRange((int)truncate.Length, _timelineItems.Count - (int)truncate.Length);
                        break;
                    case TimelineDiff.Reset reset:
                        _timelineItems.Clear();
                        _timelineItems.AddRange(reset.Values.Select(ToMessageData));
                        break;
                }
            }
            snapshot = _timelineItems.OfType<MessageData>().ToArray();
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (generation != _timelineGeneration) return;
            var next = snapshot.Select(GetOrUpdateMessage).ToArray();
            ApplyMessageGrouping(next);
            ReconcileMessages(next);
            MessagesUpdated?.Invoke(this, EventArgs.Empty);
            foreach (var message in next)
            {
                if (message.AvatarUrl is not null && message.TryStartAvatarLoad())
                    _ = LoadAvatarAsync(message);
                if (message.ImageSourceJson is not null && message.TryStartContentLoad())
                    _ = LoadContentImageAsync(message);
                if (message.HasPlayableMedia && message.TryStartMediaLoad())
                    _ = LoadPlayableMediaAsync(message);
            }

            var liveIds = next.Select(message => message.Id).ToHashSet();
            foreach (var id in _messageCache.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                _messageCache.Remove(id);
                _messageIdentifiers.Remove(id);
            }
        });
    }

    private ChatMessage GetOrUpdateMessage(MessageData data)
    {
        if (_messageCache.TryGetValue(data.Id, out var message))
        {
            message.Update(
                data.SenderName,
                data.SenderId,
                data.Body,
                data.Time,
                data.AvatarUrl,
                data.IsOwn,
                data.ImageSourceJson,
                data.FormattedHtml,
                data.SendState,
                data.EventType,
                data.Source,
                data.IsNonMessageEvent);
            message.UpdateReactions(data.Reactions.Select(reaction => new ChatReaction(
                data.Id, reaction.Key, reaction.Count, reaction.IsOwn)));
            message.ReadReceiptText = data.ReadReceiptText;
            message.UpdateMedia(data.MediaSourceJson, data.MediaKind, data.MediaFileName);
            _messageIdentifiers[data.Id] = data.Identifier;
            return message;
        }

        message = new ChatMessage(
            data.Id,
            data.SenderName,
            data.SenderId,
            data.Body,
            data.Time,
            data.AvatarUrl,
            data.IsOwn,
            data.ImageSourceJson,
            data.FormattedHtml,
            data.SendState,
            data.EventType,
            data.Source,
            data.IsNonMessageEvent);
        message.UpdateReactions(data.Reactions.Select(reaction => new ChatReaction(
            data.Id, reaction.Key, reaction.Count, reaction.IsOwn)));
        message.ReadReceiptText = data.ReadReceiptText;
        message.UpdateMedia(data.MediaSourceJson, data.MediaKind, data.MediaFileName);
        _messageCache[data.Id] = message;
        _messageIdentifiers[data.Id] = data.Identifier;
        return message;
    }

    private void ReconcileMessages(ChatMessage[] next)
    {
        var prefix = 0;
        while (prefix < Messages.Count && prefix < next.Length && ReferenceEquals(Messages[prefix], next[prefix]))
            prefix++;

        var suffix = 0;
        while (suffix < Messages.Count - prefix && suffix < next.Length - prefix &&
               ReferenceEquals(Messages[Messages.Count - 1 - suffix], next[next.Length - 1 - suffix]))
            suffix++;

        for (var index = Messages.Count - suffix - 1; index >= prefix; index--)
            Messages.RemoveAt(index);
        for (var index = prefix; index < next.Length - suffix; index++)
            Messages.Insert(index, next[index]);

#if DEBUG
        System.Diagnostics.Debug.Assert(Messages.SequenceEqual(next));
#endif
    }

    private static void ApplyMessageGrouping(ChatMessage[] messages)
    {
        for (var index = 0; index < messages.Length; index++)
        {
            var message = messages[index];
            message.SetGrouping(
                index > 0 && message.CanGroupWith(messages[index - 1]),
                index + 1 < messages.Length && message.CanGroupWith(messages[index + 1]));
        }
    }

    private MessageData? ToMessageData(TimelineItem item)
    {
        var id = item.UniqueId().Id;
        using var value = item.AsEvent();
        if (value is null)
            return null;

        var eventType = value.EventTypeRaw ?? EventTypeFor(value.Content);
        var source = EventSource(value, eventType);
        var ownUserId = matrixService.Client?.UserId();
        var reactions = value.Content is TimelineItemContent.MsgLike messageLike
            ? messageLike.Content.Reactions.Select(reaction => new ReactionData(
                reaction.Key,
                reaction.Senders.Length,
                reaction.Senders.Any(sender => sender.SenderId == ownUserId))).ToArray()
            : [];
        var identifier = value.EventOrTransactionId switch
        {
            EventOrTransactionId.TransactionId transaction => new MessageIdentifier(true, transaction.TransactionIdValue),
            EventOrTransactionId.EventId remote => new MessageIdentifier(false, remote.EventIdValue),
            _ => new MessageIdentifier(false, id)
        };
        var isNonMessageEvent = true;
        string body;
        string? imageSourceJson = null;
        string? mediaSourceJson = null;
        string? mediaKind = null;
        string? mediaFileName = null;
        string? formattedHtml = null;
        switch (value.Content)
        {
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Message { Content.MsgType: MessageType.Image image } }:
                body = image.Content.Caption ?? string.Empty;
                imageSourceJson = image.Content.Source.ToJson();
                formattedHtml = image.Content.FormattedCaption?.Body;
                isNonMessageEvent = false;
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Message message }:
                body = message.Content.Body;
                (mediaSourceJson, mediaKind, mediaFileName) = message.Content.MsgType switch
                {
                    MessageType.Audio audio => (audio.Content.Source.ToJson(), "audio", audio.Content.Filename),
                    MessageType.Video video => (video.Content.Source.ToJson(), "video", video.Content.Filename),
                    MessageType.File file => (
                        file.Content.Source.ToJson(),
                        file.Content.Info?.Mimetype is { } mime && mime.StartsWith("audio/") ? "audio" :
                        file.Content.Info?.Mimetype is { } videoMime && videoMime.StartsWith("video/") ? "video" : "file",
                        file.Content.Filename),
                    _ => (null, null, null)
                };
                formattedHtml = message.Content.MsgType switch
                {
                    MessageType.Text text => text.Content.Formatted?.Body,
                    MessageType.Notice notice => notice.Content.Formatted?.Body,
                    MessageType.Emote emote => emote.Content.Formatted?.Body,
                    _ => null
                };
                isNonMessageEvent = false;
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Sticker sticker }:
                body = sticker.Body;
                imageSourceJson = sticker.Source.ToJson();
                isNonMessageEvent = false;
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.UnableToDecrypt }:
                body = "Encrypted message — this session has not yet been verified.";
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Poll poll }:
                body = $"Poll: {poll.Question}";
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Redacted }:
                body = "This message was redacted.";
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.LiveLocation }:
                body = "Live location shared.";
                break;
            case TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Other }:
                body = "Custom Matrix event.";
                break;
            case TimelineItemContent.RoomMembership membership:
                var member = membership.UserDisplayName ?? membership.UserId;
                body = membership.Change is { } change
                    ? $"{member}: {Humanize(change.ToString())}."
                    : $"Membership updated for {member}.";
                if (!string.IsNullOrWhiteSpace(membership.Reason)) body += $" {membership.Reason}";
                break;
            case TimelineItemContent.ProfileChange profileChange:
                body = ProfileChangeSummary(profileChange);
                break;
            case TimelineItemContent.State state:
                body = $"{Humanize(state.Content.GetType().Name)} changed" +
                       (string.IsNullOrEmpty(state.StateKey) ? "." : $" for {state.StateKey}.");
                break;
            case TimelineItemContent.CallInvite:
                body = "Incoming call invitation.";
                break;
            case TimelineItemContent.RtcNotification rtc:
                body = string.IsNullOrWhiteSpace(rtc.CallIntent)
                    ? "Call notification."
                    : $"Call notification: {rtc.CallIntent}.";
                break;
            case TimelineItemContent.FailedToParseMessageLike failed:
                body = $"Could not parse {failed.EventType}: {failed.Error}";
                break;
            case TimelineItemContent.FailedToParseState failed:
                body = $"Could not parse {failed.EventType}: {failed.Error}";
                break;
            default:
                body = $"{Humanize(value.Content.GetType().Name)} event.";
                break;
        }

        var profile = value.SenderProfile as ProfileDetails.Ready;
        return new MessageData(
            id,
            profile?.DisplayName ?? value.Sender,
            value.Sender,
            body,
            DateTimeOffset.FromUnixTimeMilliseconds((long)value.Timestamp).ToLocalTime().ToString("HH:mm"),
            profile?.AvatarUrl,
            value.IsOwn,
            imageSourceJson,
            formattedHtml,
            value.LocalSendState switch
            {
                EventSendState.NotSentYet => "Sending…",
                EventSendState.SendingFailed => "Failed to send",
                _ => null
            },
            eventType,
            source,
            isNonMessageEvent,
            identifier,
            reactions,
            value.IsOwn && value.ReadReceipts.Count > 0 ? $"✓✓ {value.ReadReceipts.Count}" : string.Empty,
            mediaSourceJson,
            mediaKind,
            mediaFileName);
    }

    private static string EventTypeFor(TimelineItemContent content) => content switch
    {
        TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.UnableToDecrypt } => "m.room.encrypted",
        TimelineItemContent.RoomMembership => "m.room.member",
        TimelineItemContent.ProfileChange => "m.room.member",
        TimelineItemContent.CallInvite => "m.call.invite",
        _ => Humanize(content.GetType().Name)
    };

    private static string EventSource(EventTimelineItem item, string eventType)
    {
        try
        {
            var latest = item.LazyProvider.LatestJson();
            if (!string.IsNullOrWhiteSpace(latest)) return latest;
            var debug = item.LazyProvider.DebugInfo();
            return debug.OriginalJson ?? debug.Model;
        }
        catch
        {
            return $"Source unavailable for {eventType}.";
        }
    }

    private static string ProfileChangeSummary(TimelineItemContent.ProfileChange profile)
    {
        if (profile.DisplayName != profile.PrevDisplayName)
            return $"Display name changed from {profile.PrevDisplayName ?? "unset"} to {profile.DisplayName ?? "unset"}.";
        if (profile.AvatarUrl != profile.PrevAvatarUrl)
            return "Profile picture changed.";
        return "Profile updated.";
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Matrix";
        return string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()));
    }

    private async Task LoadMembersAsync(Room room, int generation)
    {
        try
        {
            using var iterator = await room.Members();
            var items = new List<RoomMemberItem>();
            while (iterator.NextChunk(100) is { Length: > 0 } chunk)
            {
                items.AddRange(chunk
                    .Where(member => !member.IsServiceMember)
                    .Select(member => new RoomMemberItem(
                        member.UserId,
                        member.DisplayName ?? member.UserId,
                        member.AvatarUrl,
                        member.SuggestedRoleForPowerLevel.ToString())));
            }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != _timelineGeneration) return;
                Members.Clear();
                foreach (var item in items.OrderBy(item => item.DisplayName))
                {
                    Members.Add(item);
                    if (item.AvatarUrl is not null) _ = LoadMemberAvatarAsync(item);
                }
            });
        }
        catch
        {
            // Member sync may still be warming up; the timeline remains usable.
        }
    }

    private async Task LoadImagePackAsync(string roomId, int generation)
    {
        var assets = await matrixService.GetRoomImagePackAsync(roomId);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (generation != _timelineGeneration) return;
            CustomEmojis.Clear();
            Stickers.Clear();
            foreach (var asset in assets)
            {
                if (asset.IsEmoji) CustomEmojis.Add(asset);
                if (asset.IsSticker) Stickers.Add(asset);
                _ = LoadImageAssetAsync(asset);
            }
        });
    }

    private async Task LoadImageAssetAsync(MatrixImageAsset asset)
    {
        try
        {
            var bytes = await matrixService.GetThumbnailAsync(asset.Url, 96, 96);
            asset.Image = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch { }
    }

    private async Task LoadMemberAvatarAsync(RoomMemberItem member)
    {
        try
        {
            var bytes = await matrixService.GetThumbnailAsync(member.AvatarUrl!, 64, 64);
            member.Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch { }
    }

    private void UpdateTyping(string[] userIds, int generation)
    {
        var ownUserId = matrixService.Client?.UserId();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (generation != _timelineGeneration) return;
            var names = userIds
                .Where(id => id != ownUserId)
                .Select(id => Members.FirstOrDefault(member => member.UserId == id)?.DisplayName ?? id)
                .Take(3)
                .ToArray();
            TypingText = names.Length switch
            {
                0 => string.Empty,
                1 => $"{names[0]} is typing…",
                _ => $"{string.Join(", ", names)} are typing…"
            };
        });
    }

    private async Task LoadAvatarAsync(ChatMessage message)
    {
        try
        {
            var bytes = await matrixService.GetThumbnailAsync(message.AvatarUrl!, 64, 64);
            message.Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
        }
    }

    private async Task LoadContentImageAsync(ChatMessage message)
    {
        try
        {
            var bytes = await matrixService.GetMediaThumbnailAsync(message.ImageSourceJson!, 800, 600);
            message.ContentImage = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
        }
    }

    private async Task LoadPlayableMediaAsync(ChatMessage message)
    {
        try
        {
            var path = await GetMediaFileAsync(message);
            var source = new Uri(path).AbsoluteUri;
            var tag = message.MediaKind == "video" ? "video" : "audio";
            message.MediaPlayerSource = new HtmlWebViewSource
            {
                Html = $"<html><meta name=\"viewport\" content=\"width=device-width\"><body style=\"margin:0;background:transparent\"><{tag} controls preload=\"metadata\" style=\"width:100%;height:100%;border-radius:12px\" src=\"{WebUtility.HtmlEncode(source)}\"></{tag}></body></html>"
            };
        }
        catch { }
    }

    [RelayCommand]
    private async Task OpenAttachmentAsync(ChatMessage message)
    {
        try
        {
            var path = await GetMediaFileAsync(message);
            await Launcher.Default.OpenAsync(new OpenFileRequest(
                message.MediaFileName ?? "Attachment",
                new ReadOnlyFile(path)));
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception.Message);
        }
    }

    private async Task<string> GetMediaFileAsync(ChatMessage message)
    {
        var source = message.MediaSourceJson ?? throw new InvalidOperationException("Attachment source is missing.");
        var extension = Path.GetExtension(message.MediaFileName);
        var name = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))) + extension;
        var path = Path.Combine(FileSystem.CacheDirectory, name);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, await matrixService.GetMediaContentAsync(source));
        return path;
    }

    private sealed record MessageData(
        string Id,
        string SenderName,
        string SenderId,
        string Body,
        string Time,
        string? AvatarUrl,
        bool IsOwn,
        string? ImageSourceJson,
        string? FormattedHtml,
        string? SendState,
        string EventType,
        string Source,
        bool IsNonMessageEvent,
        MessageIdentifier Identifier,
        ReactionData[] Reactions,
        string ReadReceiptText,
        string? MediaSourceJson,
        string? MediaKind,
        string? MediaFileName);

    private sealed record MessageIdentifier(bool IsTransaction, string Value);
    private sealed record ReactionData(string Key, int Count, bool IsOwn);

    public async Task MarkAsReadAsync()
    {
        var timeline = _timeline;
        var now = Environment.TickCount64;
        if (timeline is null || now - Interlocked.Read(ref _lastReadReceipt) < 2000 ||
            Interlocked.Exchange(ref _markingRead, 1) != 0)
            return;

        try
        {
            await timeline.MarkAsRead(ReceiptType.Read);
            Interlocked.Exchange(ref _lastReadReceipt, now);
        }
        catch
        {
            // Read receipts are best-effort and should never interrupt chat.
        }
        finally
        {
            Interlocked.Exchange(ref _markingRead, 0);
        }
    }

    private sealed class PaginationState
    {
        public int IsLoading;
        public bool ReachedStart;
    }
}

internal sealed class ChatTimelineListener(Action<TimelineDiff[]> update) : TimelineListener
{
    public void OnUpdate(TimelineDiff[] diff) => update(diff);
}

internal sealed class TypingListener(Action<string[]> update) : TypingNotificationsListener
{
    public void Call(string[] typingUserIds) => update(typingUserIds);
}

public sealed record ModerationRequest(string UserId, string DisplayName, bool Ban);
