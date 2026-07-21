using Dev.Naamloos.Fennec.Sdk.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.NativeListeners
{
    public class NativeSyncServiceStateObserver : SyncServiceStateObserver
    {
        private readonly AsyncSubject<SyncServiceState> _observable;

        public NativeSyncServiceStateObserver(AsyncSubject<SyncServiceState> observable)
        {
            this._observable = observable;
        }

        public void OnUpdate(SyncServiceState state)
        {
            _ = publishAsync(state);
        }

        private async Task publishAsync(SyncServiceState state)
        {
            try
            {
                await _observable.PublishAsync(state);

            }
            catch (Exception ex)
            {
                // Handle the exception (e.g., log it)
                Console.WriteLine($"Error publishing SyncServiceState: {ex.Message}");
            }
        }
    }
}
