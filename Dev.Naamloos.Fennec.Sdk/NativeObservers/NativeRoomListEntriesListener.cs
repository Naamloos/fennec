using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.NativeObservers
{
    internal sealed class NativeRoomListEntriesListener
        : RoomListEntriesListener
    {
        private readonly Action<IReadOnlyList<RoomListEntriesUpdate>>
            _onUpdate;

        public NativeRoomListEntriesListener(
            Action<IReadOnlyList<RoomListEntriesUpdate>> onUpdate)
        {
            _onUpdate = onUpdate
                ?? throw new ArgumentNullException(nameof(onUpdate));
        }

        public void OnUpdate(
            RoomListEntriesUpdate[] updates)
        {
            try
            {
                _onUpdate(updates);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Native room-list callback failed: {exception}");
            }
        }
    }
}
