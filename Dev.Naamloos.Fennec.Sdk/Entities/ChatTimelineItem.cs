using System.Collections.ObjectModel;

namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ChatTimelineItem : ObservableModel
{
    private bool _isMessage;
    private bool _isUnknown;
    private bool _isOwn;
    private bool _canReply;
    private bool _isGroupStart = true;
    private bool _isGroupEnd = true;
    private string _sender = string.Empty;
    private string _senderId = string.Empty;
    private string? _senderAvatarUrl;
    private string _body = string.Empty;
    private string? _formattedBody;
    private string _eventType = string.Empty;
    private string _sourceJson = "{}";
    private string? _eventId;
    private string? _replyPreview;
    private ChatMedia? _media;
    private ChatPoll? _poll;

    public ChatTimelineItem(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public bool IsMessage
    {
        get => _isMessage;
        set => Set(ref _isMessage, value);
    }

    public bool IsUnknown
    {
        get => _isUnknown;
        set => Set(ref _isUnknown, value);
    }

    public bool IsOwn
    {
        get => _isOwn;
        set
        {
            if (Set(ref _isOwn, value))
            {
                Raise(nameof(ShowAvatar));
                Raise(nameof(ShowSender));
                Raise(nameof(VisibleAvatarUrl));
            }
        }
    }

    public bool CanReply
    {
        get => _canReply;
        set => Set(ref _canReply, value);
    }

    public bool IsGroupStart
    {
        get => _isGroupStart;
        set
        {
            if (Set(ref _isGroupStart, value))
            {
                Raise(nameof(ShowSender));
            }
        }
    }

    public bool IsGroupEnd
    {
        get => _isGroupEnd;
        set
        {
            if (Set(ref _isGroupEnd, value))
            {
                Raise(nameof(ShowAvatar));
                Raise(nameof(VisibleAvatarUrl));
            }
        }
    }

    public bool ShowAvatar => !IsOwn && IsGroupEnd;

    public bool ShowSender => !IsOwn && IsGroupStart;

    public string? VisibleAvatarUrl => ShowAvatar ? SenderAvatarUrl : null;

    public string Sender
    {
        get => _sender;
        set => Set(ref _sender, value);
    }

    public string SenderId
    {
        get => _senderId;
        set => Set(ref _senderId, value);
    }

    public string? SenderAvatarUrl
    {
        get => _senderAvatarUrl;
        set
        {
            if (Set(ref _senderAvatarUrl, value))
            {
                Raise(nameof(VisibleAvatarUrl));
            }
        }
    }

    public string Body
    {
        get => _body;
        set => Set(ref _body, value);
    }

    public string? FormattedBody
    {
        get => _formattedBody;
        set => Set(ref _formattedBody, value);
    }

    public string EventType
    {
        get => _eventType;
        set => Set(ref _eventType, value);
    }

    public string SourceJson
    {
        get => _sourceJson;
        set => Set(ref _sourceJson, value);
    }

    public string? EventId
    {
        get => _eventId;
        set => Set(ref _eventId, value);
    }

    public string? ReplyPreview
    {
        get => _replyPreview;
        set => Set(ref _replyPreview, value);
    }

    public ChatMedia? Media
    {
        get => _media;
        set => Set(ref _media, value);
    }

    public ChatPoll? Poll
    {
        get => _poll;
        set => Set(ref _poll, value);
    }

    public ObservableCollection<ChatReaction> Reactions { get; } = [];

    public ObservableCollection<ChatReadReceipt> ReadReceipts { get; } = [];

    internal string EventOrTransactionId { get; set; } = string.Empty;

    internal bool IsRemoteEvent { get; set; }

    internal void UpdateFrom(ChatTimelineItem source)
    {
        IsMessage = source.IsMessage;
        IsUnknown = source.IsUnknown;
        IsOwn = source.IsOwn;
        CanReply = source.CanReply;
        IsGroupStart = source.IsGroupStart;
        IsGroupEnd = source.IsGroupEnd;
        Sender = source.Sender;
        SenderId = source.SenderId;
        SenderAvatarUrl = source.SenderAvatarUrl;
        Body = source.Body;
        FormattedBody = source.FormattedBody;
        EventType = source.EventType;
        SourceJson = source.SourceJson;
        EventId = source.EventId;
        ReplyPreview = source.ReplyPreview;
        if (!IsSameMedia(Media, source.Media))
        {
            Media = source.Media;
        }
        UpdatePoll(source.Poll);
        EventOrTransactionId = source.EventOrTransactionId;
        IsRemoteEvent = source.IsRemoteEvent;

        UpdateReactions(source.Reactions);
        UpdateReadReceipts(source.ReadReceipts);
    }

    private static bool IsSameMedia(ChatMedia? left, ChatMedia? right) =>
        ReferenceEquals(left, right)
        || left is not null
            && right is not null
            && left.Kind == right.Kind
            && left.SourceJson == right.SourceJson
            && left.Filename == right.Filename
            && left.MimeType == right.MimeType
            && left.ThumbnailSourceJson == right.ThumbnailSourceJson;

    private void UpdatePoll(ChatPoll? source)
    {
        var current = Poll;
        if (current is null || source is null)
        {
            Poll = source;
            return;
        }

        var sameShape =
            current.Question == source.Question
            && current.MaxSelections == source.MaxSelections
            && current.IsClosed == source.IsClosed
            && current.Answers.Count == source.Answers.Count;
        for (var index = 0; sameShape && index < current.Answers.Count; index++)
        {
            sameShape =
                current.Answers[index].Id == source.Answers[index].Id
                && current.Answers[index].Text == source.Answers[index].Text;
        }

        if (!sameShape)
        {
            Poll = source;
            return;
        }

        for (var index = 0; index < current.Answers.Count; index++)
        {
            current.Answers[index].VoteCount = source.Answers[index].VoteCount;
            current.Answers[index].IsSelected = source.Answers[index].IsSelected;
        }
    }

    private void UpdateReactions(IReadOnlyList<ChatReaction> source)
    {
        var sameKeys = Reactions.Count == source.Count;
        for (var index = 0; sameKeys && index < Reactions.Count; index++)
        {
            sameKeys = Reactions[index].Key == source[index].Key;
        }

        if (sameKeys)
        {
            for (var index = 0; index < Reactions.Count; index++)
            {
                Reactions[index].Count = source[index].Count;
                Reactions[index].IsOwn = source[index].IsOwn;
            }
            return;
        }

        Reactions.Clear();
        foreach (var reaction in source)
        {
            Reactions.Add(reaction);
        }
    }

    private void UpdateReadReceipts(IReadOnlyList<ChatReadReceipt> source)
    {
        var sameUsers = ReadReceipts.Count == source.Count;
        for (var index = 0; sameUsers && index < ReadReceipts.Count; index++)
        {
            sameUsers = ReadReceipts[index].UserId == source[index].UserId;
        }

        if (sameUsers)
        {
            for (var index = 0; index < ReadReceipts.Count; index++)
            {
                ReadReceipts[index].Name = source[index].Name;
                ReadReceipts[index].AvatarUrl = source[index].AvatarUrl;
            }
            return;
        }

        ReadReceipts.Clear();
        foreach (var receipt in source)
        {
            ReadReceipts.Add(receipt);
        }
    }
}
