using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Converters;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.App.Converters;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class SpaceRoomListView : ContentView, IDisposable
{
    private readonly CollectionView _roomsView;
    private readonly Dictionary<string, ObservableSpaceRoomList> _roomLists = [];
    private int _loadVersion;
    private bool _disposed;

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial ManagedRoom? SelectedRoom { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSpaceIdChanged))]
    public partial string? SpaceId { get; set; }

    [BindableProperty]
    public partial string? SpaceName { get; set; }

    public SpaceRoomListView()
    {
        this.BindService<ManagedMatrixClient, SpaceRoomListView>(MatrixClientProperty);

        _roomsView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            EmptyView = CreateEmptyView("No rooms in this space."),
            ItemTemplate = CreateRoomTemplate(),
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children =
            {
                new Label
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 16,
                    LineBreakMode = LineBreakMode.TailTruncation,
                }
                    .Bind(Label.TextProperty, nameof(SpaceName), source: this)
                    .Bind(
                        IsVisibleProperty,
                        nameof(SpaceName),
                        converter: new IsStringNotNullOrEmptyConverter(),
                        source: this
                    )
                    .Row(0),
                _roomsView.Row(1),
            },
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs eventArgs) => Reload();

    private static void OnSpaceIdChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((SpaceRoomListView)bindable).Reload();

    private async void Reload()
    {
        if (_disposed || !IsLoaded || MatrixClient is null || string.IsNullOrWhiteSpace(SpaceId))
        {
            return;
        }

        if (_roomLists.TryGetValue(SpaceId, out var cachedRooms))
        {
            _roomsView.ItemsSource = cachedRooms;
            return;
        }

        var loadVersion = ++_loadVersion;
        _roomsView.ItemsSource = null;

        try
        {
            var rooms = await MatrixClient.GetObservableSpaceRoomListAsync(SpaceId);
            if (_disposed || loadVersion != _loadVersion)
            {
                rooms.Dispose();
                return;
            }

            _roomLists.Add(SpaceId, rooms);
            _roomsView.ItemsSource = rooms;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load space {SpaceId}: {exception}");
        }
    }

    private static View CreateEmptyView(string message) =>
        new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = message, FontSize = 16 },
            },
        };

    private DataTemplate CreateRoomTemplate() =>
        new(() =>
        {
            var row = new Grid
            {
                Padding = new Thickness(0, 8, 10, 8),
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(6),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children =
                {
                    new Border
                    {
                        WidthRequest = 6,
                        HeightRequest = 6,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                        {
                            CornerRadius = 3,
                        },
                        VerticalOptions = LayoutOptions.Center,
                    }
                        .Bind<Border, ManagedRoom?, string?, bool>(
                            IsVisibleProperty,
                            new Binding(nameof(SelectedRoom), source: this),
                            new Binding(nameof(ManagedSpaceRoom.Id)),
                            convert: static values => values.Item1?.Id == values.Item2
                        )
                        .DynamicResource(BackgroundColorProperty, "Primary")
                        .Column(0),
                    new RoomAvatar { Size = 40 }
                        .Bind(RoomAvatar.AvatarUrlProperty, nameof(ManagedSpaceRoom.AvatarUrl))
                        .Bind(RoomAvatar.DisplayNameProperty, nameof(ManagedSpaceRoom.DisplayName))
                        .Column(1),
                    new Label
                    {
                        FontSize = 16,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Fill,
                        LineBreakMode = LineBreakMode.TailTruncation,
                    }
                        .Bind(Label.TextProperty, nameof(ManagedSpaceRoom.DisplayName))
                        .Column(2),
                    new Label
                    {
                        Text = "Join",
                        FontSize = 12,
                        Opacity = .7,
                        VerticalOptions = LayoutOptions.Center,
                    }
                        .Bind(
                            IsVisibleProperty,
                            nameof(ManagedSpaceRoom.IsJoined),
                            converter: new BooleanInverterConverter()
                        )
                        .Column(3),
                    new Label
                    {
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        VerticalOptions = LayoutOptions.Center,
                    }
                        .Bind(Label.TextProperty, nameof(ManagedSpaceRoom.UnreadLabel))
                        .Bind(IsVisibleProperty, nameof(ManagedSpaceRoom.HasUnread))
                        .DynamicResource(Label.TextColorProperty, "Primary")
                        .Column(3),
                },
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await OpenRoomAsync(row.BindingContext as ManagedSpaceRoom);
            row.GestureRecognizers.Add(tap);
            return row;
        });

    private async Task OpenRoomAsync(ManagedSpaceRoom? room)
    {
        if (room is null || MatrixClient is null)
        {
            return;
        }

        try
        {
            SelectedRoom = await MatrixClient.OpenSpaceRoomAsync(room);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not open space room {room.Id}: {exception}");
        }
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var rooms in _roomLists.Values)
        {
            rooms.Dispose();
        }

        _roomLists.Clear();
    }
}
