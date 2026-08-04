using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class TieredSidebar : ContentView, IDisposable
{
    private readonly CollectionView _sectionsView;
    private readonly CollectionView _spacesView;
    private readonly RoomListView _roomListView;
    private readonly SpaceRoomListView _spaceRoomListView;
    private readonly SidebarSection[] _sections =
    [
        new(
            "Favorites",
            MaterialIcons.Star,
            new RoomListEntriesDynamicFilterKind.All([
                new RoomListEntriesDynamicFilterKind.Favourite(),
                new RoomListEntriesDynamicFilterKind.NonSpace(),
            ])
        ),
        new(
            "DMs",
            MaterialIcons.Person,
            new RoomListEntriesDynamicFilterKind.All([
                new RoomListEntriesDynamicFilterKind.NonSpace(),
                new RoomListEntriesDynamicFilterKind.Category(RoomListFilterCategory.People),
            ])
        ),
        new("Invites", MaterialIcons.Mail, new RoomListEntriesDynamicFilterKind.Invite()),
        new(
            "Rooms",
            MaterialIcons.Tag,
            new RoomListEntriesDynamicFilterKind.All([
                new RoomListEntriesDynamicFilterKind.NonSpace(),
                new RoomListEntriesDynamicFilterKind.Category(RoomListFilterCategory.Group),
            ])
        ),
    ];

    private ObservableRoomList? _spaces;
    private ObservableRoomList? _unreadRooms;
    private ObservableSpaceRoomIds? _spaceRoomIds;
    private readonly HashSet<ManagedRoom> _trackedUnreadRooms = [];
    private bool _spaceUnreadUpdateQueued;
    private bool _disposed;

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial ManagedRoom? SelectedRoom { get; set; }

    [BindableProperty]
    public partial string? SelectedSpaceId { get; set; }

    public TieredSidebar()
    {
        this.BindService<ManagedMatrixClient, TieredSidebar>(MatrixClientProperty);

        _sectionsView = CreateSectionsView();
        _spacesView = CreateSpacesView();
        _roomListView = new RoomListView
        {
            Filter = _sections[3].Filter,
            ExcludeSpaceRooms = true,
            SectionTitle = _sections[3].Title,
        }.Bind(
            RoomListView.SelectedRoomProperty,
            nameof(SelectedRoom),
            BindingMode.TwoWay,
            source: this
        );
        _spaceRoomListView = new SpaceRoomListView { IsVisible = false }.Bind(
            SpaceRoomListView.SelectedRoomProperty,
            nameof(SelectedRoom),
            BindingMode.TwoWay,
            source: this
        );

        Build();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (_disposed || _spaces is not null || MatrixClient is null)
        {
            return;
        }

        try
        {
            _spaces = await MatrixClient.GetObservableRoomListAsync(
                new RoomListEntriesDynamicFilterKind.All([
                    new RoomListEntriesDynamicFilterKind.Space(),
                    new RoomListEntriesDynamicFilterKind.Joined(),
                ])
            );
            _spaces.CaptureCurrentContext();
            _spacesView.ItemsSource = _spaces;
            _spaces.CollectionChanged += OnSpacesChanged;

            _unreadRooms = await MatrixClient.GetObservableRoomListAsync(
                new RoomListEntriesDynamicFilterKind.All([
                    new RoomListEntriesDynamicFilterKind.Joined(),
                    new RoomListEntriesDynamicFilterKind.NonSpace(),
                ])
            );
            _unreadRooms.CaptureCurrentContext();
            _unreadRooms.CollectionChanged += OnUnreadRoomsChanged;
            TrackUnreadRooms(_unreadRooms);

            _spaceRoomIds = await MatrixClient.GetObservableSpaceRoomIdsAsync();
            _spaceRoomIds.Changed += OnSpaceRoomIdsChanged;
            QueueSpaceUnreadUpdate();
            ShowSection(_sections[3]);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load spaces: {exception}");
        }
    }

    private CollectionView CreateSectionsView() =>
        new()
        {
            ItemsSource = _sections,
            SelectionMode = SelectionMode.None,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemTemplate = new DataTemplate(() =>
            {
                var icon = new MauiIcon { IconSize = 20, HorizontalOptions = LayoutOptions.Center };
                var row = new Grid
                {
                    Padding = new Thickness(0, 8, 4, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(4),
                        new ColumnDefinition(GridLength.Star),
                    },
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 3,
                            HeightRequest = 24,
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                            {
                                CornerRadius = 2,
                            },
                            VerticalOptions = LayoutOptions.Center,
                        }
                            .Bind(IsVisibleProperty, nameof(SidebarSection.IsSelected))
                            .DynamicResource(BackgroundColorProperty, "Primary")
                            .Column(0),
                        icon.Column(1),
                    },
                };

                row.BindingContextChanged += (_, _) =>
                {
                    if (row.BindingContext is SidebarSection section)
                    {
                        icon.Icon = section.Icon;
                    }
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => ShowSection(row.BindingContext as SidebarSection);
                row.GestureRecognizers.Add(tap);
                return row;
            }),
        };

    private CollectionView CreateSpacesView() =>
        new()
        {
            SelectionMode = SelectionMode.None,
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new Grid
                {
                    Padding = new Thickness(0, 8, 4, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(4),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    },
                    Children =
                    {
                        new Border
                        {
                            WidthRequest = 3,
                            HeightRequest = 26,
                            StrokeThickness = 0,
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                            {
                                CornerRadius = 2,
                            },
                            VerticalOptions = LayoutOptions.Center,
                        }
                            .Bind<Border, string?, string?, bool>(
                                IsVisibleProperty,
                                new Binding(nameof(SelectedSpaceId), source: this),
                                new Binding(nameof(ManagedRoom.Id)),
                                convert: static values => values.Item1 == values.Item2
                            )
                            .DynamicResource(BackgroundColorProperty, "Primary")
                            .Column(0),
                        new RoomAvatar { Size = 32 }
                            .Bind(RoomAvatar.AvatarUrlProperty, nameof(ManagedRoom.AvatarUrl))
                            .Bind(RoomAvatar.DisplayNameProperty, nameof(ManagedRoom.DisplayName))
                            .Column(1),
                        new Label
                        {
                            FontSize = 12,
                            FontAttributes = FontAttributes.Bold,
                            VerticalOptions = LayoutOptions.Center,
                        }
                            .Bind(Label.TextProperty, nameof(ManagedRoom.UnreadLabel))
                            .Bind(IsVisibleProperty, nameof(ManagedRoom.HasUnread))
                            .DynamicResource(Label.TextColorProperty, "Primary")
                            .Column(2),
                    },
                };

                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => ShowSpace(row.BindingContext as ManagedRoom);
                row.GestureRecognizers.Add(tap);
                return row;
            }),
        };

    private void Build()
    {
        Content = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(68), new ColumnDefinition(GridLength.Star) },
            Children =
            {
                new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Star),
                    },
                    Children = { _sectionsView.Row(0), _spacesView.Row(1) },
                }.Column(0),
                new Grid { Children = { _roomListView, _spaceRoomListView } }.Column(1),
            },
        };
    }

    private void ShowSection(SidebarSection? section)
    {
        if (section is null)
        {
            return;
        }

        foreach (var candidate in _sections)
        {
            candidate.IsSelected = candidate == section;
        }

        SelectedSpaceId = null;
        _spaceRoomListView.IsVisible = false;
        _roomListView.IsVisible = true;
        _roomListView.SectionTitle = section.Title;
        _roomListView.Filter = section.Filter;
        _roomListView.ExcludeSpaceRooms = section.Title == "Rooms";
    }

    private void ShowSpace(ManagedRoom? space)
    {
        if (space is null)
        {
            return;
        }

        foreach (var section in _sections)
        {
            section.IsSelected = false;
        }

        SelectedSpaceId = space.Id;
        _roomListView.IsVisible = false;
        _spaceRoomListView.IsVisible = true;
        _spaceRoomListView.SpaceName = space.DisplayName ?? space.Id;
        _spaceRoomListView.SpaceId = space.Id;
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs) => Dispose();

    private void OnSpacesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs) =>
        QueueSpaceUnreadUpdate();

    private void OnUnreadRoomsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.Action is NotifyCollectionChangedAction.Reset)
        {
            foreach (var room in _trackedUnreadRooms)
            {
                room.PropertyChanged -= OnUnreadRoomChanged;
            }
            _trackedUnreadRooms.Clear();
            if (_unreadRooms is not null)
            {
                TrackUnreadRooms(_unreadRooms);
            }
        }
        else
        {
            if (eventArgs.OldItems is not null)
            {
                TrackUnreadRooms(eventArgs.OldItems.OfType<ManagedRoom>(), subscribe: false);
            }
            if (eventArgs.NewItems is not null)
            {
                TrackUnreadRooms(eventArgs.NewItems.OfType<ManagedRoom>());
            }
        }

        QueueSpaceUnreadUpdate();
    }

    private void TrackUnreadRooms(IEnumerable<ManagedRoom> rooms, bool subscribe = true)
    {
        foreach (var room in rooms)
        {
            if (subscribe && _trackedUnreadRooms.Add(room))
            {
                room.PropertyChanged += OnUnreadRoomChanged;
            }
            else if (!subscribe && _trackedUnreadRooms.Remove(room))
            {
                room.PropertyChanged -= OnUnreadRoomChanged;
            }
        }
    }

    private void OnUnreadRoomChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(ManagedRoom.UnreadCount)
                or nameof(ManagedRoom.HasUnread)
        )
        {
            QueueSpaceUnreadUpdate();
        }
    }

    private void OnSpaceRoomIdsChanged(object? sender, EventArgs eventArgs) =>
        QueueSpaceUnreadUpdate();

    private void QueueSpaceUnreadUpdate()
    {
        if (_spaceUnreadUpdateQueued || _disposed)
        {
            return;
        }

        _spaceUnreadUpdateQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _spaceUnreadUpdateQueued = false;
            UpdateSpaceUnread();
        });
    }

    private void UpdateSpaceUnread()
    {
        if (_spaces is null || _unreadRooms is null || _spaceRoomIds is null)
        {
            return;
        }

        foreach (var space in _spaces)
        {
            var roomIds = _spaceRoomIds.GetDescendantRoomIds(space.Id ?? string.Empty);
            var rooms = _unreadRooms.Where(room => roomIds.Contains(room.Id ?? string.Empty));
            space.UpdateUnread(
                rooms.Aggregate(0UL, (count, room) => count + room.UnreadCount),
                rooms.Any(room => room.HasUnread)
            );
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var room in _trackedUnreadRooms)
        {
            room.PropertyChanged -= OnUnreadRoomChanged;
        }
        _trackedUnreadRooms.Clear();
        if (_spaces is not null)
        {
            _spaces.CollectionChanged -= OnSpacesChanged;
        }
        if (_unreadRooms is not null)
        {
            _unreadRooms.CollectionChanged -= OnUnreadRoomsChanged;
            _unreadRooms.Dispose();
            _unreadRooms = null;
        }
        if (_spaceRoomIds is not null)
        {
            _spaceRoomIds.Changed -= OnSpaceRoomIdsChanged;
            _spaceRoomIds.Dispose();
            _spaceRoomIds = null;
        }
        _spaces?.Dispose();
        _spaces = null;
        _roomListView.Dispose();
        _spaceRoomListView.Dispose();
    }

    private sealed class SidebarSection(
        string title,
        MaterialIcons icon,
        RoomListEntriesDynamicFilterKind filter
    ) : ObservableModel
    {
        private bool _isSelected;

        public string Title { get; } = title;
        public MaterialIcons Icon { get; } = icon;
        public RoomListEntriesDynamicFilterKind Filter { get; } = filter;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
