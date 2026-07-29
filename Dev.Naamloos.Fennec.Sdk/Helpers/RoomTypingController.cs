using System.Diagnostics;
using Dev.Naamloos.Fennec.Sdk.Entities;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

internal sealed class RoomTypingController : ObservableModel, IDisposable
{
    private readonly Room _room;
    private readonly string _ownUserId;
    private readonly SynchronizationContext? _synchronizationContext;
    private TypingNotificationsListener? _listener;
    private TaskHandle? _handle;
    private bool _isTyping;

    public RoomTypingController(Room room)
    {
        _room = room;
        _ownUserId = room.OwnUserId();
        _synchronizationContext = SynchronizationContext.Current;
    }

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        private set => Set(ref _text, value);
    }

    public void Start()
    {
        _listener = new Listener(userIds => _ = UpdateAsync(userIds));
        _handle = _room.SubscribeToTypingNotifications(_listener);
    }

    public void SetTyping(bool isTyping)
    {
        if (_isTyping == isTyping)
        {
            return;
        }

        _isTyping = isTyping;
        _ = SendAsync(isTyping);
    }

    private async Task UpdateAsync(string[] userIds)
    {
        var names = new List<string>();

        foreach (var userId in userIds.Where(userId => userId != _ownUserId).Distinct())
        {
            try
            {
                names.Add(await _room.MemberDisplayName(userId) ?? userId);
            }
            catch
            {
                names.Add(userId);
            }
        }

        if (_synchronizationContext is null)
        {
            Text = Format(names);
            return;
        }

        _synchronizationContext.Post(
            static state =>
            {
                var (controller, text) = ((RoomTypingController, string))state!;
                controller.Text = text;
            },
            (this, Format(names))
        );
    }

    private static string Format(IEnumerable<string> users)
    {
        var names = users.Distinct().ToArray();

        return names.Length switch
        {
            0 => string.Empty,
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]} and {names[1]} are typing…",
            _ => $"{names[0]}, {names[1]}, and {names.Length - 2} others are typing…",
        };
    }

    private async Task SendAsync(bool isTyping)
    {
        try
        {
            await _room.TypingNotice(isTyping);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Could not update typing state: {exception}");
        }
    }

    public void Dispose()
    {
        SetTyping(false);
        _handle?.Cancel();
        _handle?.Dispose();
        _handle = null;
        _listener = null;
        Text = string.Empty;
    }

    private sealed class Listener(Action<string[]> update) : TypingNotificationsListener
    {
        public void Call(string[] typingUserIds) => update(typingUserIds);
    }
}
