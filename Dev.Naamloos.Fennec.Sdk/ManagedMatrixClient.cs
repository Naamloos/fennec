using Dev.Naamloos.Fennec.Sdk.Helpers;
using Dev.Naamloos.Fennec.Sdk.Interfaces;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk
{
    /// <summary>
    /// ManagedMatrixClient is a wrapper around the Matrix Rust SDK.
    /// </summary>
    public class ManagedMatrixClient : IAsyncDisposable
    {
        private const string SESSION_STORAGE_KEY = "fennec.session";
        private const string STORE_KEY_STORAGE_KEY = "fennec.store.key";

        /// <summary>
        /// Whether the client is currently logged in. This property checks if the underlying <see cref="Client"/> instance has a valid user ID, indicating an active session.
        /// </summary>
        public bool IsLoggedIn => _client?.UserId() is not null;

        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _thumbnailCache = [];
        private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _videoCache = [];

        /// <summary>
        /// The <see cref="HttpClient"/> instance used for making non-SDK HTTP requests.
        /// </summary>
        private HttpClient? _httpClient;

        /// <summary>
        /// The underlying Matrix SDK client instance. This is initialized upon successful login or session recovery and is used for all Matrix-related operations.
        /// </summary>
        private Client? _client;

        /// <summary>
        /// The <see cref="SyncService"/> instance responsible for handling synchronization with the Matrix homeserver. This service manages the state of rooms, messages, and other data, ensuring that the client stays up-to-date with the server.
        /// </summary>
        private SyncService? _syncService;
        private SyncServiceStateObserver? _syncStateObserver;
        private TaskHandle? _syncStateHandle;
        private int _isCheckingSession;

        public event EventHandler? SessionInvalidated;

        /// <summary>
        /// The secure storage interface used to store and retrieve sensitive data, such as session information and encryption keys. 
        /// This allows for secure persistence of user sessions across application restarts.
        /// </summary>
        private IAsyncSecureStorage _secureStore;

        private string _accountPath;
        private string _platformName;

        public ManagedMatrixClient(string platformName, string accountPath, IAsyncSecureStorage secureStore)
        {
            this._platformName = platformName;
            this._accountPath = accountPath;
            this._secureStore = secureStore;
        }

        /// <summary>
        /// Attempts to log in to the Matrix homeserver using the provided credentials. If successful, it initializes the <see cref="Client"/> instance and stores the session securely.
        /// </summary>
        /// <param name="homeserver">The URL of the Matrix homeserver.</param>
        /// <param name="username">The username for the Matrix account.</param>
        /// <param name="password">The password for the Matrix account.</param>
        /// <returns>True if the login was successful; otherwise, false.</returns>
        public async Task<bool> LoginAsync(string homeserver, string username, string password)
        {
            await LogoutAsync();
            await _initializationLock.WaitAsync();

            try
            {
                var (dataPath, cachePath) = ensureAccountDirectoriesExist();

                var storeBuilder = new SqliteStoreBuilder(dataPath, cachePath)
                    .Key(await getOrGenerateKey());

                var clientBuilder = new ClientBuilder()
                    .Username(username)
                    .SqliteStore(storeBuilder)
                    .SlidingSyncVersionBuilder(SlidingSyncVersionBuilder.DiscoverNative)
                    .HomeserverUrl(homeserver);

                _client = await clientBuilder.Build();

                try
                {
                    await _client.Login(username, password, $"Fennec ({_platformName})", null);
                }
                catch (Exception)
                {
                    return false;
                }

                var session = _client.Session();
                var sessionJson = JsonSerializer.Serialize(session);
                await _secureStore.SetAsync(SESSION_STORAGE_KEY, sessionJson);

                await startSyncingAsync();
            }
            finally
            {
                _initializationLock.Release();
            }

            return true;
        }

        public async Task LogoutAsync()
        {
            await _initializationLock.WaitAsync();
            try
            {
                try
                {
                    if (_client is not null)
                        await _client.Logout();
                }
                finally
                {
                    await _secureStore.RemoveAsync(SESSION_STORAGE_KEY);
                    await _secureStore.RemoveAsync(STORE_KEY_STORAGE_KEY);
                    await nativeCleanupAsync();
                    await resetAccountDirectoryAsync();
                }
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        /// <summary>
        /// Attempts to recover a previously stored session from secure storage. If a valid session is found, it initializes the <see cref="Client"/> instance with the recovered session.
        /// </summary>
        /// <returns>True if a valid session was successfully recovered; otherwise, false.</returns>
        public async Task<bool> RecoverSessionAsync()
        {
            if (IsLoggedIn)
            {
                return true; // Already logged in, no need to recover session
            }

            await _initializationLock.WaitAsync();
            await nativeCleanupAsync();

            try
            {
                var serializedSession = await _secureStore.GetAsync(SESSION_STORAGE_KEY);

                // No session stored: return false
                if (string.IsNullOrWhiteSpace(serializedSession))
                {
                    return false;
                }

                var session = JsonSerializer.Deserialize<Session>(serializedSession);

                // No valid session found: return false
                if (session is null)
                {
                    return false;
                }

                _client = await new ClientBuilder()
                    .Username(session.UserId)
                    .SqliteStore(new SqliteStoreBuilder(Path.Combine(_accountPath, "data"), Path.Combine(_accountPath, "cache"))
                        .Key(await getOrGenerateKey()))
                    .SlidingSyncVersionBuilder(SlidingSyncVersionBuilder.DiscoverNative)
                    .Build();

                await _client.RestoreSession(session);

                if (!await IsSessionValidAsync())
                {
                    await nativeCleanupAsync(); return false;
                }

                await startSyncingAsync();
                return true;
            }
            catch (Exception)
            {
                await nativeCleanupAsync();
                // If anything goes wrong during session recovery, we return false to indicate failure.
                return false;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public Room[] GetRooms()
        {
            return _client?.Rooms() ?? Array.Empty<Room>();
        }

        public SyncService GetSyncService()
        {
            if (_syncService is null)
            {
                throw new InvalidOperationException("Sync service is not initialized.");
            }
            return _syncService;
        }

        public async Task<ObservableRoomList> GetObservableRoomListAsync()
        {
            if (_syncService is null)
            {
                throw new InvalidOperationException("Sync service is not initialized.");
            }

            var roomList = await _syncService.RoomListService().AllRooms();
            var spaceService = await (_client ?? throw new InvalidOperationException(
                "The client is not logged in.")).SpaceService();
            return new ObservableRoomList(roomList, spaceService);
        }

        public async Task<ObservableTimeline> GetObservableTimelineAsync(
            Room room,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(room);

            if (_syncService is null)
            {
                throw new InvalidOperationException(
                    "Sync service is not initialized.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var timeline = await room.Timeline();

            cancellationToken.ThrowIfCancellationRequested();

            return await ObservableTimeline.CreateAsync(
                timeline,
                cancellationToken: cancellationToken);
        }

        public Task<SessionVerificationController> GetSessionVerificationControllerAsync()
        {
            return (_client ?? throw new InvalidOperationException(
                "The client is not logged in."))
                .GetSessionVerificationController();
        }

        public Task<string> UploadMediaAsync(string mimeType, byte[] data) =>
            (_client ?? throw new InvalidOperationException(
                "The client is not logged in."))
            .UploadMedia(mimeType, data, null);

        public Task<UserProfile> GetOwnProfileAsync()
        {
            var client = _client ?? throw new InvalidOperationException(
                "The client is not logged in.");
            return client.GetProfile(client.UserId());
        }

        public Task<byte[]> GetThumbnailAsync(
            string source,
            ulong width,
            ulong height,
            bool isJson = true) =>
            _thumbnailCache.GetOrAdd(
                $"{width}x{height}:{source}",
                _ => new(() => DownloadThumbnailAsync(
                    source,
                    width,
                    height,
                isJson))).Value;

        public async Task<byte[]> GetMediaContentAsync(string sourceJson)
        {
            var client = _client ?? throw new InvalidOperationException(
                "The client is not logged in.");
            using var source = MediaSource.FromJson(sourceJson);
            return await client.GetMediaContent(source);
        }

        public Task<string> GetVideoFileAsync(
            string sourceJson,
            string filename,
            string mimeType) =>
            _videoCache.GetOrAdd(
                sourceJson,
                _ => new(() => DownloadVideoAsync(
                    sourceJson,
                    filename,
                    mimeType))).Value;

        private async Task<byte[]> DownloadThumbnailAsync(
            string sourceValue,
            ulong width,
            ulong height,
            bool isJson)
        {
            var client = _client ?? throw new InvalidOperationException(
                "The client is not logged in.");
            using var source = isJson
                ? MediaSource.FromJson(sourceValue)
                : MediaSource.FromUrl(sourceValue);
            return await client.GetMediaThumbnail(source, width, height);
        }

        private async Task<string> DownloadVideoAsync(
            string sourceJson,
            string filename,
            string mimeType)
        {
            var client = _client ?? throw new InvalidOperationException(
                "The client is not logged in.");
            var directory = Path.Combine(_accountPath, "cache", "media");
            Directory.CreateDirectory(directory);

            var extension = mimeType switch
            {
                "video/webm" => ".webm",
                "video/quicktime" => ".mov",
                _ => ".mp4",
            };
            var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(sourceJson)));
            var path = Path.Combine(directory, hash + extension);

            if (File.Exists(path))
            {
                return path;
            }

            using var source = MediaSource.FromJson(sourceJson);
            using var handle = await client.GetMediaFile(
                source,
                filename,
                mimeType,
                true,
                directory);
            if (!handle.Persist(path))
            {
                throw new IOException("Could not persist downloaded video.");
            }

            return path;
        }

        private async Task clearSavedSessionAsync()
        {
            await _secureStore.RemoveAsync(SESSION_STORAGE_KEY);

            await nativeCleanupAsync();
        }

        private async Task startSyncingAsync()
        {
            if(_client is null)
            {
                throw new InvalidOperationException("Cannot start sync: client is not initialized.");
            }

            _syncService = await _client.SyncService().Finish();
            await _syncService.Start();
            _syncStateObserver = new SyncStateObserver(OnSyncStateChanged);
            _syncStateHandle = _syncService.State(_syncStateObserver);
        }

        private void OnSyncStateChanged(SyncServiceState state)
        {
            if (state is SyncServiceState.Error or SyncServiceState.Terminated)
            {
                _ = CheckSessionAfterSyncFailureAsync();
            }
        }

        private async Task CheckSessionAfterSyncFailureAsync()
        {
            if (Interlocked.Exchange(ref _isCheckingSession, 1) != 0)
                return;

            try
            {
                if (!await IsSessionValidAsync())
                {
                    await LogoutAsync();
                    SessionInvalidated?.Invoke(this, EventArgs.Empty);
                }
            }
            catch
            {
                // The next sync state update retries transient network failures.
            }
            finally
            {
                Interlocked.Exchange(ref _isCheckingSession, 0);
            }
        }

        private async Task<bool> IsSessionValidAsync()
        {
            if (!IsLoggedIn)
                return false;

            var session = _client!.Session();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{session.HomeserverUrl.TrimEnd('/')}/_matrix/client/v3/account/whoami");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", session.AccessToken);
            _httpClient ??= new HttpClient();
            using var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        private async Task<HttpResponseMessage> SendHttpRequestAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null)
        {
            if (!IsLoggedIn)
            {
                throw new InvalidOperationException("Cannot send HTTP request: client is not logged in.");
            }

            var session = _client?.Session();
            using var request = new HttpRequestMessage(method, $"{session?.HomeserverUrl.TrimEnd('/')}{path}")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session?.AccessToken);

            if (_httpClient is null)
            {
                // Initialize the HttpClient if it hasn't been initialized yet
                _httpClient = new HttpClient();
            }

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var detail = await response.Content.ReadAsStringAsync();
                response.Dispose();
                throw new InvalidOperationException($"Matrix request failed ({status}): {detail}");
            }

            return response;
        }

        private (string dataPath, string cachePath) ensureAccountDirectoriesExist(bool reset = false)
        {
            var dataPath = Path.Combine(_accountPath, "data");
            var cachePath = Path.Combine(_accountPath, "cache");

            if(reset && Directory.Exists(_accountPath))
            {
                Directory.Delete(_accountPath, true);
            }

            Directory.CreateDirectory(_accountPath);
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(cachePath);

            return (dataPath, cachePath);
        }

        private async Task<byte[]> getOrGenerateKey()
        {
            var key = await _secureStore.GetAsync(STORE_KEY_STORAGE_KEY);

            if (key is not null)
            {
                var existingKeyBytes = Convert.FromBase64String(key);
                if (existingKeyBytes.Length != 32)
                {
                    throw new InvalidOperationException("Invalid key length retrieved from secure storage.");
                }

                return existingKeyBytes;
            }

            var newKeyBytes = RandomNumberGenerator.GetBytes(32);
            await _secureStore.SetAsync(STORE_KEY_STORAGE_KEY, Convert.ToBase64String(newKeyBytes));
            return newKeyBytes;
        }

        private async Task nativeCleanupAsync()
        {
            _thumbnailCache.Clear();
            _videoCache.Clear();

            try
            {
                _syncStateHandle?.Cancel();
            }
            catch
            {
                // The listener may already be stopped with its sync service.
            }
            _syncStateHandle?.Dispose();
            _syncStateHandle = null;
            _syncStateObserver = null;

            if (_syncService is not null)
            {
                try
                {
                    await _syncService.Stop();
                }
                catch (Exception)
                {
                    // Handle any exceptions that might occur during stopping the sync service.
                }

                _syncService.Dispose();
                _syncService = null;
            }
            _client?.Dispose();
            _client = null;
            _httpClient?.Dispose();
            _httpClient = null;
        }

        private async Task resetAccountDirectoryAsync()
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(_accountPath))
                    {
                        Directory.Delete(_accountPath, true);
                    }

                    ensureAccountDirectoriesExist();
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await nativeCleanupAsync();
            _initializationLock.Dispose();
        }

        private sealed class SyncStateObserver(Action<SyncServiceState> onUpdate) :
            SyncServiceStateObserver
        {
            public void OnUpdate(SyncServiceState state) => onUpdate(state);
        }
    }
}
