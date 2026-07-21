using Dev.Naamloos.Fennec.Sdk.Interfaces;
using Dev.Naamloos.Fennec.Sdk.NativeListeners;
using Dev.Naamloos.Fennec.Sdk.NativeObservers;
using System.Collections.ObjectModel;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk;

public sealed class ManagedRoomListService : IAsyncDisposable
{
    private const uint DefaultPageSize = 50;

    private readonly object _roomsLock = new();

    private readonly List<Room> _rooms = [];
    private readonly AsyncSubject<IReadOnlyList<Room>> _roomsSubject = new();

    private RoomListService? _roomListService;
    private RoomList? _roomList;

    private NativeRoomListEntriesListener? _entriesListener;
    private RoomListEntriesWithDynamicAdaptersResult? _entriesResult;
    private RoomListDynamicEntriesController? _entriesController;
    private TaskHandle? _entriesStream;

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Emits a complete ordered snapshot whenever the room list changes.
    /// </summary>
    public IAsyncObservable<IReadOnlyList<Room>> RoomsObservable =>
        _roomsSubject;

    /// <summary>
    /// Gets the currently loaded ordered room list.
    /// </summary>
    public IReadOnlyList<Room> Rooms
    {
        get
        {
            lock (_roomsLock)
            {
                return _rooms.ToArray();
            }
        }
    }

    /// <summary>
    /// Whether the native room list has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    internal ManagedRoomListService(
        RoomListService roomListService)
    {
        _roomListService = roomListService
            ?? throw new ArgumentNullException(nameof(roomListService));
    }

    /// <summary>
    /// Creates the native room list and begins listening for updates.
    /// </summary>
    internal async Task InitializeAsync(
        uint pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var roomListService = _roomListService
            ?? throw new InvalidOperationException(
                "The native room-list service is unavailable.");

        _roomList = await roomListService.AllRooms();

        cancellationToken.ThrowIfCancellationRequested();

        _entriesListener = new NativeRoomListEntriesListener(
            ApplyRoomListUpdates);

        _entriesResult = _roomList.EntriesWithDynamicAdapters(
            pageSize,
            _entriesListener);

        _entriesController = _entriesResult.Controller();
        _entriesStream = _entriesResult.EntriesStream();

        _initialized = true;
    }

    /// <summary>
    /// Resolves a room directly through the native room-list service.
    /// </summary>
    public Room GetRoom(string roomId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);

        var roomListService = _roomListService
            ?? throw new InvalidOperationException(
                "The native room-list service is unavailable.");

        return roomListService.Room(roomId);
    }

    /// <summary>
    /// Loads another page of room-list entries.
    /// </summary>
    public void LoadNextPage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        _entriesController!.AddOnePage();
    }

    /// <summary>
    /// Resets the room list back to its first page.
    /// </summary>
    public void ResetToFirstPage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        _entriesController!.ResetToOnePage();
    }

    /// <summary>
    /// Applies a native Matrix room-list filter.
    /// </summary>
    public bool SetFilter(
        RoomListEntriesDynamicFilterKind filter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(filter);

        EnsureInitialized();

        return _entriesController!.SetFilter(filter);
    }

    private void ApplyRoomListUpdates(
        IReadOnlyList<RoomListEntriesUpdate> updates)
    {
        if (_disposed)
        {
            return;
        }

        IReadOnlyList<Room> snapshot;

        lock (_roomsLock)
        {
            foreach (var update in updates)
            {
                ApplyRoomListUpdate(update);
            }

            snapshot = _rooms.ToArray();
        }

        _ = PublishRoomsSafelyAsync(snapshot);
    }

    private void ApplyRoomListUpdate(
        RoomListEntriesUpdate update)
    {
        switch (update)
        {
            case RoomListEntriesUpdate.Append append:
                _rooms.AddRange(append.Values);
                break;

            case RoomListEntriesUpdate.Clear:
                _rooms.Clear();
                break;

            case RoomListEntriesUpdate.PushFront pushFront:
                _rooms.Insert(0, pushFront.Value);
                break;

            case RoomListEntriesUpdate.PushBack pushBack:
                _rooms.Add(pushBack.Value);
                break;

            case RoomListEntriesUpdate.PopFront:
                if (_rooms.Count > 0)
                {
                    _rooms.RemoveAt(0);
                }

                break;

            case RoomListEntriesUpdate.PopBack:
                if (_rooms.Count > 0)
                {
                    _rooms.RemoveAt(_rooms.Count - 1);
                }

                break;

            case RoomListEntriesUpdate.Insert insert:
                _rooms.Insert(
                    checked((int)insert.Index),
                    insert.Value);
                break;

            case RoomListEntriesUpdate.Set set:
                _rooms[checked((int)set.Index)] =
                    set.Value;
                break;

            case RoomListEntriesUpdate.Remove remove:
                {
                    var index = checked((int)remove.Index);

                    if (index >= 0 && index < _rooms.Count)
                    {
                        _rooms.RemoveAt(index);
                    }

                    break;
                }

            case RoomListEntriesUpdate.Truncate truncate:
                {
                    var length = checked((int)truncate.Length);

                    if (length < _rooms.Count)
                    {
                        _rooms.RemoveRange(
                            length,
                            _rooms.Count - length);
                    }

                    break;
                }

            case RoomListEntriesUpdate.Reset reset:
                _rooms.Clear();
                _rooms.AddRange(reset.Values);
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported room-list update type: " +
                    $"{update.GetType().Name}");
        }
    }

    private async Task PublishRoomsSafelyAsync(
        IReadOnlyList<Room> rooms)
    {
        try
        {
            await _roomsSubject.PublishAsync(rooms);
        }
        catch (ObjectDisposedException)
        {
            // The service was disposed while the native callback was active.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to publish room-list update: {exception}");
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized ||
            _entriesController is null)
        {
            throw new InvalidOperationException(
                "The room-list service has not been initialized.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initialized = false;

        /*
         * Cancel the native stream first so no further callbacks arrive while
         * the remaining native objects are being disposed.
         */
        _entriesStream?.Dispose();
        _entriesStream = null;

        _entriesListener = null;

        _entriesController?.Dispose();
        _entriesController = null;

        _entriesResult?.Dispose();
        _entriesResult = null;

        _roomList?.Dispose();
        _roomList = null;

        /*
         * Do not dispose RoomListService here if ManagedMatrixClient owns it.
         * This wrapper only borrows that service.
         */
        _roomListService = null;

        lock (_roomsLock)
        {
            _rooms.Clear();
        }

        await _roomsSubject.CompleteAsync();
        await _roomsSubject.DisposeAsync();
    }
}