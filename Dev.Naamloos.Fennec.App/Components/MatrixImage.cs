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

    public MatrixImage()
    {
        this.BindService<ManagedMatrixClient, MatrixImage>(ClientProperty);

        Loaded += (_, _) => Load();
        Unloaded += (_, _) => _loadId++;

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

    private void Load() => _ = LoadAsync(++_loadId);

    private async Task LoadAsync(int loadId)
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
                IsJson);

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
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to load Matrix image: {exception}");
        }
    }
}
