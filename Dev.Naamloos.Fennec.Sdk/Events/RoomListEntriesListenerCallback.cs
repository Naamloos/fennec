using System;
using System.Collections.Generic;
using System.Text;
using Dev.Naamloos.Fennec.Sdk.Generation;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events
{
    [GenerateMatrixListener<RoomListEntriesListener>]
    public partial class RoomListEntriesListenerCallback
    {
    }
}
