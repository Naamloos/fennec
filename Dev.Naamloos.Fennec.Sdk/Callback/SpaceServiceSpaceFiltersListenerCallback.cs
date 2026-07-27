using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class SpaceServiceSpaceFiltersListenerCallback(
    Action<SpaceFilterUpdate[]> callback)
    : SpaceServiceSpaceFiltersListener
{
    public void OnUpdate(SpaceFilterUpdate[] updates) => callback(updates);
}
