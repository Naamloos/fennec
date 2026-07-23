using Dev.Naamloos.Fennec.Sdk.Events;
using System.Collections.ObjectModel;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class ObservableTimeline :
    ObservableCollection<TimelineItem>,
    IDisposable
{
  private const ushort DefaultPageSize = 50;
  private const int DefaultInitialItemTarget = 50;

  private readonly Timeline _timeline;
  private readonly SynchronizationContext? _synchronizationContext;

  private TimelineListenerCallback? _listener;
  private TaskHandle? _listenerHandle;

  private bool _isLoadingHistory;
  private bool _hasReachedStart;
  private bool _disposed;

  private ObservableTimeline(Timeline timeline)
  {
    _timeline = timeline;
    _synchronizationContext = SynchronizationContext.Current;
  }

  public Timeline Timeline
  {
    get
    {
      ThrowIfDisposed();
      return _timeline;
    }
  }

  public bool IsLoadingHistory
  {
    get => _isLoadingHistory;
    private set
    {
      if (_isLoadingHistory == value)
      {
        return;
      }

      _isLoadingHistory = value;

      OnPropertyChanged(
          new System.ComponentModel.PropertyChangedEventArgs(
              nameof(IsLoadingHistory)));
    }
  }

  public bool HasReachedStart
  {
    get => _hasReachedStart;
    private set
    {
      if (_hasReachedStart == value)
      {
        return;
      }

      _hasReachedStart = value;

      OnPropertyChanged(
          new System.ComponentModel.PropertyChangedEventArgs(
              nameof(HasReachedStart)));
    }
  }

  internal static async Task<ObservableTimeline> CreateAsync(
      Timeline timeline,
      ushort initialPageSize = DefaultPageSize,
      int initialItemTarget = DefaultInitialItemTarget,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(timeline);

    if (initialPageSize == 0)
    {
      throw new ArgumentOutOfRangeException(
          nameof(initialPageSize),
          "The page size must be greater than zero.");
    }

    if (initialItemTarget < 0)
    {
      throw new ArgumentOutOfRangeException(
          nameof(initialItemTarget));
    }

    cancellationToken.ThrowIfCancellationRequested();

    var observableTimeline =
        new ObservableTimeline(timeline);

    await observableTimeline.InitializeListener();

    await observableTimeline.LoadInitialHistoryAsync(
        initialPageSize,
        initialItemTarget,
        cancellationToken);

    return observableTimeline;
  }

  private async Task InitializeListener()
  {
    ThrowIfDisposed();

        _listener = TimelineListenerCallback.Create(diff => UpdateEntries(diff));

    /*
     * In the generated Matrix FFI bindings, AddListener commonly returns
     * the TaskHandle directly. Do not await the lifetime of the listener
     * itself; it remains active until its handle is cancelled.
     */
    _listenerHandle =
        await _timeline.AddListener(_listener);
  }

  private async Task LoadInitialHistoryAsync(
      ushort pageSize,
      int targetItemCount,
      CancellationToken cancellationToken)
  {
    if (targetItemCount == 0)
    {
      return;
    }

    /*
     * The initial listener Reset may be posted to the UI synchronization
     * context. Yield once so that update can be applied before deciding
     * how much history still needs to be loaded.
     */
    await Task.Yield();

    while (!_disposed &&
           !HasReachedStart &&
           Count < targetItemCount)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var countBeforePagination = Count;

      var reachedStart = await LoadMoreHistoryAsync(
          pageSize,
          cancellationToken);

      if (reachedStart)
      {
        break;
      }

      /*
       * Timeline diffs can arrive through the listener immediately
       * after pagination completes. Give the callback an opportunity
       * to apply them.
       */
      await Task.Yield();

      /*
       * Prevent an infinite loop if the SDK reports that more history
       * exists but does not emit any additional items.
       */
      if (Count <= countBeforePagination)
      {
        Debug.WriteLine(
            "Timeline pagination returned without adding items.");

        break;
      }
    }
  }

  public async Task<bool> LoadMoreHistoryAsync(
      ushort eventCount = DefaultPageSize,
      CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (eventCount == 0)
    {
      throw new ArgumentOutOfRangeException(
          nameof(eventCount),
          "The event count must be greater than zero.");
    }

    if (HasReachedStart)
    {
      return true;
    }

    if (IsLoadingHistory)
    {
      return HasReachedStart;
    }

    IsLoadingHistory = true;

    try
    {
      cancellationToken.ThrowIfCancellationRequested();

      /*
       * PaginateBackwards requests older events. The resulting
       * timeline changes arrive through UpdateEntries as PushFront,
       * Insert, Reset or other TimelineDiff variants.
       *
       * In these bindings the returned bool indicates whether the
       * beginning of the timeline was reached.
       */
      var reachedStart =
          await _timeline.PaginateBackwards(eventCount);

      cancellationToken.ThrowIfCancellationRequested();

      if (reachedStart)
      {
        HasReachedStart = true;
      }

      return reachedStart;
    }
    finally
    {
      IsLoadingHistory = false;
    }
  }

  private void UpdateEntries(TimelineDiff[] updates)
  {
    if (_disposed || updates.Length == 0)
    {
      return;
    }

    RunOnCapturedContext(() =>
    {
      if (_disposed)
      {
        return;
      }

      foreach (var update in updates)
      {
        ApplyUpdate(update);
      }
    });
  }

  private void ApplyUpdate(TimelineDiff update)
  {
    switch (update)
    {
      case TimelineDiff.Append append:
        ApplyAppend(append);
        break;

      case TimelineDiff.Clear:
        Clear();
        break;

      case TimelineDiff.PushFront pushFront:
        Insert(0, pushFront.Value);
        break;

      case TimelineDiff.PushBack pushBack:
        Add(pushBack.Value);
        break;

      case TimelineDiff.PopFront:
        if (Count > 0)
        {
          RemoveAt(0);
        }

        break;

      case TimelineDiff.PopBack:
        if (Count > 0)
        {
          RemoveAt(Count - 1);
        }

        break;

      case TimelineDiff.Insert insert:
        ApplyInsert(insert);
        break;

      case TimelineDiff.Set set:
        ApplySet(set);
        break;

      case TimelineDiff.Remove remove:
        ApplyRemove(remove);
        break;

      case TimelineDiff.Truncate truncate:
        ApplyTruncate(truncate);
        break;

      case TimelineDiff.Reset reset:
        ApplyReset(reset);
        break;

      default:
        Debug.WriteLine(
            $"Unhandled timeline diff: " +
            update.GetType().FullName);
        break;
    }
  }

  private void ApplyAppend(TimelineDiff.Append append)
  {
    foreach (var item in append.Values)
    {
      Add(item);
    }
  }

  private void ApplyInsert(TimelineDiff.Insert insert)
  {
    var index = checked((int)insert.Index);

    if (index < 0 || index > Count)
    {
      Debug.WriteLine(
          $"Ignoring timeline insert at invalid index {index}. " +
          $"Current count: {Count}.");

      return;
    }

    Insert(index, insert.Value);
  }

  private void ApplySet(TimelineDiff.Set set)
  {
    var index = checked((int)set.Index);

    if (index < 0 || index >= Count)
    {
      Debug.WriteLine(
          $"Ignoring timeline set at invalid index {index}. " +
          $"Current count: {Count}.");

      return;
    }

    this[index] = set.Value;
  }

  private void ApplyRemove(TimelineDiff.Remove remove)
  {
    var index = checked((int)remove.Index);

    if (index < 0 || index >= Count)
    {
      Debug.WriteLine(
          $"Ignoring timeline remove at invalid index {index}. " +
          $"Current count: {Count}.");

      return;
    }

    RemoveAt(index);
  }

  private void ApplyTruncate(TimelineDiff.Truncate truncate)
  {
    var length = checked((int)truncate.Length);

    while (Count > length)
    {
      RemoveAt(Count - 1);
    }
  }

  private void ApplyReset(TimelineDiff.Reset reset)
  {
    /*
     * Reset is emitted when the SDK replaces its current complete
     * timeline snapshot.
     *
     * Consumers should reconcile these collection notifications by
     * stable timeline-item ID instead of clearing and rebuilding their
     * own view model collection.
     */
    Clear();

    foreach (var item in reset.Values)
    {
      Add(item);
    }
  }

  private void RunOnCapturedContext(
      System.Action action)
  {
    if (_disposed)
    {
      return;
    }

    if (_synchronizationContext is null ||
        SynchronizationContext.Current ==
        _synchronizationContext)
    {
      action();
      return;
    }

    _synchronizationContext.Post(
        static state =>
        {
          var callback = (System.Action)state!;
          callback();
        },
        action);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;

    try
    {
      _listenerHandle?.Cancel();
    }
    catch (Exception exception)
    {
      Debug.WriteLine(
          $"Failed to cancel timeline listener: {exception}");
    }

    _listenerHandle?.Dispose();

    _listenerHandle = null;
    _listener = null;

    GC.SuppressFinalize(this);
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
