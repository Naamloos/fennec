using Dev.Naamloos.Fennec.Sdk.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers
{
    public sealed record RoomSpace(
        string Id,
        string Name,
        string? AvatarUrl);

    public sealed class RoomEntry : INotifyPropertyChanged
    {
        private Room _room;
        private string _name;
        private string? _avatarUrl;
        private bool _isSpace;
        private bool _isDirectMessage;
        private bool _hasUnread;
        private IReadOnlyList<RoomSpace> _spaces = [];
        private int _refreshVersion;

        internal RoomEntry(
            Room room,
            SpaceService spaceService)
        {
            _room = room;
            _name = GetName(room);
            _avatarUrl = room.AvatarUrl();
            _isSpace = room.IsSpace();
            _ = RefreshInfoAsync(room);

            if (!_isSpace)
            {
                _ = LoadSpacesAsync(room, spaceService);
            }
        }

        public Room Room => _room;

        public string Id => _room.Id();

        public string Name
        {
            get => _name;
            private set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                OnPropertyChanged();
            }
        }

        public string? AvatarUrl
        {
            get => _avatarUrl;
            private set => SetField(ref _avatarUrl, value);
        }

        public bool IsSpace
        {
            get => _isSpace;
            private set => SetField(ref _isSpace, value);
        }

        public bool HasUnread
        {
            get => _hasUnread;
            private set => SetField(ref _hasUnread, value);
        }

        public bool IsDirectMessage
        {
            get => _isDirectMessage;
            private set => SetField(ref _isDirectMessage, value);
        }

        public IReadOnlyList<RoomSpace> Spaces
        {
            get => _spaces;
            private set => SetField(ref _spaces, value);
        }

        internal void Update(Room room)
        {
            _room = room;

            OnPropertyChanged(nameof(Room));
            Name = GetName(room);
            AvatarUrl = room.AvatarUrl();
            IsSpace = room.IsSpace();
            _ = RefreshInfoAsync(room);
        }

        private static string GetName(Room room)
        {
            return room.DisplayName() ?? room.Id();
        }

        private async Task RefreshInfoAsync(Room room)
        {
            var version = Interlocked.Increment(ref _refreshVersion);

            try
            {
                using var info = await room.RoomInfo();

                if (version != _refreshVersion)
                {
                    return;
                }

                Name = info.DisplayName ?? room.Id();
                AvatarUrl = info.AvatarUrl;
                IsSpace = info.IsSpace;
                IsDirectMessage = info.IsDm || info.IsDirect;
                HasUnread =
                    info.IsMarkedUnread ||
                    info.NumUnreadMessages > 0 ||
                    info.NumUnreadNotifications > 0;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not refresh room {room.Id()}: {exception}");
            }
        }

        public async Task MarkAsReadAsync()
        {
            Interlocked.Increment(ref _refreshVersion);
            HasUnread = false;

            await Task.WhenAll(
                _room.MarkAsRead(ReceiptType.Read),
                _room.SetUnreadFlag(false));
        }

        private async Task LoadSpacesAsync(
            Room room,
            SpaceService spaceService)
        {
            try
            {
                Spaces = (await spaceService.JoinedParentsOfChild(room.Id()))
                    .Select(space => new RoomSpace(
                        space.RoomId,
                        space.DisplayName,
                        space.AvatarUrl))
                    .ToArray();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load spaces for {room.Id()}: {exception}");
            }
        }

        private void SetField<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ObservableRoomList : ObservableCollection<RoomEntry>, IDisposable
    {
        private readonly SpaceService _spaceService;

        private RoomListEntriesListenerCallback? _listener;
        private RoomListEntriesWithDynamicAdaptersResult? _adapterResult;

        private bool _disposed;

        internal ObservableRoomList(
            RoomList roomList,
            SpaceService spaceService)
        {
            _spaceService = spaceService;
            _listener = RoomListEntriesListenerCallback.Create(entries => this.applyUpdates(entries));

            _adapterResult = roomList.EntriesWithDynamicAdapters(5000, _listener);
            _adapterResult.Controller().SetFilter(new RoomListEntriesDynamicFilterKind.All(new RoomListEntriesDynamicFilterKind[0]));
        }

        private void applyUpdates(RoomListEntriesUpdate[] updates)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var update in updates)
            {
                switch (update)
                {
                    case RoomListEntriesUpdate.Append appendUpdate:
                        append(appendUpdate);
                        break;
                    case RoomListEntriesUpdate.Clear clearUpdate:
                        clear(clearUpdate);
                        break;
                    case RoomListEntriesUpdate.PushFront pushFrontUpdate:
                        pushFront(pushFrontUpdate);
                        break;
                    case RoomListEntriesUpdate.PushBack pushBackUpdate:
                        pushBack(pushBackUpdate);
                        break;
                    case RoomListEntriesUpdate.PopFront popFrontUpdate:
                        popFront(popFrontUpdate);
                        break;
                    case RoomListEntriesUpdate.PopBack popBackUpdate:
                        popBack(popBackUpdate);
                        break;
                    case RoomListEntriesUpdate.Insert insertUpdate:
                        insert(insertUpdate);
                        break;
                    case RoomListEntriesUpdate.Set setUpdate:
                        set(setUpdate);
                        break;
                    case RoomListEntriesUpdate.Remove removeUpdate:
                        remove(removeUpdate);
                        break;
                    case RoomListEntriesUpdate.Truncate truncateUpdate:
                        truncate(truncateUpdate);
                        break;
                    case RoomListEntriesUpdate.Reset resetUpdate:
                        reset(resetUpdate);
                        break;
                    default:
                        break;
                }
            }
        }

        // multiple elements appended
        private void append(RoomListEntriesUpdate.Append append)
        {
            foreach (var room in append.Values)
            {
                Add(new RoomEntry(room, _spaceService));
            }
        }

        // vector was cleared
        private void clear(RoomListEntriesUpdate.Clear clear)
        {
            Clear();
        }

        // an element was added at the front
        private void pushFront(RoomListEntriesUpdate.PushFront pushFront)
        {
            Insert(0, new RoomEntry(pushFront.Value, _spaceService));
        }

        // an element was added at the back
        private void pushBack(RoomListEntriesUpdate.PushBack pushBack)
        {
            Add(new RoomEntry(pushBack.Value, _spaceService));
        }

        // the element at the front was removed
        private void popFront(RoomListEntriesUpdate.PopFront popFront)
        {
            RemoveAt(0);
        }

        // the element at the back was removed
        private void popBack(RoomListEntriesUpdate.PopBack popBack)
        {
            RemoveAt(Count - 1);
        }

        // an element was inserted at a specific index
        private void insert(RoomListEntriesUpdate.Insert insert)
        {
            Insert((int)insert.Index, new RoomEntry(insert.Value, _spaceService));
        }

        // an element was set at a specific index
        private void set(RoomListEntriesUpdate.Set set)
        {
            var index = checked((int)set.Index);
            var existing = this[index];

            if (existing.Id == set.Value.Id())
            {
                existing.Update(set.Value);
                return;
            }

            this[index] = new RoomEntry(set.Value, _spaceService);
        }

        // an element was removed at a specific index
        private void remove(RoomListEntriesUpdate.Remove remove)
        {
            RemoveAt((int)remove.Index);
        }

        // the list was truncated to a specific length
        private void truncate(RoomListEntriesUpdate.Truncate truncate)
        {
            while (Count > truncate.Length)
            {
                RemoveAt(Count - 1);
            }
        }

        // the list was reset with new values
        private void reset(RoomListEntriesUpdate.Reset reset)
        {
            var existingEntries = this
                .ToDictionary(entry => entry.Id);

            Clear();

            foreach (var room in reset.Values) 
            {
                if (existingEntries.TryGetValue(room.Id(), out var existing))
                {
                    existing.Update(room);
                    Add(existing);
                }
                else
                {
                    Add(new RoomEntry(room, _spaceService));
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _adapterResult?.EntriesStream().Cancel();
            _adapterResult?.Dispose();
            _spaceService.Dispose();

            _adapterResult = null;
            _listener = null;

            GC.SuppressFinalize(this);
        }
    }
}
