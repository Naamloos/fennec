using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev.Naamloos.Fennec.App.Models;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.ViewModels;

public sealed partial class AppShellViewModel : ObservableObject, IDisposable
{
    private readonly MatrixService _matrixService;
    private readonly ChatViewModel _chat;
    private readonly AppNavigationService _navigation;
    private readonly CancellationTokenSource _refreshCancellation = new();
    private string? _roomSignature;
    private bool _started;

    public AppShellViewModel(
        MatrixService matrixService,
        ChatViewModel chat,
        AppNavigationService navigation)
    {
        _matrixService = matrixService;
        _chat = chat;
        _navigation = navigation;
        _chat.OverviewRoomSelected += OnOverviewRoomSelected;
        UserId = matrixService.Client?.UserId() ?? "Signed in";

#if DEBUG
        var group = new RoomGroup("test", "Test", null, true);
        group.AddRoom(new("!test", "Test"));
        group.Toggle();
        System.Diagnostics.Debug.Assert(
            group.Count == 0 && group.AllRooms.Count == 1 && group.ChevronRotation == -90);
#endif
    }

    public ObservableCollection<RoomGroup> Groups { get; } = [];
    public string UserId { get; }
    public event EventHandler? RoomSelected;
    public event EventHandler? SettingsRequested;

    [ObservableProperty]
    public partial RoomListItem? SelectedRoom { get; set; }

    partial void OnSelectedRoomChanged(RoomListItem? value)
    {
        if (value is null) return;
        _selectedSpaceKey = null;
        RoomSelected?.Invoke(this, EventArgs.Empty);
        _ = _chat.OpenRoomAsync(value);
    }

    private string? _selectedSpaceKey;

    private void OpenGroup(RoomGroup group)
    {
        _selectedSpaceKey = group.Key;
        SelectedRoom = null;
        RoomSelected?.Invoke(this, EventArgs.Empty);
        _chat.OpenSpace(group);
    }

    private void OnOverviewRoomSelected(object? sender, RoomListItem room)
    {
        if (Groups.FirstOrDefault(group => group.AllRooms.Contains(room)) is { IsExpanded: false } group)
            group.Toggle();
        SelectedRoom = room;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = RefreshRoomsAsync(_refreshCancellation.Token);
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _refreshCancellation.Cancel();
        _chat.OverviewRoomSelected -= OnOverviewRoomSelected;
        _chat.CloseRoom();
        await _matrixService.LogoutAsync();
        _navigation.ShowLogin();
    }

    private async Task RefreshRoomsAsync(CancellationToken cancellationToken)
    {
        // ponytail: polling keeps this PoC small; use SDK space listeners if account size makes it measurable.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                try
                {
                    var groups = await BuildRoomGroupsAsync();
                    var signature = string.Join('|', groups.SelectMany(group =>
                        new[] { group.Key, group.Name, group.AvatarUrl ?? "", group.Description ?? "" }
                            .Concat(group.AllRooms.Select(room => $"{room.RoomId}:{room.Name}:{room.AvatarUrl}:{room.IsJoined}"))));
                    if (signature != _roomSignature)
                    {
                        _roomSignature = signature;
                        await MainThread.InvokeOnMainThreadAsync(() => UpdateGroups(groups));
                    }
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    // Sync may briefly make room state unavailable; the next tick retries.
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<List<RoomGroup>> BuildRoomGroupsAsync()
    {
        if (_matrixService.Client is not { } client) return [];

        using var spaceService = await client.SpaceService();
        var dms = new RoomGroup("dms", "DMs", null, true);
        var ungrouped = new RoomGroup("rooms", "Rooms", null, true);
        var spaces = new Dictionary<string, RoomGroup>();

        foreach (var space in await spaceService.TopLevelJoinedSpaces())
        {
            spaces[space.RoomId] = new RoomGroup(
                space.RoomId,
                space.DisplayName,
                space.AvatarUrl,
                false,
                isSpace: true,
                open: OpenGroup)
            {
                Description = space.Topic
            };
        }

        foreach (var room in client.Rooms())
        {
            try
            {
                if (room.IsSpace()) continue;

                var item = new RoomListItem(room.Id(), room.DisplayName() ?? room.Id(), room.AvatarUrl());
                if (await room.IsDirect())
                {
                    dms.AddRoom(item);
                    continue;
                }

                var parents = await spaceService.JoinedParentsOfChild(room.Id());
                if (parents.Length == 0)
                {
                    ungrouped.AddRoom(item);
                    continue;
                }

                foreach (var parent in parents)
                {
                    if (!spaces.TryGetValue(parent.RoomId, out var group))
                    {
                        group = new RoomGroup(
                            parent.RoomId,
                            parent.DisplayName,
                            parent.AvatarUrl,
                            false,
                            isSpace: true,
                            open: OpenGroup);
                        spaces[parent.RoomId] = group;
                    }
                    group.AddRoom(item);
                }
            }
            finally
            {
                room.Dispose();
            }
        }

        foreach (var group in spaces.Values)
        {
            using var roomList = await spaceService.SpaceRoomList(group.Key);
            await roomList.Paginate();
            foreach (var child in await roomList.Rooms())
            {
                if (child.RoomType is RoomType.Space || group.AllRooms.Any(room => room.RoomId == child.RoomId))
                    continue;
                var joined = child.State == Membership.Joined;
                group.AddRoom(new RoomListItem(
                    child.RoomId,
                    child.DisplayName,
                    child.AvatarUrl,
                    child.Topic,
                    joined,
                    child.NumJoinedMembers), includeInSidebar: joined);
            }
        }

        var result = new List<RoomGroup>();
        if (dms.AllRooms.Count > 0) result.Add(dms);
        result.AddRange(spaces.Values.OrderBy(group => group.Name));
        if (ungrouped.AllRooms.Count > 0) result.Add(ungrouped);
        return result;
    }

    private void UpdateGroups(List<RoomGroup> groups)
    {
        var selectedId = SelectedRoom?.RoomId;
        var expansion = Groups.ToDictionary(group => group.Key, group => group.IsExpanded);
        Groups.Clear();

        foreach (var group in groups)
        {
            if (expansion.TryGetValue(group.Key, out var wasExpanded) && group.IsExpanded != wasExpanded)
                group.Toggle();
            Groups.Add(group);
            if (group.AvatarUrl is not null) _ = LoadIconAsync(group);
            foreach (var room in group.AllRooms)
                if (room.AvatarUrl is not null) _ = LoadIconAsync(room);
        }

        if (_selectedSpaceKey is not null && Groups.FirstOrDefault(group => group.Key == _selectedSpaceKey) is { } space)
        {
            SelectedRoom = null;
            _chat.OpenSpace(space);
        }
        else
        {
            SelectedRoom = Groups.SelectMany(group => group.AllRooms)
                .FirstOrDefault(room => room.RoomId == selectedId)
                ?? Groups.SelectMany(group => group).FirstOrDefault();
        }
    }

    private async Task LoadIconAsync(RoomListItem room)
    {
        try
        {
            var bytes = await _matrixService.GetThumbnailAsync(room.AvatarUrl!, 64, 64);
            room.Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
        }
    }

    private async Task LoadIconAsync(RoomGroup group)
    {
        try
        {
            var bytes = await _matrixService.GetThumbnailAsync(group.AvatarUrl!, 64, 64);
            group.Avatar = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _chat.OverviewRoomSelected -= OnOverviewRoomSelected;
        _refreshCancellation.Cancel();
        _refreshCancellation.Dispose();
    }
}
