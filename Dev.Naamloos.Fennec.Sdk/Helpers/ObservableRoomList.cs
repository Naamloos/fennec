using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers
{
    public class ObservableRoomList : ObservableCollection<ManagedRoom>, IDisposable
    {
        private RoomList _roomList;
        private RoomListEntriesListenerCallback? _listenerCallback;
        private RoomListEntriesWithDynamicAdaptersResult? _emitter;
        private SynchronizationContext? _synchronizationContext;
        private bool _disposed;

        internal ObservableRoomList(RoomList roomList, RoomListEntriesDynamicFilterKind? initialFilter = null)
        {
            _roomList = roomList;

            _listenerCallback = RoomListEntriesListenerCallback.Create(entries => this.applyUpdates(entries));

            _emitter = roomList.EntriesWithDynamicAdapters(5000, _listenerCallback);
            _emitter.Controller().SetFilter(initialFilter ?? new RoomListEntriesDynamicFilterKind.All([]));
        }

        public void SetFilter(RoomListEntriesDynamicFilterKind filter)
        {
            if (_disposed)
            {
                return;
            }

            _emitter?.Controller().SetFilter(filter);
        }

        public void CaptureCurrentContext()
        {
            _synchronizationContext = SynchronizationContext.Current;
        }

        private void applyUpdates(RoomListEntriesUpdate[] entries, bool skipContext = false)
        {
            if(_disposed)
            {
                return;
            }

            // This piece ensures that updates are applied on the same thread that created the ObservableCollection, which is important for UI updates in many frameworks.
            if (_synchronizationContext != null && !skipContext)
            {
                _synchronizationContext.Post(_ => this.applyUpdates(entries, true), null);
                return;
            }

            foreach (var entry in entries)
            {
                switch (entry)
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

        // append to the end
        private void append(RoomListEntriesUpdate.Append append)
        {
            var values = append.Values.Select(x => new ManagedRoom(x));
            foreach (var value in values)
            {
                this.Add(value);
            }
        }

        // clear the list
        private void clear(RoomListEntriesUpdate.Clear clear)
        {
            this.Clear();
        }

        // push to the front
        private void pushFront(RoomListEntriesUpdate.PushFront pushFront)
        {
            this.Insert(0, new ManagedRoom(pushFront.Value));
        }

        // push to the back
        private void pushBack(RoomListEntriesUpdate.PushBack pushBack)
        {
            this.Add(new ManagedRoom(pushBack.Value));
        }

        // pop the first element
        private void popFront(RoomListEntriesUpdate.PopFront popFront)
        {
            if (this.Count > 0)
            {
                this.RemoveAt(0);
            }
        }

        // pop the last element
        private void popBack(RoomListEntriesUpdate.PopBack popBack)
        {
            if (this.Count > 0)
            {
                this.RemoveAt(Count - 1);
            }
        }

        // insert at index
        private void insert(RoomListEntriesUpdate.Insert insert)
        {
            this.Insert((int)insert.Index, new ManagedRoom(insert.Value));
        }

        // set at specific index (update)
        private void set(RoomListEntriesUpdate.Set set)
        {
            this[(int)set.Index].Update(set.Value);
        }

        // remove at a specific index
        private void remove(RoomListEntriesUpdate.Remove remove)
        {
            this.RemoveAt((int)remove.Index);
        }

        // the list was truncated to a specific length
        private void truncate(RoomListEntriesUpdate.Truncate truncate)
        {
            while (Count > truncate.Length)
            {
                RemoveAt(Count - 1);
            }
        }

        // reset the list with new values
        private void reset(RoomListEntriesUpdate.Reset reset)
        {
            this.Clear();
            var values = reset.Values.Select(x => new ManagedRoom(x));
            foreach (var value in values)
            {
                this.Add(value);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _emitter?.Dispose();

            _listenerCallback = null;
            _emitter = null;

            GC.SuppressFinalize(this);
        }
    }
}
