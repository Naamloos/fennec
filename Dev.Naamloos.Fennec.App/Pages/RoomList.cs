using Dev.Naamloos.Fennec.Sdk;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed class RoomList : ContentPage
{
    private readonly ManagedMatrixClient _matrixClient;
    private readonly CollectionView _roomsCollectionView;

    public RoomList(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        Title = "Rooms";

        _roomsCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = CreateEmptyView(),
            ItemTemplate = new DataTemplate(CreateRoomItem),
        };

        Content = new Grid
        {
            Children =
            {
                _roomsCollectionView,
            },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        RefreshRooms();
    }

    private void RefreshRooms()
    {
        _roomsCollectionView.ItemsSource = _matrixClient
            .GetRooms()
            .Select(static room => new RoomRecord(room))
            .ToArray();
    }

    private static View CreateEmptyView()
    {
        return new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Spacing = 8,
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "Loading rooms…",
                },
            },
        };
    }

    private static View CreateRoomItem()
    {
        var nameLabel = new Label
        {
            FontSize = 18,
        };

        nameLabel.SetBinding(
            Label.TextProperty,
            nameof(RoomRecord.Name));

        var border = new Border
        {
            Margin = new Thickness(10, 5),
            Padding = 12,
            Content = nameLabel,
        };

        border.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "SurfaceContainer");

        return border;
    }

    private sealed class RoomRecord(Room room)
    {
        public Room NativeRoom { get; } = room;

        public string Name =>
            room.DisplayName() ?? room.Id();

        public override string ToString() => Name;
    }
}