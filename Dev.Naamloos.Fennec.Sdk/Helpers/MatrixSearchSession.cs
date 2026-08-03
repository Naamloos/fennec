using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

public sealed class MatrixSearchSession(
    GlobalSearchIterator iterator,
    Func<TimelineItemContent, string> body
) : ObservableModel, IDisposable
{
    private bool _isLoading;
    private bool _hasMore = true;
    private string _errorMessage = string.Empty;
    private bool _disposed;

    public ObservableRangeCollection<MatrixSearchResult> Results { get; } = [];

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

    public async Task LoadMoreAsync()
    {
        if (_disposed || IsLoading || !HasMore) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var batch = await iterator.NextEvents();
            HasMore = batch is { Length: > 0 };
            var results = (batch ?? []).Select(result =>
            {
                using (result)
                {
                    return new MatrixSearchResult(
                        result.RoomId,
                        result.Result.EventId,
                        result.Result.SenderProfile is ProfileDetails.Ready ready
                        && !string.IsNullOrWhiteSpace(ready.DisplayName)
                            ? ready.DisplayName!
                            : result.Result.Sender,
                        result.Result.Sender,
                        body(result.Result.Content),
                        DateTimeOffset.FromUnixTimeMilliseconds(
                            (long)Math.Min(result.Result.Timestamp, 253402300799999UL)
                        ).ToLocalTime().ToString("g")
                    );
                }
            }).ToArray();
            if (Results.Count == 0)
                Results.ReplaceAll(results);
            else
                foreach (var result in results)
                    Results.Add(result);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        iterator.Dispose();
    }
}
