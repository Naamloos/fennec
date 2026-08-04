using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;
using uniffi.matrix_sdk_ui;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ObservableThreadList : ObservableModel, ThreadListEntriesListener, ThreadListPaginationStateListener, IDisposable
{
    private readonly ThreadListService _service;
    private readonly TaskHandle _itemsSubscription;
    private readonly TaskHandle _paginationSubscription;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private bool _isLoading;
    private bool _hasMore = true;
    private bool _disposed;

    public ObservableThreadList(Room room)
    {
        _service = room.ThreadListService();
        _itemsSubscription = _service.SubscribeToItemsUpdates(this);
        _paginationSubscription = _service.SubscribeToPaginationStateUpdates(this);
    }

    public ObservableRangeCollection<MatrixThreadSummary> Items { get; } = [];

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

    public async Task LoadMoreAsync()
    {
        if (_disposed || !HasMore || IsLoading) return;
        IsLoading = true;
        try
        {
            await _service.Paginate();
        }
        catch (Exception exception)
        {
            HasMore = false;
            System.Diagnostics.Debug.WriteLine($"Could not load threads: {exception}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void OnUpdate(ThreadListUpdate[] diff) => RunOnContext(() => Apply(diff));

    void ThreadListPaginationStateListener.OnUpdate(ThreadListPaginationState state) =>
        RunOnContext(() =>
        {
            IsLoading = state is ThreadListPaginationState.Loading;
            HasMore = state is not ThreadListPaginationState.Idle { EndReached: true };
        });

    private void Apply(IEnumerable<ThreadListUpdate> updates)
    {
        foreach (var update in updates)
        {
            switch (update)
            {
                case ThreadListUpdate.Append value:
                    if (Items.Count == 0)
                        Items.ReplaceAll(value.Values.Select(Map));
                    else
                        foreach (var item in value.Values)
                            Items.Add(Map(item));
                    break;
                case ThreadListUpdate.Clear:
                    Items.Clear();
                    break;
                case ThreadListUpdate.PushFront value:
                    Items.Insert(0, Map(value.Value));
                    break;
                case ThreadListUpdate.PushBack value:
                    Items.Add(Map(value.Value));
                    break;
                case ThreadListUpdate.PopFront when Items.Count > 0:
                    Items.RemoveAt(0);
                    break;
                case ThreadListUpdate.PopBack when Items.Count > 0:
                    Items.RemoveAt(Items.Count - 1);
                    break;
                case ThreadListUpdate.Insert value when value.Index <= (uint)Items.Count:
                    Items.Insert((int)value.Index, Map(value.Value));
                    break;
                case ThreadListUpdate.Set value when value.Index < (uint)Items.Count:
                    Items[(int)value.Index] = Map(value.Value);
                    break;
                case ThreadListUpdate.Remove value when value.Index < (uint)Items.Count:
                    Items.RemoveAt((int)value.Index);
                    break;
                case ThreadListUpdate.Truncate value:
                    while (Items.Count > value.Length) Items.RemoveAt(Items.Count - 1);
                    break;
                case ThreadListUpdate.Reset value:
                    Items.ReplaceAll(value.Values.Select(Map));
                    break;
            }
        }
    }

    private static MatrixThreadSummary Map(ThreadListItem item) => new(
        item.RootEvent.EventId,
        item.RootEvent.SenderProfile is ProfileDetails.Ready ready
        && !string.IsNullOrWhiteSpace(ready.DisplayName)
            ? ready.DisplayName!
            : item.RootEvent.Sender,
        (item.RootEvent.SenderProfile as ProfileDetails.Ready)?.AvatarUrl,
        Body(item.RootEvent.Content),
        Body(item.LatestEvent?.Content),
        item.NumReplies
    );

    private static string Body(TimelineItemContent? content) => content switch
    {
        TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Message message } => message.Content.Body,
        TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Sticker sticker } => sticker.Body,
        TimelineItemContent.MsgLike { Content.Kind: MsgLikeKind.Poll poll } => poll.Question,
        _ => "Timeline event",
    };

    private void RunOnContext(System.Action action)
    {
        if (_context is null || SynchronizationContext.Current == _context) action();
        else _context.Post(static state => ((System.Action)state!).Invoke(), action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _itemsSubscription.Cancel();
        _paginationSubscription.Cancel();
        _itemsSubscription.Dispose();
        _paginationSubscription.Dispose();
        _service.Dispose();
    }
}
