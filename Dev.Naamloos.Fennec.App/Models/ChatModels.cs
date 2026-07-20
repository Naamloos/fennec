using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dev.Naamloos.Fennec.App.Models;

public sealed record MatrixInstance(string Name, string Url, string ServerName)
{
    public override string ToString() => Name;
}

public sealed partial class RoomListItem(
    string roomId,
    string name,
    string? avatarUrl = null,
    string? topic = null,
    bool isJoined = true,
    ulong memberCount = 0) : ObservableObject
{
    public string RoomId { get; } = roomId;
    public string Name { get; } = name;
    public string? AvatarUrl { get; } = avatarUrl;
    public string? Topic { get; } = topic;
    public bool IsJoined { get; } = isJoined;
    public bool IsJoinable => !IsJoined;
    public ulong MemberCount { get; } = memberCount;
    public string Initials { get; } = Initial(name);

    [ObservableProperty]
    public partial ImageSource? Avatar { get; set; }

    internal static string Initial(string value) =>
        (value.FirstOrDefault(char.IsLetterOrDigit) is var initial && initial != default ? initial : '?')
        .ToString().ToUpperInvariant();
}

public sealed class RoomGroup : ObservableCollection<RoomListItem>
{
    private ImageSource? _avatar;

    public RoomGroup(
        string key,
        string name,
        string? avatarUrl,
        bool expanded,
        bool isSpace = false,
        Action<RoomGroup>? open = null)
    {
        Key = key;
        Name = name;
        AvatarUrl = avatarUrl;
        Initials = RoomListItem.Initial(name);
        IsExpanded = expanded;
        IsSpace = isSpace;
        ToggleCommand = new RelayCommand(Toggle);
        OpenCommand = new RelayCommand(() =>
        {
            if (IsSpace) open?.Invoke(this);
            else Toggle();
        });
    }

    public string Key { get; }
    public string Name { get; }
    public string? Description { get; internal set; }
    public string? AvatarUrl { get; }
    public string Initials { get; }
    public List<RoomListItem> AllRooms { get; } = [];
    public bool IsExpanded { get; private set; }
    public bool IsSpace { get; }
    public double ChevronRotation => IsExpanded ? 0 : -90;
    public IRelayCommand ToggleCommand { get; }
    public IRelayCommand OpenCommand { get; }

    public ImageSource? Avatar
    {
        get => _avatar;
        set
        {
            if (_avatar == value) return;
            _avatar = value;
            OnPropertyChanged(new(nameof(Avatar)));
        }
    }

    public void AddRoom(RoomListItem room, bool includeInSidebar = true)
    {
        AllRooms.Add(room);
        if (includeInSidebar && IsExpanded) Add(room);
    }

    public void Toggle()
    {
        IsExpanded = !IsExpanded;
        Clear();
        if (IsExpanded)
            foreach (var room in AllRooms.Where(room => room.IsJoined)) Add(room);
        OnPropertyChanged(new(nameof(ChevronRotation)));
    }
}

public sealed partial class ChatMessage : ObservableObject
{
    private int _avatarLoadStarted;
    private int _contentLoadStarted;
    private int _mediaLoadStarted;
    private string _senderId = string.Empty;
    private string? _avatarUrl;
    private string? _formattedHtml;

    public ChatMessage(
        string id,
        string senderName,
        string senderId,
        string body,
        string time,
        string? avatarUrl,
        bool isOwn,
        string? imageSourceJson,
        string? formattedHtml,
        string? sendState,
        string eventType,
        string source,
        bool isNonMessageEvent)
    {
        Id = id;
        Update(senderName, senderId, body, time, avatarUrl, isOwn, imageSourceJson, formattedHtml,
            sendState, eventType, source, isNonMessageEvent);
    }

    public string Id { get; }
    public string SenderId => _senderId;
    public string? AvatarUrl => _avatarUrl;
    public string? ImageSourceJson { get; private set; }
    public string? MediaSourceJson { get; private set; }
    public string? MediaFileName { get; private set; }
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public bool HasFormattedBody => FormattedBody is not null;
    public bool HasPlainBody => HasBody && !HasFormattedBody;
    public bool HasSendState => !string.IsNullOrWhiteSpace(SendState);
    public bool HasImage => ImageSourceJson is not null;
    public bool HasEventType => IsNonMessageEvent && !string.IsNullOrWhiteSpace(EventType);
    public bool IsMessage => !IsNonMessageEvent;
    public bool HasReactions => Reactions.Count > 0;
    public bool HasReadReceipts => !string.IsNullOrWhiteSpace(ReadReceiptText);
    public bool HasPlayableMedia => MediaKind is "audio" or "video";
    public bool HasFileAttachment => MediaSourceJson is not null && !HasPlayableMedia;
    public double MediaPlayerHeight => MediaKind == "video" ? 260 : 72;
    public ObservableCollection<ChatReaction> Reactions { get; } = [];
    public int AvatarColumn => IsOwn ? 2 : 0;
    public LayoutOptions MessageAlignment => IsOwn ? LayoutOptions.End : LayoutOptions.Start;
    public TextAlignment TextAlignment => IsOwn ? TextAlignment.End : TextAlignment.Start;
    public string Initials =>
        (SenderName.FirstOrDefault(char.IsLetterOrDigit) is var initial && initial != default
            ? initial
            : _senderId.TrimStart('@').FirstOrDefault()).ToString().ToUpperInvariant();

