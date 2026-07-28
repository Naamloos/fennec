using uniffi.matrix_sdk_ffi;
using uniffi.matrix_sdk;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class PaginationStatusListenerCallback : PaginationStatusListener
{
    private readonly Action<PaginationStatus> _callback;

    private PaginationStatusListenerCallback(Action<PaginationStatus> callback) =>
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));

    public static PaginationStatusListenerCallback Create(Action<PaginationStatus> callback) =>
        new(callback);

    public void OnUpdate(PaginationStatus status) => _callback(status);
}
