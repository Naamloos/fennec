using System.Threading;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class SyncServiceStateObserverCallback
    : SyncServiceStateObserver
{
    private readonly Action<SyncServiceState> _callback;
    private readonly SynchronizationContext? _context;

    private SyncServiceStateObserverCallback(
        Action<SyncServiceState> callback)
    {
        _callback = callback
            ?? throw new ArgumentNullException(nameof(callback));

        _context = SynchronizationContext.Current;
    }

    public static SyncServiceStateObserverCallback Create(
        Action<SyncServiceState> callback)
    {
        return new SyncServiceStateObserverCallback(callback);
    }

    public void OnUpdate(SyncServiceState updates)
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
        Action<SyncServiceState> Callback,
        SyncServiceState Updates);
}
