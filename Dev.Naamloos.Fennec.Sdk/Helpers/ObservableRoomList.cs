using Dev.Naamloos.Fennec.Sdk.NativeEventHandler;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers
{
    public sealed class RoomEntry : INotifyPropertyChanged
    {
        private Room _room;
        private string _name;

        public RoomEntry(Room room)
        {
            _room = room;
            _name = GetName(room);
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

        public bool IsSpace => _room.IsSpace();

        internal void Update(Room room)
        {
            _room = room;

            OnPropertyChanged(nameof(Room));
            Name = GetName(room);
        }

        internal void Refresh()
        {
            Name = GetName(_room);
        }

        private static string GetName(Room room)
        {
            return room.DisplayName() ?? room.Id();
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
        private readonly SynchronizationContext? _synchronizationContext;

        private RoomListEntriesEvent? _listener;
        private RoomListEntriesWithDynamicAdaptersResult? _adapterResult;

        private bool _disposed;

        internal ObservableRoomList(RoomList roomList)
        {
            _synchronizationContext = SynchronizationContext.Current;
            _listener = new RoomListEntriesEvent(this.updateEntries);

            _adapterResult = roomList.EntriesWithDynamicAdapters(5000, _listener);
            _adapterResult.Controller().SetFilter(new RoomListEntriesDynamicFilterKind.All(new RoomListEntriesDynamicFilterKind[0]));
        }

        private void RunOnCapturedContext(System.Action action)
        {
            if (_synchronizationContext is null ||
                SynchronizationContext.Current == _synchronizationContext)
            {
                action();
                return;
            }

            _synchronizationContext.Post(
                static state => ((System.Action)state!).Invoke(),
                action);
        }

        private void updateEntries(RoomListEntriesUpdate[] updates)
        {
            RunOnCapturedContext(() => applyUpdates(updates));
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
                Add(new RoomEntry(room));
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
            Insert(0, new RoomEntry(pushFront.Value));
        }

        // an element was added at the back
        private void pushBack(RoomListEntriesUpdate.PushBack pushBack)
        {
            Add(new RoomEntry(pushBack.Value));
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
            Insert((int)insert.Index, new RoomEntry(insert.Value));
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

            this[index] = new RoomEntry(set.Value);
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
                    Add(new RoomEntry(room));
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

            _adapterResult = null;
            _listener = null;

            GC.SuppressFinalize(this);
        }
    }
}
