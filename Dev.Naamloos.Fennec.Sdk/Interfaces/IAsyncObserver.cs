using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.Sdk.Interfaces
{
    public interface IAsyncObserver<in T>
    {
        ValueTask OnNextAsync(
            T value,
            CancellationToken cancellationToken = default);

        ValueTask OnErrorAsync(
            Exception exception,
            CancellationToken cancellationToken = default);

        ValueTask OnCompletedAsync(
            CancellationToken cancellationToken = default);
    }
}
