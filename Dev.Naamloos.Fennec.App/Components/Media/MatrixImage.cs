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
            UnsubscribeFromAvatarChanges();
            Source = null;
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MatrixSource)
                or nameof(Client)
                or nameof(IsJson)
                or nameof(UseFullSize)
                or nameof(UseAvatarCache))
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
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        _ = LoadAsync(++_loadId, _loadCancellation.Token);
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
            var data = UseFullSize
                ? await client.GetMediaContentAsync(source, IsJson).ConfigureAwait(false)
                : UseAvatarCache
                    ? await client.GetAvatarThumbnailAsync(
                        source,
                        200,
                        200,
                        IsJson,
                        cancellationToken).ConfigureAwait(false)
                : await client.GetThumbnailAsync(
                    source,
                    200,
                    200,
                    IsJson,
                    cancellationToken).ConfigureAwait(false);

            if (loadId != _loadId)
            {
                return;
            }

            var imageSource = ImageSource.FromStream(() => new MemoryStream(data));
            await Dispatcher.DispatchAsync(() =>
            {
                Source = imageSource;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to load Matrix image: {exception}");
        }
    }

    private void SubscribeToAvatarChanges()
    {
        if (_avatarChangeClient == Client) return;

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
        if (!string.Equals(_sourceOverride ?? MatrixSource, previous, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        _sourceOverride = current;
        Load();
    }
}