    [ObservableProperty]
    public partial string SenderName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Time { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOwn { get; set; }

    [ObservableProperty]
    public partial ImageSource? Avatar { get; set; }

    [ObservableProperty]
    public partial ImageSource? ContentImage { get; set; }

    [ObservableProperty]
    public partial FormattedString? FormattedBody { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSendState))]
    public partial string? SendState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventType))]
    public partial string EventType { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Source { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEventType))]
    [NotifyPropertyChangedFor(nameof(IsMessage))]
    public partial bool IsNonMessageEvent { get; set; }

    [ObservableProperty]
    public partial bool ShowAuthor { get; set; } = true;

    [ObservableProperty]
    public partial Thickness RowPadding { get; set; } = new(24, 8);

    [ObservableProperty]
    public partial CornerRadius BubbleCorners { get; set; } = new(18);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReadReceipts))]
    public partial string ReadReceiptText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? MediaKind { get; set; }

    [ObservableProperty]
    public partial HtmlWebViewSource? MediaPlayerSource { get; set; }

    [ObservableProperty]
    public partial bool IsActionsOpen { get; set; }

    public void Update(
        string senderName,
        string senderId,
        string body,
        string time,
        string? avatarUrl,
        bool isOwn,
        string? imageSourceJson,
        string? formattedHtml,
        string? sendState,
        string eventType,
        string source,
        bool isNonMessageEvent)
    {
        var senderChanged = SenderName != senderName || _senderId != senderId;
        var bodyVisibilityChanged = HasBody != !string.IsNullOrWhiteSpace(body);
        SenderName = senderName;
        _senderId = senderId;
        Body = body;
        if (_formattedHtml != formattedHtml)
        {
            _formattedHtml = formattedHtml;
            FormattedBody = formattedHtml is null
                ? null
                : MessageFormatter.HtmlToFormattedString(formattedHtml);
            OnPropertyChanged(nameof(HasFormattedBody));
            OnPropertyChanged(nameof(HasPlainBody));
        }
        Time = time;
        IsOwn = isOwn;
        SendState = sendState;
        EventType = eventType;
        Source = source;
        IsNonMessageEvent = isNonMessageEvent;

        if (_avatarUrl != avatarUrl)
        {
            _avatarUrl = avatarUrl;
            Avatar = null;
            Interlocked.Exchange(ref _avatarLoadStarted, 0);
        }

        if (ImageSourceJson != imageSourceJson)
        {
            ImageSourceJson = imageSourceJson;
            ContentImage = null;
            Interlocked.Exchange(ref _contentLoadStarted, 0);
            OnPropertyChanged(nameof(HasImage));
        }

        if (senderChanged) OnPropertyChanged(nameof(Initials));
        if (bodyVisibilityChanged)
        {
            OnPropertyChanged(nameof(HasBody));
            OnPropertyChanged(nameof(HasPlainBody));
        }
    }

    partial void OnIsOwnChanged(bool value)
    {
        OnPropertyChanged(nameof(AvatarColumn));
        OnPropertyChanged(nameof(MessageAlignment));
        OnPropertyChanged(nameof(TextAlignment));
    }

    public bool TryStartAvatarLoad() => Interlocked.Exchange(ref _avatarLoadStarted, 1) == 0;
    public bool TryStartContentLoad() => Interlocked.Exchange(ref _contentLoadStarted, 1) == 0;
    public bool TryStartMediaLoad() => Interlocked.Exchange(ref _mediaLoadStarted, 1) == 0;

    public void UpdateMedia(string? sourceJson, string? kind, string? fileName)
    {
        if (MediaSourceJson == sourceJson && MediaKind == kind && MediaFileName == fileName) return;
        MediaSourceJson = sourceJson;
        MediaKind = kind;
        MediaFileName = fileName;
        MediaPlayerSource = null;
        Interlocked.Exchange(ref _mediaLoadStarted, 0);
        OnPropertyChanged(nameof(HasPlayableMedia));
        OnPropertyChanged(nameof(HasFileAttachment));
        OnPropertyChanged(nameof(MediaPlayerHeight));
    }

    public bool CanGroupWith(ChatMessage other) =>
        IsMessage && other.IsMessage && _senderId == other._senderId;

    public void SetGrouping(bool continuesPrevious, bool continuesNext)
    {
        ShowAuthor = !continuesPrevious;
        RowPadding = new Thickness(24, continuesPrevious ? 1 : 8, 24, continuesNext ? 1 : 8);
        BubbleCorners = (continuesPrevious, continuesNext) switch
        {
            (false, true) => new CornerRadius(18, 18, 6, 6),
            (true, true) => new CornerRadius(6),
            (true, false) => new CornerRadius(6, 6, 18, 18),
            _ => new CornerRadius(18)
        };
    }

    public void UpdateReactions(IEnumerable<ChatReaction> reactions)
    {
        Reactions.Clear();
        foreach (var reaction in reactions) Reactions.Add(reaction);
        OnPropertyChanged(nameof(HasReactions));
    }
}

public sealed record ChatReaction(string MessageId, string Key, int Count, bool IsOwn)
{
    public string Label => $"{Key} {Count}";
}

public sealed partial class RoomMemberItem(
    string userId,
    string displayName,
    string? avatarUrl,
    string role) : ObservableObject
{
    public string UserId { get; } = userId;
    public string DisplayName { get; } = displayName;
    public string? AvatarUrl { get; } = avatarUrl;
    public string Role { get; } = role;
    public string Initials { get; } = RoomListItem.Initial(displayName);

    [ObservableProperty]
    public partial ImageSource? Avatar { get; set; }
}

public sealed partial class MatrixImageAsset(
    string shortcode,
    string body,
    string url,
    bool isSticker,
    bool isEmoji) : ObservableObject
{
    public string Shortcode { get; } = shortcode;
    public string Body { get; } = body;
    public string Url { get; } = url;
    public bool IsSticker { get; } = isSticker;
    public bool IsEmoji { get; } = isEmoji;

    [ObservableProperty]
    public partial ImageSource? Image { get; set; }
}
