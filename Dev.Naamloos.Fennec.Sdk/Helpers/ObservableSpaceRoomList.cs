using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Events;
using System.Collections.ObjectModel;
using uniffi.matrix_sdk_ffi;
using uniffi.matrix_sdk_ui;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ObservableSpaceRoomList : ObservableCollection<ManagedSpaceRoom>, IDisposable
{
    private readonly ManagedMatrixClient _client;
    private readonly SpaceService _spaceService;
    private readonly SpaceRoomList _roomList;
    private readonly SpaceRoomListEntriesListenerCallback _listener;
    private TaskHandle? _subscription;
    private readonly SynchronizationContext? _context;

    private bool _disposed;

    private ObservableSpaceRoomList(
        ManagedMatrixClient client,
        SpaceService spaceService,
        SpaceRoomList roomList,
        SpaceRoomListEntriesListenerCallback listener)
    {
        _client = client;
        _spaceService = spaceService;
        _roomList = roomList;
        _listener = listener;
        _context = SynchronizationContext.Current;
        _client.NativeResourcesDisposing += StopAsync;
    }

    internal static async Task<ObservableSpaceRoomList> CreateAsync(
        ManagedMatrixClient client,
        string spaceId)
    {
        var spaceService = await client.GetSpaceServiceAsync();
        var roomList = await spaceService.SpaceRoomList(spaceId);
        ObservableSpaceRoomList? rooms = null;
        var listener = new SpaceRoomListEntriesListenerCallback(
            updates => rooms!.ApplyUpdates(updates));
        rooms = new ObservableSpaceRoomList(client, spaceService, roomList, listener);
        rooms._subscription = await roomList.SubscribeToRoomUpdate(listener);
        await rooms.LoadAllAsync();
        return rooms;
    }

    private async Task LoadAllAsync()
    {
        while (_roomList.PaginationState() is SpaceRoomListPaginationState.Idle
        {
            EndReached: false,
        })
        {
            await _roomList.Paginate();
        }
    }

    private void ApplyUpdates(SpaceListUpdate[] updates, bool skipContext = false)
    {
        if (_disposed)
        {
            return;
        }

        if (_context is not null && !skipContext)
        {
            _context.Post(_ => ApplyUpdates(updates, true), null);
            return;
        }

        foreach (var update in updates)
        {
            switch (update)
            {
                case SpaceListUpdate.Append append:
                    foreach (var room in append.Values)
                    {
                        Add(new ManagedSpaceRoom(room));
                    }
                    break;
                case SpaceListUpdate.Clear:
                    Clear();
                    break;
                case SpaceListUpdate.PushFront pushFront:
                    Insert(0, new ManagedSpaceRoom(pushFront.Value));
                    break;
                case SpaceListUpdate.PushBack pushBack:
                    Add(new ManagedSpaceRoom(pushBack.Value));
                    break;
                case SpaceListUpdate.PopFront when Count > 0:
                    RemoveAt(0);
                    break;
                case SpaceListUpdate.PopBack when Count > 0:
                    RemoveAt(Count - 1);
                    break;
                case SpaceListUpdate.Insert insert:
                    Insert((int)insert.Index, new ManagedSpaceRoom(insert.Value));
                    break;
                case SpaceListUpdate.Set set when set.Index < Count:
                    this[(int)set.Index].Update(set.Value);
                    break;
                case SpaceListUpdate.Remove remove when remove.Index < Count:
                    RemoveAt((int)remove.Index);
                    break;
                case SpaceListUpdate.Truncate truncate:
                    while (Count > truncate.Length)
                    {
                        RemoveAt(Count - 1);
                    }
                    break;
                case SpaceListUpdate.Reset reset:
                    Clear();
                    foreach (var room in reset.Values)
                    {
                        Add(new ManagedSpaceRoom(room));
                    }
                    break;
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
        _client.NativeResourcesDisposing -= StopAsync;
        _subscription?.Cancel();
        _subscription?.Dispose();
        _roomList.Dispose();
        _spaceService.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task StopAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
