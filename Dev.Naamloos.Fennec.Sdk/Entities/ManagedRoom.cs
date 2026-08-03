using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Entities
{
    public partial class ManagedRoom : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value) return;
                _displayName = value;
                OnPropertyChanged();
            }
        }
        private string? _displayName;

        public string? Id
        {
            get => _id;
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged();
            }
        }
        private string? _id;

        public string? AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                if (_avatarUrl == value) return;
                _avatarUrl = value;
                OnPropertyChanged();
            }
        }
        private string? _avatarUrl;

        public bool IsSpace
        {
            get => _isSpace;
            set
            {
                if (_isSpace == value) return;
                _isSpace = value;
                OnPropertyChanged();
            }
        }
        private bool _isSpace;

        public bool IsEncrypted
        {
            get => _isEncrypted;
            private set
            {
                if (_isEncrypted == value) return;
                _isEncrypted = value;
                OnPropertyChanged();
            }
        }
        private bool _isEncrypted;

        public ulong UnreadCount
        {
            get => _unreadCount;
            private set
            {
                if (_unreadCount == value) return;
                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadBadge));
            }
        }
        private ulong _unreadCount;

        public ulong NotificationCount
        {
            get => _notificationCount;
            private set
            {
                if (_notificationCount == value) return;
                _notificationCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnread));
            }
        }
        private ulong _notificationCount;

        public ulong MentionCount
        {
            get => _mentionCount;
            private set
            {
                if (_mentionCount == value) return;
                _mentionCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMentions));
                OnPropertyChanged(nameof(UnreadBadge));
            }
        }
        private ulong _mentionCount;

        public ulong HighlightCount
        {
            get => _highlightCount;
            private set
            {
                if (_highlightCount == value) return;
                _highlightCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMentions));
                OnPropertyChanged(nameof(UnreadBadge));
            }
        }
        private ulong _highlightCount;

        public bool IsMarkedUnread
        {
            get => _isMarkedUnread;
            private set
            {
                if (_isMarkedUnread == value) return;
                _isMarkedUnread = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadBadge));
            }
        }
        private bool _isMarkedUnread;

        public bool IsServerNotice
        {
            get => _isServerNotice;
            internal set
            {
                if (_isServerNotice == value) return;
                _isServerNotice = value;
                OnPropertyChanged();
            }
        }
        private bool _isServerNotice;

        public bool HasUnread => IsMarkedUnread || UnreadCount > 0 || NotificationCount > 0;
        public bool HasMentions => MentionCount > 0 || HighlightCount > 0;
        public string UnreadBadge => Math.Max(MentionCount, HighlightCount) > 0
            ? Math.Max(MentionCount, HighlightCount) > 99
                ? "99+"
                : Math.Max(MentionCount, HighlightCount).ToString()
            : UnreadCount > 0
                ? UnreadCount > 99 ? "99+" : UnreadCount.ToString()
                : NotificationCount > 0
                    ? NotificationCount > 99 ? "99+" : NotificationCount.ToString()
                    : IsMarkedUnread ? "•" : string.Empty;

        public Room NativeRoom
        {
            get => _room;
            set
            {
                _room = value;
                OnPropertyChanged();
            }
        }
        private Room _room;
        private Task<RoomInfo>? _roomInfoTask;

        public ManagedRoom(Room room, bool includeRoomInfo = true)
        {
            Update(room, includeRoomInfo);
            if (_room is null)
            {
                throw new ArgumentNullException(nameof(room), "Room cannot be null.");
            }
        }

        public void Update(Room room) => Update(room, true);

        private void Update(Room room, bool includeRoomInfo)
        {
            this._room = room;
            this.DisplayName = room.DisplayName();
            this.Id = room.Id();
            this.AvatarUrl = room.AvatarUrl();
            this.IsSpace = room.IsSpace();
            if (includeRoomInfo)
                _ = UpdateRoomInfoAsync(room);
            else
                _roomInfoTask = null;
        }

        private async Task UpdateRoomInfoAsync(Room room)
        {
            try
            {
                var roomInfoTask = room.RoomInfo();
                _roomInfoTask = roomInfoTask;
                var isEncrypted = await room.IsEncrypted();
                var info = await roomInfoTask;
                if (!ReferenceEquals(_room, room) && _room.Id() != room.Id()) return;
                IsEncrypted = isEncrypted;
                UnreadCount = info.NumUnreadMessages;
                NotificationCount = info.NumUnreadNotifications;
                MentionCount = info.NumUnreadMentions;
                HighlightCount = info.HighlightCount;
                IsMarkedUnread = info.IsMarkedUnread;
            }
            catch
            {
                // Room-list snapshots can briefly outlive a replaced native room.
            }
        }

        public async Task ResolveDirectAvatarAsync()
        {
            if (!string.IsNullOrWhiteSpace(AvatarUrl))
            {
                return;
            }

            var info = _roomInfoTask is null
                ? await NativeRoom.RoomInfo()
                : await _roomInfoTask;
            if (!info.IsDm)
            {
                return;
            }

            using var members = await NativeRoom.Members();
            var otherMember = members
                .NextChunk(100)
                ?.FirstOrDefault(member => member.UserId != NativeRoom.OwnUserId());
            AvatarUrl = otherMember?.AvatarUrl;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
