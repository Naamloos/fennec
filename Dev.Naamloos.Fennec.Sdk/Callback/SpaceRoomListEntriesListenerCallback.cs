using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events;

public sealed class SpaceRoomListEntriesListenerCallback(
    Action<SpaceListUpdate[]> callback)
    : SpaceRoomListEntriesListener
{
    public void OnUpdate(SpaceListUpdate[] rooms) => callback(rooms);
}
