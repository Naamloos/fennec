using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk
{
    public class ManagedRoom : IAsyncDisposable
    {
        internal ManagedRoom(Room room) 
        {
        
        }

        public ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }
    }
}
