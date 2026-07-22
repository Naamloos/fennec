using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Dev.Naamloos.Fennec.Sdk.NativeEventHandler;
using System.Diagnostics;
using System.Text.Json;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Pages;

public sealed class RoomList : ContentPage
{
    private readonly ManagedMatrixClient _matrixClient;
    private readonly CollectionView _roomsCollectionView;

    public ObservableRoomList? ObservableRoomList
    {
        get
        {
            return _observableRoomList;
        }
        private set
        {
            _observableRoomList = value; 
            OnPropertyChanged(nameof(ObservableRoomList));
        }
    }
    private ObservableRoomList? _observableRoomList = null;

    public RoomList(ManagedMatrixClient matrixClient)
    {
        this.BindingContext = this;

        _matrixClient = matrixClient;

        Title = "Rooms";

        _roomsCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            EmptyView = CreateEmptyView(),
            ItemTemplate = new DataTemplate(CreateRoomItem),
        }.Bind(CollectionView.ItemsSourceProperty, nameof(ObservableRoomList));

        Content = new Grid
        {
            Children =
            {
                _roomsCollectionView,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (ObservableRoomList is null)
        {
            ObservableRoomList = await _matrixClient.GetObservableRoomListAsync();
        }
    }

    protected override void OnDisappearing()
    {
        ObservableRoomList?.Dispose();
        ObservableRoomList = null;

        base.OnDisappearing();
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
        }.Bind(Label.TextProperty, nameof(RoomEntry.Name), BindingMode.OneWay);

        var border = new Border
        {
            Margin = new Thickness(10, 5),
            Padding = 12,
            Content = nameLabel,
        };

        return border;
    }
}