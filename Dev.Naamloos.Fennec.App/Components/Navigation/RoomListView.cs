using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomListView : ContentView, IDisposable
{
    private readonly CollectionView _roomsView;
    private readonly ObservableCollection<ManagedRoom> _visibleRooms = [];
    private ObservableRoomList? _rooms;
    private ObservableSpaceRoomIds? _spaceRoomIds;
    private bool _disposed;

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial ManagedRoom? SelectedRoom { get; set; }

    [BindableProperty]
    public partial string SectionTitle { get; set; } = string.Empty;

    [BindableProperty(PropertyChangedMethodName = nameof(OnFilterChanged))]
    public partial RoomListEntriesDynamicFilterKind? Filter { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnExcludeSpaceRoomsChanged))]
    public partial bool ExcludeSpaceRooms { get; set; }

    public RoomListView()
    {
        this.BindService<ManagedMatrixClient, RoomListView>(MatrixClientProperty);

        _roomsView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemsSource = _visibleRooms,
            EmptyView = CreateEmptyView("No rooms available."),
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
                    .Bind(Label.TextProperty, nameof(SectionTitle), source: this)
                    .Row(0),
                _roomsView.Row(1),
            },
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (_disposed || _rooms is not null || MatrixClient is null || Filter is null)
        {
            return;
        }

        try
        {
            if (ExcludeSpaceRooms)
            {
                await StartSpaceRoomIdsAsync();
            }

            _rooms = await MatrixClient.GetObservableRoomListAsync(Filter);
            _rooms.CaptureCurrentContext();
            _rooms.CollectionChanged += OnRoomsChanged;
            SyncRooms();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load rooms: {exception}");
        }
    }

    private static void OnFilterChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is RoomListEntriesDynamicFilterKind filter)
        {
            ((RoomListView)bindable)._rooms?.SetFilter(filter);
        }
    }

    private static void OnExcludeSpaceRoomsChanged(
        BindableObject bindable,
        object oldValue,
        object newValue
    ) => ((RoomListView)bindable).UpdateSpaceRoomFilter();

    private async void UpdateSpaceRoomFilter()
    {
        if (ExcludeSpaceRooms)
        {
            await StartSpaceRoomIdsAsync();
        }
        else
        {
            StopSpaceRoomIds();
        }

        SyncRooms();
    }

    private async Task StartSpaceRoomIdsAsync()
    {
        if (_spaceRoomIds is not null || MatrixClient is null)
        {
            return;
        }

        _spaceRoomIds = await MatrixClient.GetObservableSpaceRoomIdsAsync();
        _spaceRoomIds.Changed += OnSpaceRoomIdsChanged;
    }

    private void StopSpaceRoomIds()
    {
        if (_spaceRoomIds is null)
        {
            return;
        }

        _spaceRoomIds.Changed -= OnSpaceRoomIdsChanged;
        _spaceRoomIds.Dispose();
        _spaceRoomIds = null;
    }

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) =>
        SyncRooms();

    private void OnSpaceRoomIdsChanged(object? sender, EventArgs eventArgs) => SyncRooms();

    private void SyncRooms()
    {
        _visibleRooms.Clear();

        if (_rooms is null)
        {
            return;
        }

        foreach (var room in _rooms)
        {
            if (
                !ExcludeSpaceRooms
                || _spaceRoomIds is null
                || !_spaceRoomIds.RoomIds.Contains(room.Id ?? string.Empty)
            )
            {
                _visibleRooms.Add(room);
            }
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
                            new Binding(nameof(ManagedRoom.Id)),
                            convert: static values => values.Item1?.Id == values.Item2
                        )
                        .DynamicResource(BackgroundColorProperty, "Primary")
                        .Column(0),
                    new RoomAvatar { Size = 40 }
                        .Bind(RoomAvatar.AvatarUrlProperty, nameof(ManagedRoom.AvatarUrl))
                        .Bind(RoomAvatar.DisplayNameProperty, nameof(ManagedRoom.DisplayName))
                        .Column(1),
                    new Label
                    {
                        FontSize = 16,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Fill,
                        LineBreakMode = LineBreakMode.TailTruncation,
                    }
                        .Bind(Label.TextProperty, nameof(ManagedRoom.DisplayName))
                        .Column(2),
                    new Label
                    {
                        FontSize = 12,
                        FontAttributes = FontAttributes.Bold,
                        VerticalOptions = LayoutOptions.Center,
                    }
                        .Bind(Label.TextProperty, nameof(ManagedRoom.UnreadLabel))
                        .Bind(IsVisibleProperty, nameof(ManagedRoom.HasUnread))
                        .DynamicResource(Label.TextColorProperty, "Primary")
                        .Column(3),
                },
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SelectedRoom = row.BindingContext as ManagedRoom;
            row.GestureRecognizers.Add(tap);
            return row;
        });

    private void OnUnloaded(object? sender, EventArgs eventArgs) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_rooms is not null)
        {
            _rooms.CollectionChanged -= OnRoomsChanged;
            _rooms.Dispose();
        }

        _rooms = null;
        StopSpaceRoomIds();
    }
}
