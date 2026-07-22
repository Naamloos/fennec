using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events
{
    public class SyncServiceStateEvent : SyncServiceStateObserver
    {
        private readonly Action<SyncServiceState> _callback;

        public SyncServiceStateEvent(Action<SyncServiceState> callback)
        {
            _callback = callback;
        }

        public void OnUpdate(SyncServiceState state)
        {
            _callback?.Invoke(state);
        }
    }
}
