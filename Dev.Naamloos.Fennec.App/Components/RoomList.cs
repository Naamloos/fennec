using CommunityToolkit.Maui.Markup;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class RoomList : ContentView, IDisposable
{
    private readonly ManagedMatrixClient _matrixClient;
    private readonly CollectionView _collectionView;
    private readonly HashSet<RoomEntry> _trackedEntries = [];

    private ObservableRoomList? _observableRoomList;
    private bool _isLoading;
    private bool _isRebuildingGroups;
    private bool _disposed;

    public static readonly BindableProperty SelectedRoomProperty =
        BindableProperty.Create(
            nameof(SelectedRoom),
            typeof(Room),
            typeof(RoomList),
            default(Room),
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnSelectedRoomChanged);

    public Room? SelectedRoom
    {
        get => (Room?)GetValue(SelectedRoomProperty);
        set => SetValue(SelectedRoomProperty, value);
    }

    public RoomList(ManagedMatrixClient matrixClient)
    {
        _matrixClient = matrixClient;

        _collectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            IsGrouped = true,
            EmptyView = CreateEmptyView(),
            ItemTemplate = new DataTemplate(CreateRoomListItem),
            GroupHeaderTemplate = new DataTemplate(CreateGroupHeader),
        };

        _collectionView.SelectionChanged += OnSelectionChanged;

        Content = _collectionView;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(
        object? sender,
        EventArgs e)
    {
        await InitializeAsync();
    }

    private void OnUnloaded(
        object? sender,
        EventArgs e)
    {
        DisposeObservableRoomList();
    }

    private async Task InitializeAsync()
    {
        if (_disposed ||
            _isLoading ||
            _observableRoomList is not null)
        {
            return;
        }

        _isLoading = true;

        try
        {
            _observableRoomList =
                await _matrixClient.GetObservableRoomListAsync();

            _observableRoomList.CollectionChanged += OnRoomsChanged;
            TrackEntries();
            RebuildGroups();

            SynchronizeSelectedItem();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Failed to initialize room list: {exception}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_isRebuildingGroups)
        {
            return;
        }

        if (e.CurrentSelection.FirstOrDefault() is RoomEntry roomEntry)
        {
            SelectedRoom = roomEntry.Room;

            try
            {
                await roomEntry.MarkAsReadAsync();
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not mark room as read: {exception}");
            }
        }
        else
        {
            SelectedRoom = null;
        }
    }

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object? oldValue,
        object? newValue)
    {
        var roomList = (RoomList)bindable;
        roomList.SynchronizeSelectedItem();
    }

    private void SynchronizeSelectedItem()
    {
        if (SelectedRoom is null ||
            _observableRoomList is null)
        {
            _collectionView.SelectedItem = null;
            return;
        }

        if (_collectionView.SelectedItem is RoomEntry currentEntry &&
            currentEntry.Room.Id() == SelectedRoom.Id())
        {
            return;
        }

        _collectionView.SelectedItem = _observableRoomList
            .FirstOrDefault(
                entry => entry.Room.Id() == SelectedRoom.Id());
    }

    private static View CreateEmptyView()
    {
        return new VerticalStackLayout
        {
            Padding = new Thickness(0, 24),
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
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
                    HorizontalTextAlignment = TextAlignment.Center,
                    Opacity = 0.7,
                },
            },
        };
    }

    private View CreateRoomListItem()
    {
        var nameLabel = new Label
        {
            FontSize = 15,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomEntry.Name),
            mode: BindingMode.OneWay);

        var fallbackLabel = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomEntry.Name),
            converter: InitialConverter.Instance,
            mode: BindingMode.OneWay);

        var avatar = new Image
        {
            Aspect = Aspect.AspectFill,
            IsVisible = false,
        };

        var roomIcon = new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 18,
            },
            Content = new Grid
            {
                Children =
                {
                    fallbackLabel,
                    avatar,
                },
            },
        };
        roomIcon.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "SurfaceVariant");

        var unreadMarker = new Border
        {
            WidthRequest = 8,
            HeightRequest = 8,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 4,
            },
            VerticalOptions = LayoutOptions.Center,
        }.Bind(
            VisualElement.IsVisibleProperty,
            nameof(RoomEntry.HasUnread),
            mode: BindingMode.OneWay);
        unreadMarker.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "Primary");

        var content = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        content.Add(roomIcon, column: 0);
        content.Add(nameLabel, column: 1);
        content.Add(unreadMarker, column: 2);

        var root = new Border
        {
            Margin = new Thickness(0, 2),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 8,
            },
            Content = content,
        };

        RoomEntry? boundEntry = null;

        root.BindingContextChanged += (_, _) =>
        {
            if (boundEntry is not null)
            {
                boundEntry.PropertyChanged -= OnEntryPropertyChanged;
            }

            boundEntry = root.BindingContext as RoomEntry;

            if (boundEntry is not null)
            {
                boundEntry.PropertyChanged += OnEntryPropertyChanged;
            }

            _ = LoadAvatarAsync(boundEntry);
        };

        return root;

        void OnEntryPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RoomEntry.AvatarUrl))
            {
                _ = LoadAvatarAsync(sender as RoomEntry);
            }
        }

        async Task LoadAvatarAsync(RoomEntry? entry)
        {
            avatar.Source = null;
            avatar.IsVisible = false;

            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.AvatarUrl))
            {
                return;
            }

            var avatarUrl = entry.AvatarUrl;

            try
            {
                var bytes = await _matrixClient.GetThumbnailAsync(
                    avatarUrl,
                    72,
                    72,
                    isJson: false);

                if (root.BindingContext is RoomEntry current &&
                    current.Id == entry.Id &&
                    current.AvatarUrl == avatarUrl)
                {
                    avatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                    avatar.IsVisible = true;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load room avatar: {exception}");
            }
        }
    }

    private View CreateGroupHeader()
    {
        var fallbackLabel = new Label
        {
            FontSize = 9,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomGroup.Name),
            converter: InitialConverter.Instance,
            mode: BindingMode.OneWay);

        var avatar = new Image
        {
            Aspect = Aspect.AspectFill,
            IsVisible = false,
        };

        var icon = new Border
        {
            WidthRequest = 18,
            HeightRequest = 18,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = 9,
            },
            Content = new Grid
            {
                Children =
                {
                    fallbackLabel,
                    avatar,
                },
            },
        }.Bind(
            VisualElement.IsVisibleProperty,
            nameof(RoomGroup.IsSpace),
            mode: BindingMode.OneWay);
        icon.SetDynamicResource(
            VisualElement.BackgroundColorProperty,
            "SurfaceVariant");

        var label = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalTextAlignment = TextAlignment.Center,
        }.Bind(
            Label.TextProperty,
            nameof(RoomGroup.Name),
            mode: BindingMode.OneWay);
        label.SetDynamicResource(
            Label.TextColorProperty,
            "OnSurfaceVariant");

        var root = new Grid
        {
            Margin = new Thickness(8, 12, 8, 4),
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        root.Add(icon, column: 0);
        root.Add(label, column: 1);

        root.BindingContextChanged += async (_, _) =>
        {
            avatar.Source = null;
            avatar.IsVisible = false;

            if (root.BindingContext is not RoomGroup group ||
                string.IsNullOrWhiteSpace(group.AvatarUrl))
            {
                return;
            }

            var avatarUrl = group.AvatarUrl;

            try
            {
                var bytes = await _matrixClient.GetThumbnailAsync(
                    avatarUrl,
                    36,
                    36,
                    isJson: false);

                if (root.BindingContext is RoomGroup current &&
                    current.Id == group.Id &&
                    current.AvatarUrl == avatarUrl)
                {
                    avatar.Source = ImageSource.FromStream(
                        () => new MemoryStream(bytes));
                    avatar.IsVisible = true;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Could not load space avatar: {exception}");
            }
        };

        return root;
    }

    private void OnRoomsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        TrackEntries();
        RebuildGroups();
    }

    private void OnRoomPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(RoomEntry.Spaces) or
            nameof(RoomEntry.IsSpace) or
            nameof(RoomEntry.IsDirectMessage))
        {
            RebuildGroups();
        }
    }

    private void TrackEntries()
    {
        foreach (var entry in _trackedEntries)
        {
            entry.PropertyChanged -= OnRoomPropertyChanged;
        }

        _trackedEntries.Clear();

        if (_observableRoomList is null)
        {
            return;
        }

        foreach (var entry in _observableRoomList)
        {
            _trackedEntries.Add(entry);
            entry.PropertyChanged += OnRoomPropertyChanged;
        }
    }

    private void RebuildGroups()
    {
        if (_observableRoomList is null)
        {
            _collectionView.ItemsSource = null;
            return;
        }

        var groups = new Dictionary<string, RoomGroup>();

        foreach (var room in _observableRoomList.Where(room => !room.IsSpace))
        {
            if (room.IsDirectMessage)
            {
                AddToGroup("$dms", "Direct messages", null, room);
                continue;
            }

            var spaces = room.Spaces
                .DistinctBy(space => space.Id)
                .ToArray();

            if (spaces.Length == 0)
            {
                AddToGroup("", "Channels", null, room);
                continue;
            }

            foreach (var space in spaces)
            {
                AddToGroup(
                    space.Id,
                    space.Name,
                    space.AvatarUrl,
                    room);
            }
        }

        var orderedGroups = groups.Values
            .OrderBy(group => group.Id == "$dms"
                ? 0
                : group.Id.Length == 0
                    ? 2
                    : 1)
            .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        // ponytail: rebuild the small sidebar wholesale; use incremental group
        // updates only if profiling shows large accounts stutter.
        _isRebuildingGroups = true;

        try
        {
            _collectionView.ItemsSource = orderedGroups;
            SynchronizeSelectedItem();
        }
        finally
        {
            _isRebuildingGroups = false;
        }

        Debug.Assert(
            orderedGroups.Sum(group => group.Count) ==
            _observableRoomList
                .Where(room => !room.IsSpace)
                .Sum(room => room.IsDirectMessage
                    ? 1
                    : Math.Max(
                        1,
                        room.Spaces.DistinctBy(space => space.Id).Count())));

        void AddToGroup(
            string id,
            string name,
            string? avatarUrl,
            RoomEntry room)
        {
            if (!groups.TryGetValue(id, out var group))
            {
                group = new RoomGroup(
                    id,
                    name,
                    avatarUrl);
                groups.Add(id, group);
            }

            group.Add(room);
        }
    }

    private void DisposeObservableRoomList()
    {
        _collectionView.ItemsSource = null;

        if (_observableRoomList is not null)
        {
            _observableRoomList.CollectionChanged -= OnRoomsChanged;
        }

        foreach (var entry in _trackedEntries)
        {
            entry.PropertyChanged -= OnRoomPropertyChanged;
        }

        _trackedEntries.Clear();
        _observableRoomList?.Dispose();
        _observableRoomList = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _collectionView.SelectionChanged -= OnSelectionChanged;

        DisposeObservableRoomList();

        GC.SuppressFinalize(this);
    }

    private sealed class RoomGroup(
        string id,
        string name,
        string? avatarUrl) : List<RoomEntry>
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string? AvatarUrl { get; } = avatarUrl;

        public bool IsSpace => Id is not "" and not "$dms";
    }

    private sealed class InitialConverter : IValueConverter
    {
        public static InitialConverter Instance { get; } = new();

        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            return value is string name &&
                !string.IsNullOrWhiteSpace(name)
                    ? StringInfo.GetNextTextElement(name).ToUpperInvariant()
                    : "#";
        }

        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
