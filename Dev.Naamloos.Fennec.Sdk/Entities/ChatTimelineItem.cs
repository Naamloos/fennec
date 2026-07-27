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
    private ChatEventGroup? _eventGroup;
    private bool _isEventGroupHeader;

    public ChatTimelineItem(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public bool IsMessage { get => _isMessage; set => Set(ref _isMessage, value); }

    public bool IsUnknown { get => _isUnknown; set => Set(ref _isUnknown, value); }

    public bool IsOwn
    {
        get => _isOwn;
        set
        {
            if (Set(ref _isOwn, value))
            {
                Raise(nameof(ShowAvatar));
                Raise(nameof(ShowSender));
            }
        }
    }

    public bool CanReply { get => _canReply; set => Set(ref _canReply, value); }

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
            }
        }
    }

    public bool ShowAvatar => !IsOwn && IsGroupEnd;

    public bool ShowSender => !IsOwn && IsGroupStart;

    public string Sender { get => _sender; set => Set(ref _sender, value); }

    public string SenderId { get => _senderId; set => Set(ref _senderId, value); }

    public string? SenderAvatarUrl { get => _senderAvatarUrl; set => Set(ref _senderAvatarUrl, value); }

    public string Body { get => _body; set => Set(ref _body, value); }

    public string? FormattedBody { get => _formattedBody; set => Set(ref _formattedBody, value); }

    public string EventType { get => _eventType; set => Set(ref _eventType, value); }

    public string SourceJson { get => _sourceJson; set => Set(ref _sourceJson, value); }

    public string? EventId { get => _eventId; set => Set(ref _eventId, value); }

    public string? ReplyPreview { get => _replyPreview; set => Set(ref _replyPreview, value); }

    public ChatMedia? Media { get => _media; set => Set(ref _media, value); }

    public ChatEventGroup? EventGroup
    {
        get => _eventGroup;
        set
        {
            if (ReferenceEquals(_eventGroup, value))
            {
                return;
            }

            if (_eventGroup is not null)
            {
                _eventGroup.PropertyChanged -= OnEventGroupChanged;
            }

            _eventGroup = value;

            if (_eventGroup is not null)
            {
                _eventGroup.PropertyChanged += OnEventGroupChanged;
            }

            Raise();
            RaiseEventGroupProperties();
        }
    }

    public bool IsEventGroupHeader
    {
        get => _isEventGroupHeader;
        set
        {
            if (Set(ref _isEventGroupHeader, value))
            {
                Raise(nameof(IsEventVisible));
                Raise(nameof(ShowEventGroupToggle));
            }
        }
    }

    public bool HasEventGroup => EventGroup?.Count > 1;

    public bool IsEventGroupCollapsed => HasEventGroup && EventGroup?.IsCollapsed == true;

    public bool IsEventVisible => !HasEventGroup || IsEventGroupHeader || !IsEventGroupCollapsed;

    public bool ShowEventGroupToggle => HasEventGroup && IsEventGroupHeader;

    public string EventGroupToggleText => EventGroup is { Count: > 1 } group
        ? IsEventGroupCollapsed ? $"Collapsed {group.Count} events" : $"Collapse {group.Count} events"
        : string.Empty;

    public void ToggleEventGroup()
    {
        if (EventGroup is { Count: > 1 } group)
        {
            group.IsCollapsed = !group.IsCollapsed;
        }
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
        Media = source.Media;
        EventOrTransactionId = source.EventOrTransactionId;
        IsRemoteEvent = source.IsRemoteEvent;

        Replace(Reactions, source.Reactions);
        Replace(ReadReceipts, source.ReadReceipts);
    }

    private void OnEventGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        RaiseEventGroupProperties();

    private void RaiseEventGroupProperties()
    {
        Raise(nameof(HasEventGroup));
        Raise(nameof(IsEventGroupCollapsed));
        Raise(nameof(IsEventVisible));
        Raise(nameof(EventGroupToggleText));
        Raise(nameof(ShowEventGroupToggle));
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();

        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
