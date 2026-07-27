using Dev.Naamloos.Fennec.Sdk.Events;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ObservableSpaceRoomIds : IDisposable
{
    private readonly ManagedMatrixClient _client;
    private readonly SpaceService _spaceService;
    private readonly SpaceServiceSpaceFiltersListenerCallback _listener;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SynchronizationContext? _context;

    private TaskHandle? _subscription;
    private HashSet<string> _roomIds = [];
    private bool _disposed;

    private ObservableSpaceRoomIds(
        ManagedMatrixClient client,
        SpaceService spaceService)
    {
        _client = client;
        _spaceService = spaceService;
        _listener = new SpaceServiceSpaceFiltersListenerCallback(OnUpdates);
        _context = SynchronizationContext.Current;
        _client.NativeResourcesDisposing += StopAsync;
    }

    public event EventHandler? Changed;

    public IReadOnlySet<string> RoomIds => _roomIds;

    internal static async Task<ObservableSpaceRoomIds> CreateAsync(
        ManagedMatrixClient client)
    {
        var roomIds = new ObservableSpaceRoomIds(
            client,
            await client.GetSpaceServiceAsync());
        roomIds._subscription = await roomIds._spaceService
            .SubscribeToSpaceFilters(roomIds._listener);
        await roomIds.RefreshAsync();
        return roomIds;
    }

    private void OnUpdates(SpaceFilterUpdate[] updates) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var lockHeld = false;

        try
        {
            if (_disposed)
            {
                return;
            }

            await _refreshLock.WaitAsync();
            lockHeld = true;

            if (_disposed)
            {
                return;
            }

            var roomIds = (await _spaceService.SpaceFilters())
                .SelectMany(filter => filter.Descendants)
                .ToHashSet(StringComparer.Ordinal);

            if (_roomIds.SetEquals(roomIds))
            {
                return;
            }

            _roomIds = roomIds;
            NotifyChanged();
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                Debug.WriteLine($"Could not refresh space room IDs: {exception}");
            }
        }
        finally
        {
            if (lockHeld)
            {
                _refreshLock.Release();
            }
        }
    }

    private void NotifyChanged()
    {
        if (_context is not null && !ReferenceEquals(
            SynchronizationContext.Current,
            _context))
        {
            _context.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Task StopAsync()
    {
        Dispose();
        return Task.CompletedTask;
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
        _spaceService.Dispose();
        GC.SuppressFinalize(this);
    }
}
