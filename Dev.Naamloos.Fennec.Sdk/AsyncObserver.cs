using Dev.Naamloos.Fennec.Sdk.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.Sdk
{
    /// <summary>
    /// An implementation of IAsyncObserver that allows you to provide delegates for the OnNext, OnError, and OnCompleted methods.
    /// </summary>
    /// <typeparam name="T">The type of the values being observed.</typeparam>
    public sealed class AsyncObserver<T> : IAsyncObserver<T>
    {
        private readonly Func<T, CancellationToken, ValueTask> _onNext;
        private readonly Func<Exception, CancellationToken, ValueTask> _onError;
        private readonly Func<CancellationToken, ValueTask> _onCompleted;

        public AsyncObserver(
            Func<T, CancellationToken, ValueTask> onNext,
            Func<Exception, CancellationToken, ValueTask>? onError = null,
            Func<CancellationToken, ValueTask>? onCompleted = null)
        {
            _onNext = onNext;
            _onError = onError ?? ((_, _) => ValueTask.CompletedTask);
            _onCompleted = onCompleted ?? (_ => ValueTask.CompletedTask);
        }

        public ValueTask OnNextAsync(
            T value,
            CancellationToken cancellationToken = default)
        {
            return _onNext(value, cancellationToken);
        }

        public ValueTask OnErrorAsync(
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            return _onError(exception, cancellationToken);
        }

        public ValueTask OnCompletedAsync(
            CancellationToken cancellationToken = default)
        {
            return _onCompleted(cancellationToken);
        }
    }

    public static class AsyncObservableExtensions
    {
        public static ValueTask<IAsyncDisposable> SubscribeAsync<T>(
            this IAsyncObservable<T> observable,
            Func<T, CancellationToken, ValueTask> onNext,
            Func<Exception, CancellationToken, ValueTask>? onError = null,
            Func<CancellationToken, ValueTask>? onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(observable);
            ArgumentNullException.ThrowIfNull(onNext);

            return observable.SubscribeAsync(
                new AsyncObserver<T>(
                    onNext,
                    onError,
                    onCompleted),
                cancellationToken);
        }
    }
}
