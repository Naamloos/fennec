using System.Threading;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class RoomListEntriesListenerCallback
    : RoomListEntriesListener
{
    private readonly Action<RoomListEntriesUpdate[]> _callback;
    private readonly SynchronizationContext? _context;

    private RoomListEntriesListenerCallback(
        Action<RoomListEntriesUpdate[]> callback)
    {
        _callback = callback
            ?? throw new ArgumentNullException(nameof(callback));

        _context = SynchronizationContext.Current;
    }

    public static RoomListEntriesListenerCallback Create(
        Action<RoomListEntriesUpdate[]> callback)
    {
        return new RoomListEntriesListenerCallback(callback);
    }

    public void OnUpdate(RoomListEntriesUpdate[] updates)
    {
        if (_context is null ||
            ReferenceEquals(SynchronizationContext.Current, _context))
        {
            _callback(updates);
            return;
        }

        _context.Post(
            static state =>
            {
                var callbackState = (CallbackState)state!;
                callbackState.Callback(callbackState.Updates);
            },
            new CallbackState(_callback, updates));
    }

    private sealed record CallbackState(
        Action<RoomListEntriesUpdate[]> Callback,
        RoomListEntriesUpdate[] Updates);
}
