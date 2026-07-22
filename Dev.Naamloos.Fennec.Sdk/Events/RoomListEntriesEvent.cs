using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.NativeEventHandler
{
    public class RoomListEntriesEvent : RoomListEntriesListener
    {
        private readonly Action<RoomListEntriesUpdate[]> _callback;

        public RoomListEntriesEvent(Action<RoomListEntriesUpdate[]> callback) 
        { 
            _callback = callback;
        }

        public void OnUpdate(RoomListEntriesUpdate[] roomEntriesUpdate)
        {
            _callback?.Invoke(roomEntriesUpdate);
        }
    }
}
