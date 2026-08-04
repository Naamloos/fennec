using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed class ManagedSpaceRoom : ObservableModel
{
    private string _displayName = string.Empty;
    private string? _avatarUrl;
    private string? _canonicalAlias;
    private string[] _via = [];
    private bool _isJoined;
    private ulong _unreadCount;
    private bool _hasUnread;

    public ManagedSpaceRoom(SpaceRoom room) => Update(room);

    public string Id { get; private set; } = string.Empty;

    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public string? AvatarUrl
    {
        get => _avatarUrl;
        private set => Set(ref _avatarUrl, value);
    }

    public string? CanonicalAlias
    {
        get => _canonicalAlias;
        private set => Set(ref _canonicalAlias, value);
    }

    public string[] Via
    {
        get => _via;
        private set => Set(ref _via, value);
    }

    public bool IsJoined
    {
        get => _isJoined;
        private set => Set(ref _isJoined, value);
    }

    public bool IsSpace { get; private set; }

    public ulong UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (Set(ref _unreadCount, value))
            {
                Raise(nameof(UnreadLabel));
            }
        }
    }

    public bool HasUnread
    {
        get => _hasUnread;
        private set => Set(ref _hasUnread, value);
    }

    public string UnreadLabel => UnreadCount > 0 ? UnreadCount.ToString() : "•";

    public void Update(SpaceRoom room)
    {
        Id = room.RoomId;
        DisplayName = room.DisplayName;
        AvatarUrl = room.AvatarUrl;
        CanonicalAlias = room.CanonicalAlias;
        Via = room.Via;
        IsJoined = room.State is Membership.Joined;
        IsSpace = room.RoomType is RoomType.Space;
        Raise(nameof(Id));
        Raise(nameof(IsSpace));
    }

    public void UpdateUnread(ulong count, bool hasUnread)
    {
        UnreadCount = count;
        HasUnread = hasUnread;
    }
}
