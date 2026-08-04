using CommunityToolkit.Maui;
using Dev.Naamloos.Fennec.Sdk;

namespace Dev.Naamloos.Fennec.App.Components;

public partial class MatrixImage : Image
{
    [BindableProperty]
    public partial string? MatrixSource { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? Client { get; set; }

    [BindableProperty]
    public partial bool IsJson { get; set; }

    [BindableProperty]
    public partial bool UseFullSize { get; set; }

    [BindableProperty]
    public partial bool UseAvatarCache { get; set; }

    [BindableProperty]
    public partial bool UseRoomImageCache { get; set; }

    [BindableProperty]
    public partial int ThumbnailWidth { get; set; } = 200;

    [BindableProperty]
    public partial int ThumbnailHeight { get; set; } = 200;

    private int _loadId;
    private CancellationTokenSource? _loadCancellation;
    private ManagedMatrixClient? _avatarChangeClient;
    private string? _sourceOverride;

    public MatrixImage()
    {
        this.BindService<ManagedMatrixClient, MatrixImage>(ClientProperty);

        Loaded += (_, _) =>
        {
            SubscribeToAvatarChanges();
            Load();
        };
        Unloaded += (_, _) =>
        {
            _loadId++;
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;
            UnsubscribeFromAvatarChanges();
            Source = null;
        };

        PropertyChanged += (_, e) =>
        {
            if (
                e.PropertyName
                is nameof(MatrixSource)
                    or nameof(Client)
                    or nameof(IsJson)
                    or nameof(UseFullSize)
                    or nameof(UseAvatarCache)
                    or nameof(UseRoomImageCache)
                    or nameof(ThumbnailWidth)
                    or nameof(ThumbnailHeight)
            )
            {
                if (e.PropertyName == nameof(MatrixSource))
                {
                    _sourceOverride = null;
                }
                if (e.PropertyName == nameof(Client))
                {
                    if (IsLoaded)
                    {
                        SubscribeToAvatarChanges();
                    }
                }
                Load();
            }
        };
    }

    private void Load()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(Load);
            return;
        }

        Source = null;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        var loadId = ++_loadId;
        if (!IsLoaded)
        {
            return;
        }

        _loadCancellation = new CancellationTokenSource();
        _ = LoadAsync(loadId, _loadCancellation.Token);
    }

    private async Task LoadAsync(int loadId, CancellationToken cancellationToken)
    {
        var client = Client;
        var source = _sourceOverride ?? MatrixSource;

        if (client is null || string.IsNullOrWhiteSpace(source))
        {
            Source = null;
            return;
        }

        try
        {
            var data =
                UseFullSize
                    ? await client.GetMediaContentAsync(source, IsJson).ConfigureAwait(false)
                : UseAvatarCache
                    ? await client
                        .GetAvatarThumbnailAsync(
                            source,
                            (ulong)Math.Max(1, ThumbnailWidth),
                            (ulong)Math.Max(1, ThumbnailHeight),
                            IsJson,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                : UseRoomImageCache
                    ? await client
                        .GetRoomImageThumbnailAsync(
                            source,
                            (ulong)Math.Max(1, ThumbnailWidth),
                            (ulong)Math.Max(1, ThumbnailHeight),
                            IsJson,
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                : await client
                    .GetThumbnailAsync(
                        source,
                        (ulong)Math.Max(1, ThumbnailWidth),
                        (ulong)Math.Max(1, ThumbnailHeight),
                        IsJson,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

            if (loadId != _loadId || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var imageSource = ImageSource.FromStream(() => new MemoryStream(data));
            await Dispatcher.DispatchAsync(() =>
            {
                if (loadId == _loadId && !cancellationToken.IsCancellationRequested)
                {
                    Source = imageSource;
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load Matrix image: {exception}");
        }
    }

    private void SubscribeToAvatarChanges()
    {
        if (_avatarChangeClient == Client)
            return;

        UnsubscribeFromAvatarChanges();
        _avatarChangeClient = Client;
        if (_avatarChangeClient is not null)
        {
            _avatarChangeClient.AvatarChanged += OnAvatarChanged;
        }
    }

    private void UnsubscribeFromAvatarChanges()
    {
        if (_avatarChangeClient is not null)
        {
            _avatarChangeClient.AvatarChanged -= OnAvatarChanged;
            _avatarChangeClient = null;
        }
    }

    private void OnAvatarChanged(string? previous, string? current)
    {
        if (
            string.IsNullOrWhiteSpace(previous)
            || !string.Equals(_sourceOverride ?? MatrixSource, previous, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(current)
        )
        {
            return;
        }

        _sourceOverride = current;
        Load();
    }
}
