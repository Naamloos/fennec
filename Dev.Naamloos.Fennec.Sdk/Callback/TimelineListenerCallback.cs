using System.Threading;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class TimelineListenerCallback : TimelineListener
{
    private readonly Action<TimelineDiff[]> _callback;
    private readonly SynchronizationContext? _context;

    private TimelineListenerCallback(Action<TimelineDiff[]> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));

        _context = SynchronizationContext.Current;
    }

    public static TimelineListenerCallback Create(Action<TimelineDiff[]> callback)
    {
        return new TimelineListenerCallback(callback);
    }

    public void OnUpdate(TimelineDiff[] updates)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
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
            new CallbackState(_callback, updates)
        );
    }

    private sealed record CallbackState(Action<TimelineDiff[]> Callback, TimelineDiff[] Updates);
}
