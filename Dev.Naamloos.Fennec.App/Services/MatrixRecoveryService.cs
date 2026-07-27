using Dev.Naamloos.Fennec.Sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Dev.Naamloos.Fennec.App.Services
{
    public sealed class MatrixRecoveryService
    {
        private readonly ManagedMatrixClient _client;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);

        public MatrixRecoveryService(ManagedMatrixClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task OnAppStoppedAsync(CancellationToken cancellationToken = default)
        {
            await _reconnectLock.WaitAsync(cancellationToken);

            try
            {
                await _client.PauseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Unable to pause Matrix client: {ex}");
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        public async Task OnAppResumedAsync(CancellationToken cancellationToken = default)
        {
            await _reconnectLock.WaitAsync(cancellationToken);

            try
            {
                if (await _client.ResumeAsync(cancellationToken))
                {
                    return;
                }

                if (!await _client.IsConnectedAsync(cancellationToken))
                {
                    await _client.ReconnectAsync(cancellationToken);
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(
                    $"Unable to reconnect Matrix client: {ex}");
            }
            finally
            {
                _reconnectLock.Release();
            }
        }
    }
}
