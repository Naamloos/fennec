using Dev.Naamloos.Fennec.Sdk.Generation;
using System;
using System.Collections.Generic;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Events
{
    [GenerateMatrixListener<SyncServiceStateObserver>]
    public partial class SyncServiceStateObserverCallback
    {
    }
}
