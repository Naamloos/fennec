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

    private int _loadId;
    private CancellationTokenSource? _loadCancellation;

    public MatrixImage()
    {
        this.BindService<ManagedMatrixClient, MatrixImage>(ClientProperty);

        Loaded += (_, _) => Load();
        Unloaded += (_, _) =>
        {
            _loadId++;
            _loadCancellation?.Cancel();
            Source = null;
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MatrixSource)
                or nameof(Client)
                or nameof(IsJson))
            {
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
        var source = MatrixSource;

        if (client is null || string.IsNullOrWhiteSpace(source))
        {
            Source = null;
            return;
        }

        try
        {
            var data = await client.GetThumbnailAsync(
                source,
                200,
                200,
                IsJson,
                cancellationToken);

            if (loadId != _loadId)
            {
                return;
            }

            await Dispatcher.DispatchAsync(() =>
            {
                Source = ImageSource.FromStream(
                    () => new MemoryStream(data));
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
}
