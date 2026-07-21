using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.Sdk.Interfaces
{
    public interface IAsyncObservable<out T>
    {
        ValueTask<IAsyncDisposable> SubscribeAsync(
            IAsyncObserver<T> observer,
            CancellationToken cancellationToken = default);
    }
}
