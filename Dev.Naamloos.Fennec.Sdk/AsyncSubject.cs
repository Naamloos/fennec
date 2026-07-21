using Dev.Naamloos.Fennec.Sdk.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.Sdk
{
    /// <summary>
    /// Represents an asynchronous subject that allows multiple observers to subscribe and receive notifications of values, errors, or completion events.
    /// </summary>
    /// <typeparam name="T">The type of the values being observed.</typeparam>
    public sealed class AsyncSubject<T> :
        IAsyncObservable<T>,
        IAsyncDisposable
    {
        private readonly ConcurrentDictionary<long, Subscription> _subscriptions = [];

        private long _nextSubscriptionId;
        private bool _isCompleted;
        private Exception? _terminalError;

        public ValueTask<IAsyncDisposable> SubscribeAsync(
            IAsyncObserver<T> observer,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(observer);
            cancellationToken.ThrowIfCancellationRequested();

            if (_isCompleted)
            {
                return subscribeToCompletedSubjectAsync(
                    observer,
                    cancellationToken);
            }

            var id = Interlocked.Increment(ref _nextSubscriptionId);

            var subscription = new Subscription(
                id,
                this,
                observer,
                cancellationToken);

            if (!_subscriptions.TryAdd(id, subscription))
            {
                throw new InvalidOperationException(
                    "Could not register the observable subscription.");
            }

            if (_isCompleted && _subscriptions.TryRemove(id, out var removed))
            {
                return subscribeAfterConcurrentCompletionAsync(
                    removed,
                    cancellationToken);
            }

            return ValueTask.FromResult<IAsyncDisposable>(subscription);
        }

        public async ValueTask PublishAsync(
            T value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_isCompleted)
            {
                return;
            }

            var subscriptions = _subscriptions.Values.ToArray();

            foreach (var subscription in subscriptions)
            {
                await subscription.PublishAsync(
                    value,
                    cancellationToken);
            }
        }

        public async ValueTask FailAsync(
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(exception);

            if (_isCompleted)
            {
                return;
            }

            _terminalError = exception;
            _isCompleted = true;

            var subscriptions = removeAllSubscriptions();

            foreach (var subscription in subscriptions)
            {
                await subscription.FailAsync(
                    exception,
                    cancellationToken);
            }
        }

        public async ValueTask CompleteAsync(
            CancellationToken cancellationToken = default)
        {
            if (_isCompleted)
            {
                return;
            }

            _isCompleted = true;

            var subscriptions = removeAllSubscriptions();

            foreach (var subscription in subscriptions)
            {
                await subscription.CompleteAsync(cancellationToken);
            }
        }

        private Subscription[] removeAllSubscriptions()
        {
            var subscriptions = _subscriptions.Values.ToArray();

            foreach (var subscription in subscriptions)
            {
                _subscriptions.TryRemove(subscription.Id, out _);
            }

            return subscriptions;
        }

        private void removeSubscription(long id)
        {
            _subscriptions.TryRemove(id, out _);
        }

        private async ValueTask<IAsyncDisposable> subscribeToCompletedSubjectAsync(
            IAsyncObserver<T> observer,
            CancellationToken cancellationToken)
        {
            if (_terminalError is not null)
            {
                await observer.OnErrorAsync(
                    _terminalError,
                    cancellationToken);
            }
            else
            {
                await observer.OnCompletedAsync(cancellationToken);
            }

            return EmptyAsyncDisposable.Instance;
        }

        private async ValueTask<IAsyncDisposable>
            subscribeAfterConcurrentCompletionAsync(
                Subscription subscription,
                CancellationToken cancellationToken)
        {
            await subscription.DisposeAsync();

            return await subscribeToCompletedSubjectAsync(
                subscription.Observer,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await CompleteAsync();
        }

        private sealed class Subscription : IAsyncDisposable
        {
            private readonly AsyncSubject<T> _owner;
            private readonly CancellationTokenRegistration _registration;
            private readonly SemaphoreSlim _callbackLock = new(1, 1);

            private int _disposed;

            public long Id { get; }

            public IAsyncObserver<T> Observer { get; }

            public Subscription(
                long id,
                AsyncSubject<T> owner,
                IAsyncObserver<T> observer,
                CancellationToken cancellationToken)
            {
                Id = id;
                _owner = owner;
                Observer = observer;

                _registration = cancellationToken.Register(
                    static state =>
                    {
                        var subscription = (Subscription)state!;
                        _ = subscription.DisposeAsync();
                    },
                    this);
            }

            public async ValueTask PublishAsync(
                T value,
                CancellationToken cancellationToken)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                await _callbackLock.WaitAsync(cancellationToken);

                try
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        await Observer.OnNextAsync(
                            value,
                            cancellationToken);
                    }
                }
                finally
                {
                    _callbackLock.Release();
                }
            }

            public async ValueTask FailAsync(
                Exception exception,
                CancellationToken cancellationToken)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                await _callbackLock.WaitAsync(cancellationToken);

                try
                {
                    await Observer.OnErrorAsync(
                        exception,
                        cancellationToken);
                }
                finally
                {
                    _callbackLock.Release();
                    cleanup();
                }
            }

            public async ValueTask CompleteAsync(
                CancellationToken cancellationToken)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                await _callbackLock.WaitAsync(cancellationToken);

                try
                {
                    await Observer.OnCompletedAsync(cancellationToken);
                }
                finally
                {
                    _callbackLock.Release();
                    cleanup();
                }
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    cleanup();
                }

                return ValueTask.CompletedTask;
            }

            private void cleanup()
            {
                _registration.Dispose();
                _owner.removeSubscription(Id);
                _callbackLock.Dispose();
            }
        }

        private sealed class EmptyAsyncDisposable : IAsyncDisposable
        {
            public static EmptyAsyncDisposable Instance { get; } = new();

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
