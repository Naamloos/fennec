using Dev.Naamloos.Fennec.Sdk;
using System.Diagnostics;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Components;

public sealed class ChatTypingController : IDisposable
{
    private readonly IDispatcher _dispatcher;
    private readonly Func<Room?> _getCurrentRoom;
    private readonly Action<string> _setText;
    private TypingListener? _listener;
    private TaskHandle? _handle;
    private int _updateVersion;

    public ChatTypingController(
        IDispatcher dispatcher,
        Func<Room?> getCurrentRoom,
        Action<string> setText)
    {
        _dispatcher = dispatcher;
        _getCurrentRoom = getCurrentRoom;
        _setText = setText;
    }

    public void Start(Room room, bool isTyping)
    {
        Stop();

        var roomId = room.Id();
        var ownUserId = room.OwnUserId();
        _listener = new TypingListener(
            userIds => _ = UpdateAsync(
                room,
                roomId,
                ownUserId,
                userIds));
        _handle = room.SubscribeToTypingNotifications(_listener);

        if (isTyping)
        {
            _ = SendNoticeAsync(room, true);
        }
    }

    public void SetTyping(bool isTyping)
    {
        if (_getCurrentRoom() is { } room)
        {
            _ = SendNoticeAsync(room, isTyping);
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _updateVersion);
        if (_getCurrentRoom() is { } room)
        {
            _ = SendNoticeAsync(room, false);
        }

        try
        {
            _handle?.Cancel();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not stop typing updates: {exception}");
        }

        _handle?.Dispose();
        _handle = null;
        _listener = null;
        _setText(string.Empty);
    }

    public void Dispose() => Stop();

    public static string Format(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => $"{names[0]} is typing…",
        2 => $"{names[0]} and {names[1]} are typing…",
        _ => $"{names[0]}, {names[1]}, and {names.Count - 2} " +
            $"{(names.Count == 3 ? "other" : "others")} are typing…",
    };

    private async Task UpdateAsync(Room room, string roomId, string ownUserId, string[] userIds)
    {
        var version = Interlocked.Increment(ref _updateVersion);
        var names = new List<string>();
        foreach (var userId in userIds.Where(userId => userId != ownUserId).Distinct())
        {
            try { names.Add(await room.MemberDisplayName(userId) ?? userId); }
            catch { names.Add(userId); }
        }

        var text = Format(names);
        _dispatcher.Dispatch(() =>
        {
            if (version == _updateVersion && _getCurrentRoom()?.Id() == roomId) _setText(text);
        });
    }

    private static async Task SendNoticeAsync(Room room, bool isTyping)
    {
        try { await room.TypingNotice(isTyping); }
        catch (Exception exception) { Debug.WriteLine($"Could not update typing state: {exception}"); }
    }

    private sealed class TypingListener(Action<string[]> callback) : TypingNotificationsListener
    {
        public void Call(string[] typingUserIds) => callback(typingUserIds);
    }
}
