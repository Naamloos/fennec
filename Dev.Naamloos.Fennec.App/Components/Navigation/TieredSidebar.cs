using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Entities;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using MauiIcons.Core;
using MauiIcons.Material;
using System.Diagnostics;
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
        new("Favorites", MaterialIcons.Star, new RoomListEntriesDynamicFilterKind.All(
        [
            new RoomListEntriesDynamicFilterKind.Favourite(),
            new RoomListEntriesDynamicFilterKind.NonSpace(),
        ])),
        new("DMs", MaterialIcons.Person, new RoomListEntriesDynamicFilterKind.All(
        [
            new RoomListEntriesDynamicFilterKind.NonSpace(),
            new RoomListEntriesDynamicFilterKind.Category(
                RoomListFilterCategory.People),
        ])),
        new("Rooms", MaterialIcons.Tag, new RoomListEntriesDynamicFilterKind.All(
        [
            new RoomListEntriesDynamicFilterKind.NonSpace(),
            new RoomListEntriesDynamicFilterKind.Category(
                RoomListFilterCategory.Group),
        ])),
    ];

    private ObservableRoomList? _spaces;
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
            Filter = _sections[2].Filter,
            ExcludeSpaceRooms = true,
            SectionTitle = _sections[2].Title,
        }
        .Bind(
            RoomListView.SelectedRoomProperty,
            nameof(SelectedRoom),
            BindingMode.TwoWay,
            source: this);
        _spaceRoomListView = new SpaceRoomListView { IsVisible = false }
            .Bind(
                SpaceRoomListView.SelectedRoomProperty,
                nameof(SelectedRoom),
                BindingMode.TwoWay,
                source: this);

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
                new RoomListEntriesDynamicFilterKind.All(
                [
                    new RoomListEntriesDynamicFilterKind.Space(),
                    new RoomListEntriesDynamicFilterKind.Joined(),
                ]));
            _spaces.CaptureCurrentContext();
            _spacesView.ItemsSource = _spaces;
            ShowSection(_sections[2]);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not load spaces: {exception}");
        }
    }

    private CollectionView CreateSectionsView() => new()
    {
        ItemsSource = _sections,
        SelectionMode = SelectionMode.None,
        ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
        ItemTemplate = new DataTemplate(() =>
        {
            var icon = new MauiIcon
            {
                IconSize = 20,
                HorizontalOptions = LayoutOptions.Center,
            };
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
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
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

    private CollectionView CreateSpacesView() => new()
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
                },
                Children =
                {
                    new Border
                    {
                        WidthRequest = 3,
                        HeightRequest = 26,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
                        VerticalOptions = LayoutOptions.Center,
                    }
                    .Bind<Border, string?, string?, bool>(
                        IsVisibleProperty,
                        new Binding(nameof(SelectedSpaceId), source: this),
                        new Binding(nameof(ManagedRoom.Id)),
                        convert: static values => values.Item1 == values.Item2)
                    .DynamicResource(BackgroundColorProperty, "Primary")
                    .Column(0),
                    new RoomAvatar { Size = 32 }
                        .Bind(RoomAvatar.AvatarUrlProperty, nameof(ManagedRoom.AvatarUrl))
                        .Bind(RoomAvatar.DisplayNameProperty, nameof(ManagedRoom.DisplayName))
                        .Column(1),
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
            ColumnDefinitions =
            {
                new ColumnDefinition(68),
                new ColumnDefinition(GridLength.Star),
            },
            Children =
            {
                new Grid
                {
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(GridLength.Star),
                    },
                    Children =
                    {
                        _sectionsView.Row(0),
                        _spacesView.Row(1),
                    },
                }.Column(0),
                new Grid
                {
                    Children =
                    {
                        _roomListView,
                        _spaceRoomListView,
                    },
                }.Column(1),
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spaces?.Dispose();
        _spaces = null;
        _roomListView.Dispose();
        _spaceRoomListView.Dispose();
    }

    private sealed class SidebarSection(
        string title,
        MaterialIcons icon,
        RoomListEntriesDynamicFilterKind filter)
        : ObservableModel
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
