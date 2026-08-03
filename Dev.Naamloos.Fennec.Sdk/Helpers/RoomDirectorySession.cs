using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class RoomDirectorySession
    : ObservableModel,
        RoomDirectorySearchEntriesListener,
        IDisposable
{
    private readonly RoomDirectorySearch _search;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Task<TaskHandle> _subscription;
    private bool _isLoading;
    private bool _hasMore;
    private string _errorMessage = string.Empty;
    private bool _disposed;

    internal RoomDirectorySession(RoomDirectorySearch search)
    {
        _search = search;
        _subscription = search.Results(this);
    }

    public ObservableRangeCollection<RoomDescription> Rooms { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public bool HasMore
    {
        get => _hasMore;
        private set => Set(ref _hasMore, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    public async Task SearchAsync(string? query, string? server = null)
    {
        if (_disposed || IsLoading)
            return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            _ = await _subscription;
            await _search.Search(
                string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                50,
                server
            );
            HasMore = !await _search.IsAtLastPage();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadMoreAsync()
    {
        if (_disposed || IsLoading || !HasMore)
            return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            await _search.NextPage();
            HasMore = !await _search.IsAtLastPage();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OnUpdate(RoomDirectorySearchEntryUpdate[] roomEntriesUpdate) =>
        RunOnContext(() => Apply(roomEntriesUpdate));

    private void Apply(IEnumerable<RoomDirectorySearchEntryUpdate> updates)
    {
        foreach (var update in updates)
        {
            switch (update)
            {
                case RoomDirectorySearchEntryUpdate.Append value:
                    if (Rooms.Count == 0)
                        Rooms.ReplaceAll(value.Values);
                    else
                        foreach (var room in value.Values)
                            Rooms.Add(room);
                    break;
                case RoomDirectorySearchEntryUpdate.Clear:
                    Rooms.Clear();
                    break;
                case RoomDirectorySearchEntryUpdate.PushFront value:
                    Rooms.Insert(0, value.Value);
                    break;
                case RoomDirectorySearchEntryUpdate.PushBack value:
                    Rooms.Add(value.Value);
                    break;
                case RoomDirectorySearchEntryUpdate.PopFront when Rooms.Count > 0:
                    Rooms.RemoveAt(0);
                    break;
                case RoomDirectorySearchEntryUpdate.PopBack when Rooms.Count > 0:
                    Rooms.RemoveAt(Rooms.Count - 1);
                    break;
                case RoomDirectorySearchEntryUpdate.Insert value
                    when value.Index <= (uint)Rooms.Count:
                    Rooms.Insert((int)value.Index, value.Value);
                    break;
                case RoomDirectorySearchEntryUpdate.Set value when value.Index < (uint)Rooms.Count:
                    Rooms[(int)value.Index] = value.Value;
                    break;
                case RoomDirectorySearchEntryUpdate.Remove value
                    when value.Index < (uint)Rooms.Count:
                    Rooms.RemoveAt((int)value.Index);
                    break;
                case RoomDirectorySearchEntryUpdate.Truncate value:
                    while (Rooms.Count > value.Length)
                        Rooms.RemoveAt(Rooms.Count - 1);
                    break;
                case RoomDirectorySearchEntryUpdate.Reset value:
                    Rooms.ReplaceAll(value.Values);
                    break;
            }
        }
    }

    private void RunOnContext(System.Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            action();
        else
            _context.Post(static state => ((System.Action)state!).Invoke(), action);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_subscription.IsCompletedSuccessfully)
        {
            _subscription.Result.Cancel();
            _subscription.Result.Dispose();
        }
        _search.Dispose();
    }
}
