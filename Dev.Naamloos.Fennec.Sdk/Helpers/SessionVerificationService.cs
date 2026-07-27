using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Helpers
{
    public sealed class SessionVerificationService : SessionVerificationControllerDelegate, INotifyPropertyChanged, IAsyncDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ManagedVerificationState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    onPropertyChanged(nameof(State));
                }
            }
        }
        private ManagedVerificationState _state = ManagedVerificationState.Listening;

        public SessionVerificationData? VerificationData
        {
            get => _verificationData;
            private set
            {
                if (_verificationData != value)
                {
                    _verificationData = value;
                    onPropertyChanged(nameof(VerificationData));
                }
            }
        }
        private SessionVerificationData? _verificationData;

        public SessionVerificationRequestDetails? PendingRequest
        {
            get => _pendingRequest;
            private set
            {
                if (_pendingRequest != value)
                {
                    _pendingRequest = value;
                    onPropertyChanged(nameof(PendingRequest));
                }
            }
        }
        private SessionVerificationRequestDetails? _pendingRequest;

        private readonly ManagedMatrixClient _client;
        private readonly SemaphoreSlim _controllerLock = new(1, 1);
        private SessionVerificationController? _controller;
        private SynchronizationContext? _synchronizationContext;

        private bool _initiatedLocally;
        private bool _disposed;

        public SessionVerificationService(
            ManagedMatrixClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.NativeResourcesDisposing += StopAsync;
        }

        public static async Task<SessionVerificationService> CreateAsync(ManagedMatrixClient client)
        {
            var service = new SessionVerificationService(client);
            await service.InitializeAsync();
            return service;
        }

        public Task InitializeAsync()
        {
            throwIfDisposed();
            _synchronizationContext ??= SynchronizationContext.Current;

            return InitializeControllerAsync();
        }

        public async Task RequestVerificationAsync()
        {
            throwIfDisposed();
            Reset();

            _initiatedLocally = true;
            State = ManagedVerificationState.RequestingVerification;

            await GetController().RequestDeviceVerification();

            if(State == ManagedVerificationState.RequestingVerification)
            {
                State = ManagedVerificationState.WaitingForOtherSession;
            }
        }

        public async Task AcceptAsync()
        {
            throwIfDisposed();
            if (State != ManagedVerificationState.AwaitingUserAcceptance)
            {
                return;
            }

            State = ManagedVerificationState.AcceptingRequest;

            await GetController().AcceptVerificationRequest();

            if(State == ManagedVerificationState.AcceptingRequest)
            {
                State = ManagedVerificationState.WaitingForVerificationData;
            }
        }

        public async Task CancelOrRejectAsync()
        {
            throwIfDisposed();
            State = ManagedVerificationState.Cancelling;
            await GetController().CancelVerification();
        }

        public async Task ApproveAsync()
        {
            throwIfDisposed();
            if (State != ManagedVerificationState.Comparing)
            {
                return;
            }
            State = ManagedVerificationState.Approving;
            await GetController().ApproveVerification();
        }

        public async Task DeclineAsync()
        {
            throwIfDisposed();
            if (State != ManagedVerificationState.Comparing)
            {
                return;
            }
            State = ManagedVerificationState.Declining;
            await GetController().DeclineVerification();
        }

        // --- Delegate Implementation ---
        public void DidAcceptVerificationRequest()
        {
            if(_initiatedLocally)
            {
                State = ManagedVerificationState.StartingSas;
                _ = startSasAsync();
                return;
            }

            State = ManagedVerificationState.WaitingForVerificationData;
        }

        public void DidCancel()
        {
            State = ManagedVerificationState.Cancelled;
        }

        public void DidFail()
        {
            State = ManagedVerificationState.Failed;
        }

        public void DidFinish()
        {
            State = ManagedVerificationState.Completed;
        }

        public void DidReceiveVerificationData(SessionVerificationData data)
        {
            VerificationData = data;
            State = ManagedVerificationState.Comparing;
        }

        public void DidReceiveVerificationRequest(SessionVerificationRequestDetails details)
        {
            Reset();

            PendingRequest = details;
            State = ManagedVerificationState.AcknowledgingRequest;

            _ = acknowledgeAsync(details);
        }

        public void DidStartSasVerification()
        {
            State = ManagedVerificationState.WaitingForVerificationData;
        }

        public void Reset()
        {
            throwIfDisposed();

            PendingRequest = null;
            VerificationData = null;
            _initiatedLocally = false;
            State = ManagedVerificationState.Listening;
        }

        private async Task acknowledgeAsync(
            SessionVerificationRequestDetails details)
        {
            await GetController().AcknowledgeVerificationRequest(
                details.SenderProfile.UserId,
                details.FlowId);

            if (_pendingRequest?.FlowId == details.FlowId)
            {
                State = ManagedVerificationState.AwaitingUserAcceptance;
            }
        }

        private async Task startSasAsync()
        {
            await GetController().StartSasVerification();

            if (State == ManagedVerificationState.StartingSas)
            {
                State = ManagedVerificationState.WaitingForVerificationData;
            }
        }

        private void OnConnectionRecovered(
            object? sender,
            EventArgs e)
        {
            _ = ReplaceControllerAsync();
        }

        private async Task ReplaceControllerAsync()
        {
            await _controllerLock.WaitAsync();

            try
            {
                if (_disposed || _controller is null)
                {
                    return;
                }

                var controller =
                    await _client.GetSessionVerificationControllerAsync();

                if (_disposed)
                {
                    DestroyController(controller);
                    return;
                }

                var hadActiveVerification =
                    State != ManagedVerificationState.Listening;
                var previousController = _controller;

                _controller = controller;
                controller.SetDelegate(this);
                DestroyController(previousController);

                Reset();

                if (hadActiveVerification)
                {
                    State = ManagedVerificationState.Failed;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine(
                    $"Unable to restore session verification: {exception}");
            }
            finally
            {
                _controllerLock.Release();
            }
        }

        public async Task StopAsync()
        {
            await _controllerLock.WaitAsync();

            try
            {
                if (_controller is not null)
                {
                    DestroyController(_controller);
                    _controller = null;
                }

                _client.ConnectionRecovered -= OnConnectionRecovered;
                Reset();
            }
            finally
            {
                _controllerLock.Release();
            }
        }

        private async Task InitializeControllerAsync()
        {
            await _controllerLock.WaitAsync();

            try
            {
                if (_controller is not null)
                {
                    return;
                }

                var controller =
                    await _client.GetSessionVerificationControllerAsync();

                if (_disposed)
                {
                    DestroyController(controller);
                    return;
                }

                _controller = controller;
                controller.SetDelegate(this);
                _client.ConnectionRecovered += OnConnectionRecovered;
                Reset();
            }
            finally
            {
                _controllerLock.Release();
            }
        }

        private void onPropertyChanged(string propertyName)
        {
            var synchronizationContext = _synchronizationContext;

            if (synchronizationContext is not null &&
                SynchronizationContext.Current != synchronizationContext)
            {
                synchronizationContext.Post(
                    _ => onPropertyChanged(propertyName),
                    null);

                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void throwIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SessionVerificationService));
            }
        }

        private SessionVerificationController GetController()
        {
            return _controller ??
                throw new InvalidOperationException(
                    "Session verification is not initialized.");
        }

        // --- Dispose Implementation ---
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client.ConnectionRecovered -= OnConnectionRecovered;
            _client.NativeResourcesDisposing -= StopAsync;

            await _controllerLock.WaitAsync();

            try
            {
                if (_controller is not null)
                {
                    DestroyController(_controller);
                    _controller = null;
                }
            }
            finally
            {
                _controllerLock.Release();
            }

            PropertyChanged = null;
        }

        private static void DestroyController(
            SessionVerificationController controller)
        {
            try
            {
                controller.Dispose();
            }
            finally
            {
                controller.Destroy();
            }
        }
    }

    public enum ManagedVerificationState
    {
        /// <summary>
        /// The service is initialized and waiting for either an incoming
        /// verification request or a locally initiated verification flow.
        /// </summary>
        Listening,

        /// <summary>
        /// An incoming verification request has been received and is being
        /// acknowledged with the sending session.
        /// </summary>
        AcknowledgingRequest,

        /// <summary>
        /// An incoming verification request has been acknowledged and is waiting
        /// for the local user to accept or reject it.
        /// </summary>
        AwaitingUserAcceptance,

        /// <summary>
        /// The service is accepting an incoming verification request.
        /// </summary>
        AcceptingRequest,

        /// <summary>
        /// The service is sending a verification request to another signed-in
        /// session.
        /// </summary>
        RequestingVerification,

        /// <summary>
        /// A locally initiated verification request has been sent and the service
        /// is waiting for another session to accept it.
        /// </summary>
        WaitingForOtherSession,

        /// <summary>
        /// Short Authentication String verification is being started.
        /// </summary>
        StartingSas,

        /// <summary>
        /// SAS verification has started and the service is waiting for the
        /// comparison data to become available.
        /// </summary>
        WaitingForVerificationData,

        /// <summary>
        /// Verification data is available and should be compared with the values
        /// displayed by the other session.
        /// </summary>
        Comparing,

        /// <summary>
        /// The service is confirming that the verification values match.
        /// </summary>
        Approving,

        /// <summary>
        /// The service is reporting that the verification values do not match.
        /// </summary>
        Declining,

        /// <summary>
        /// The current verification flow is being cancelled.
        /// </summary>
        Cancelling,

        /// <summary>
        /// The verification flow completed successfully.
        /// </summary>
        Completed,

        /// <summary>
        /// The verification flow was cancelled by the local session, the remote
        /// session, or the Matrix server.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The verification flow could not be completed because an error occurred.
        /// </summary>
        Failed,
    }
}
