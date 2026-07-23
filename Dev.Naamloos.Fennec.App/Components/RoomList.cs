using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
using CommunityToolkit.Maui.Markup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed partial class RoomList : ContentView, IDisposable
{
    private readonly HashSet<RoomEntry> _trackedEntries = [];
    private ObservableRoomList? _observableRoomList;
    private bool _isLoading;
    private bool _isRebuildingGroups;
    private bool _disposed;

    [BindableProperty(PropertyChangedMethodName = nameof(OnSelectedRoomChanged))]
    public partial Room? SelectedRoom { get; set; }

    [BindableProperty(PropertyChangedMethodName = nameof(OnSelectedEntryChanged))]
    public partial RoomEntry? SelectedEntry { get; set; }

    [BindableProperty]
    public partial ManagedMatrixClient? MatrixClient { get; set; }

    [BindableProperty]
    public partial IReadOnlyList<RoomListGroup>? Groups { get; set; }

    public RoomList()
    {
        BindingContext = this;
        build();
    }

    private void build()
    {
        Content = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            IsGrouped = true,
            EmptyView = new VerticalStackLayout
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
                        Opacity = .7,
                    },
                },
            },
            ItemTemplate = new DataTemplate(() =>
                new ContentView
                {
                    Content = new RoomListItem()
                        .Bind(
                            RoomListItem.MatrixClientProperty,
                            nameof(MatrixClient),
                            source: BindingContext)
                        .Bind(
                            RoomListItem.EntryProperty,
                            nameof(BindingContext),
                            source: new RelativeBindingSource(
                                RelativeBindingSourceMode.FindAncestor,
                                typeof(ContentView))),
                }),
            GroupHeaderTemplate = new DataTemplate(() =>
                new ContentView
                {
                    Content = new RoomListGroupHeader()
                        .Bind(
                            RoomListGroupHeader.MatrixClientProperty,
                            nameof(MatrixClient),
                            source: BindingContext)
                        .Bind(
                            RoomListGroupHeader.GroupProperty,
                            nameof(BindingContext),
                            source: new RelativeBindingSource(
                                RelativeBindingSourceMode.FindAncestor,
                                typeof(ContentView))),
                }),
            Behaviors =
            {
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Loaded),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(LoadCommand)),
                new EventToCommandBehavior
                {
                    BindingContext = this,
                    EventName = nameof(Unloaded),
                }.Bind(
                    EventToCommandBehavior.CommandProperty,
                    nameof(UnloadCommand)),
            },
        }
        .Bind(ItemsView.ItemsSourceProperty, nameof(Groups))
        .Bind(
            SelectableItemsView.SelectedItemProperty,
            nameof(SelectedEntry),
            BindingMode.TwoWay);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_disposed ||
            _isLoading ||
            _observableRoomList is not null ||
            MatrixClient is null)
        {
            return;
        }

        _isLoading = true;

        try
        {
            _observableRoomList =
                await MatrixClient.GetObservableRoomListAsync();

            _observableRoomList.CollectionChanged += OnRoomsChanged;
            TrackEntries();
            RebuildGroups();
            SynchronizeSelectedEntry();
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

    [RelayCommand]
    private void Unload() => DisposeObservableRoomList();

    private static void OnSelectedRoomChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        ((RoomList)bindable).SynchronizeSelectedEntry();

    private static void OnSelectedEntryChanged(
        BindableObject bindable,
        object oldValue,
        object newValue) =>
        _ = ((RoomList)bindable).SelectEntryAsync(
            newValue as RoomEntry);

    private async Task SelectEntryAsync(RoomEntry? entry)
    {
        if (_isRebuildingGroups)
        {
            return;
        }

        SelectedRoom = entry?.Room;

        if (entry is null)
        {
            return;
        }

        try
        {
            await entry.MarkAsReadAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Could not mark room as read: {exception}");
        }
    }

    private void SynchronizeSelectedEntry()
    {
        if (SelectedRoom is null ||
            _observableRoomList is null)
        {
            SelectedEntry = null;
            return;
        }

        if (SelectedEntry?.Room.Id() == SelectedRoom.Id())
        {
            return;
        }

        SelectedEntry = _observableRoomList.FirstOrDefault(
            entry => entry.Room.Id() == SelectedRoom.Id());
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
            Groups = null;
            return;
        }

        var groups = new Dictionary<string, RoomListGroup>();

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
            .ThenBy(
                group => group.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _isRebuildingGroups = true;

        try
        {
            Groups = orderedGroups;
            SynchronizeSelectedEntry();
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
                        room.Spaces
                            .DistinctBy(space => space.Id)
                            .Count())));

        void AddToGroup(
            string id,
            string name,
            string? avatarUrl,
            RoomEntry room)
        {
            if (!groups.TryGetValue(id, out var group))
            {
                group = new RoomListGroup(
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
        Groups = null;

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
        DisposeObservableRoomList();
        GC.SuppressFinalize(this);
    }
}
