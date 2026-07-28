using Dev.Naamloos.Fennec.Sdk.Helpers;
using Dev.Naamloos.Fennec.Sdk.Interfaces;
using Dev.Naamloos.Fennec.Sdk.Entities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk;

/// <summary>
/// Provides a managed wrapper around the Matrix Rust SDK client.
/// </summary>
public sealed class ManagedMatrixClient : IAsyncDisposable
{
    private const string SessionStorageKey = "fennec.session";
    private const string StoreKeyStorageKey = "fennec.store.key";

    private const int StoreKeyLength = 32;
    private const int DirectoryDeleteRetryCount = 5;

    private readonly string _platformName;
    private readonly string _accountPath;
    private readonly IAsyncSecureStorage _secureStore;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly SemaphoreSlim _thumbnailLoadGate = new(3, 3);

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>>
        _videoCache = [];

    private HttpClient? _httpClient;

    private Client? _client;
    private SyncService? _syncService;
    private SyncServiceStateObserver? _syncStateObserver;
    private TaskHandle? _syncStateHandle;

    private SyncServiceState _state = SyncServiceState.Offline;

    private int _isCheckingSession;
    private bool _isPaused;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new managed Matrix client.
    /// </summary>
    /// <param name="platformName">
    /// The current platform name, used in the Matrix device display name.
    /// </param>
    /// <param name="accountPath">
    /// The directory used for the Matrix store, cache, and downloaded media.
    /// </param>
    /// <param name="secureStore">
    /// Secure storage used for the Matrix session and store encryption key.
    /// </param>
    public ManagedMatrixClient(
        string platformName,
        string accountPath,
        IAsyncSecureStorage secureStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformName);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountPath);
        ArgumentNullException.ThrowIfNull(secureStore);

        _platformName = platformName;
        _accountPath = accountPath;
        _secureStore = secureStore;
    }

    /// <summary>
    /// Gets whether the underlying Matrix client has an authenticated user.
    /// </summary>
    public bool IsLoggedIn => _client?.UserId() is not null;

    /// <summary>
    /// Occurs when the current Matrix session is no longer valid.
    /// </summary>
    public event EventHandler? SessionInvalidated;

    /// <summary>
    /// Occurs after the Matrix sync infrastructure has been recovered.
    /// </summary>
    /// <remarks>
    /// Subscribers should recreate native listeners, observers, controllers,
    /// and other handles derived from the Matrix Rust SDK client.
    /// </remarks>
    public event EventHandler? ConnectionRecovered;

    /// <summary>
    /// Occurs before the native Matrix client and its stores are released.
    /// Subscribers must release any native handles created from this client.
    /// </summary>
    public event Func<Task>? NativeResourcesDisposing;

    /// <summary>
    /// Attempts to log in to the Matrix homeserver.
    /// </summary>
    /// <param name="homeserver">The Matrix homeserver URL.</param>
    /// <param name="username">The Matrix username.</param>
    /// <param name="password">The Matrix password.</param>
    /// <returns>
    /// <see langword="true"/> when login succeeds; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public async Task<bool> LoginAsync(
        string homeserver,
        string username,
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeserver);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        ThrowIfDisposed();

        // LogoutAsync acquires the initialization lock itself.
        await LogoutAsync();

        await _initializationLock.WaitAsync();

        try
        {
            ThrowIfDisposed();

            var (dataPath, cachePath) =
                EnsureAccountDirectoriesExist();

            var storeBuilder = new SqliteStoreBuilder(
                    dataPath,
                    cachePath)
                .Key(await GetOrGenerateStoreKeyAsync());

            var client = await new ClientBuilder()
                .Username(username)
                .SqliteStore(storeBuilder)
                .SlidingSyncVersionBuilder(
                    SlidingSyncVersionBuilder.DiscoverNative)
                .HomeserverUrl(homeserver)
                .Build();

            try
            {
                await client.Login(
                    username,
                    password,
                    $"Fennec ({_platformName})",
                    null);
            }
            catch
            {
                DestroyClient(client);
                return false;
            }

            _client = client;

            try
            {
                var serializedSession =
                    JsonSerializer.Serialize(client.Session());

                await _secureStore.SetAsync(
                    SessionStorageKey,
                    serializedSession);

                await StartSyncingAsync();

                return true;
            }
            catch
            {
                await NativeCleanupAsync();
                throw;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Logs out, removes the stored session, and clears local account data.
    /// </summary>
    public async Task LogoutAsync()
    {
        await _initializationLock.WaitAsync();

        try
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                if (_client is not null && IsLoggedIn)
                {
                    await _client.Logout();
                }
            }
            catch
            {
                // Local cleanup must still run when logout cannot reach the
                // homeserver or the access token has already expired.
            }
            finally
            {
                await _secureStore.RemoveAsync(SessionStorageKey);
                await _secureStore.RemoveAsync(StoreKeyStorageKey);

                await NativeCleanupAsync();
                await ResetAccountDirectoryAsync();
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Attempts to restore a previously stored Matrix session.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a stored session was restored successfully;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public async Task<bool> RecoverSessionAsync()
    {
        ThrowIfDisposed();

        if (IsLoggedIn)
        {
            return true;
        }

        await _initializationLock.WaitAsync();

        try
        {
            ThrowIfDisposed();

            await NativeCleanupAsync();

            var serializedSession =
                await _secureStore.GetAsync(SessionStorageKey);

            if (string.IsNullOrWhiteSpace(serializedSession))
            {
                return false;
            }

            Session? session;

            try
            {
                session = JsonSerializer.Deserialize<Session>(
                    serializedSession);
            }
            catch (JsonException)
            {
                await ClearSavedSessionAsync();
                return false;
            }

            if (session is null)
            {
                await ClearSavedSessionAsync();
                return false;
            }

            var (dataPath, cachePath) =
                EnsureAccountDirectoriesExist();

            var client = await new ClientBuilder()
                .Username(session.UserId)
                .SqliteStore(
                    new SqliteStoreBuilder(
                            dataPath,
                            cachePath)
                        .Key(await GetOrGenerateStoreKeyAsync()))
                .SlidingSyncVersionBuilder(
                    SlidingSyncVersionBuilder.DiscoverNative)
                .HomeserverUrl(session.HomeserverUrl)
                .Build();

            _client = client;

            try
            {
                await client.RestoreSession(session);
                await StartSyncingAsync();

                return true;
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex);
                await NativeCleanupAsync();
                return false;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Determines whether the Matrix client and sync infrastructure currently
    /// appear connected and usable.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token used to cancel session validation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the client is logged in and the sync service
    /// is not offline, terminated, or in an error state; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    public async Task<bool> IsConnectedAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (_client is null ||
            !IsLoggedIn ||
            _syncService is null)
        {
            return false;
        }

        if (_state is not (
            SyncServiceState.Offline or
            SyncServiceState.Error or
            SyncServiceState.Terminated))
        {
            return true;
        }

        var validity =
            await GetSessionValidityAsync(cancellationToken);

        if (validity == SessionValidity.Invalid)
        {
            await InvalidateSessionAsync();
        }

        return false;
    }

    /// <summary>
    /// Restarts or rebuilds the Matrix sync service.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token used to cancel the recovery operation.
    /// </param>
    /// <remarks>
    /// This method first attempts to restart the existing UniFFI-backed sync
    /// service. If that native object can no longer be reused, the sync service
    /// and state observer are destroyed and rebuilt from the existing client.
    /// </remarks>
    public async Task ReconnectAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (_client is null || !IsLoggedIn)
            {
                throw new InvalidOperationException(
                    "Cannot reconnect because the client is not logged in.");
            }

            var validity =
                await GetSessionValidityAsync(cancellationToken);

            if (validity == SessionValidity.Invalid)
            {
                await InvalidateSessionAsync();

                throw new InvalidOperationException(
                    "Cannot reconnect because the Matrix session is invalid.");
            }

            var restarted =
                await TryRestartSyncServiceAsync(cancellationToken);

            if (!restarted)
            {
                await StartSyncingAsync();
            }

            ConnectionRecovered?.Invoke(
                this,
                EventArgs.Empty);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Stops synchronization and releases native store resources while the app
    /// is in the background.
    /// </summary>
    public async Task PauseAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (_isPaused || _client is null || !IsLoggedIn)
            {
                return;
            }

            await StopAndDisposeSyncServiceAsync();
            cancellationToken.ThrowIfCancellationRequested();

            await _client.Pause();
            _isPaused = true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Restores native store resources and synchronization after a pause.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the client was resumed; otherwise,
    /// <see langword="false"/> when it was not paused.
    /// </returns>
    public async Task<bool> ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            if (!_isPaused)
            {
                return false;
            }

            if (_client is null || !IsLoggedIn)
            {
                _isPaused = false;
                return false;
            }

            await _client.Resume();
            cancellationToken.ThrowIfCancellationRequested();

            _isPaused = false;
            await StartSyncingAsync();

            ConnectionRecovered?.Invoke(
                this,
                EventArgs.Empty);

            return true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Gets all rooms currently known by the Matrix client.
    /// </summary>
    public Room[] GetRooms()
    {
        return _client?.Rooms() ?? [];
    }

    /// <summary>
    /// Gets the active Matrix sync service.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when synchronization has not been initialized.
    /// </exception>
    public SyncService GetSyncService()
    {
        ThrowIfDisposed();

        return _syncService ??
            throw new InvalidOperationException(
                "Sync service is not initialized.");
    }

    /// <summary>
    /// Creates an observable room list using the supplied initial filter.
    /// </summary>
    /// <param name="initialFilter">The initial room-list filter.</param>
    public async Task<ObservableRoomList> GetObservableRoomListAsync(
        RoomListEntriesDynamicFilterKind initialFilter)
    {
        var roomList = await GetSyncService()
            .RoomListService()
            .AllRooms();

        return new ObservableRoomList(
            this,
            roomList,
            initialFilter);
    }

    /// <summary>
    /// Creates a live list of all rooms exposed by a space, including rooms
    /// the user has not joined.
    /// </summary>
    public Task<ObservableSpaceRoomList> GetObservableSpaceRoomListAsync(
        string spaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        ThrowIfDisposed();
        return ObservableSpaceRoomList.CreateAsync(this, spaceId);
    }

    /// <summary>
    /// Gets a Matrix Rust SDK space service for a short-lived wrapper.
    /// </summary>
    internal async Task<SpaceService> GetSpaceServiceAsync()
    {
        ThrowIfDisposed();

        return await (_client?.SpaceService()
            ?? throw new InvalidOperationException(
                "Matrix client is not initialized."));
    }

    /// <summary>
    /// Creates a live view of every room that belongs to a space.
    /// </summary>
    public Task<ObservableSpaceRoomIds> GetObservableSpaceRoomIdsAsync()
    {
        ThrowIfDisposed();
        return ObservableSpaceRoomIds.CreateAsync(this);
    }

    /// <summary>
    /// Opens a space child, joining it when necessary.
    /// </summary>
    public async Task<ManagedRoom> OpenSpaceRoomAsync(ManagedSpaceRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        ThrowIfDisposed();

        var nativeRoom = room.IsJoined
            ? GetSyncService().RoomListService().Room(room.Id)
            : await (_client?.JoinRoomByIdOrAlias(room.CanonicalAlias ?? room.Id, room.Via)
                ?? throw new InvalidOperationException(
                    "Matrix client is not initialized."));

        return new ManagedRoom(nativeRoom);
    }

    /// <summary>
    /// Creates an observable timeline for a room.
    /// </summary>
    /// <param name="room">The Matrix room.</param>
    /// <param name="cancellationToken">
    /// A token used to cancel timeline creation.
    /// </param>
    public async Task<ObservableTimeline> GetObservableTimelineAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);

        _ = GetSyncService();

        cancellationToken.ThrowIfCancellationRequested();

        var timeline = await room.Timeline();

        cancellationToken.ThrowIfCancellationRequested();

        return await ObservableTimeline.CreateAsync(
            this,
            timeline,
            cancellationToken: cancellationToken);
    }

    public async Task<SessionVerificationService> GetSessionVerificationServiceAsync()
    {
        return await SessionVerificationService.CreateAsync(this);
    }

    /// <summary>
    /// Gets a Matrix session-verification controller.
    /// </summary>
    internal Task<SessionVerificationController>
        GetSessionVerificationControllerAsync()
    {
        return GetRequiredClient()
            .GetSessionVerificationController();
    }

    /// <summary>
    /// Uploads media to the Matrix homeserver.
    /// </summary>
    /// <param name="mimeType">The media MIME type.</param>
    /// <param name="data">The media data.</param>
    public Task<string> UploadMediaAsync(
        string mimeType,
        byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(data);

        return GetRequiredClient()
            .UploadMedia(
                mimeType,
                data,
                null);
    }

    /// <summary>
    /// Gets the authenticated user's profile.
    /// </summary>
    public Task<UserProfile> GetOwnProfileAsync()
    {
        var client = GetRequiredClient();

        return client.GetProfile(
            client.UserId());
    }

    public Task<MatrixProfile> GetOwnMatrixProfileAsync() =>
        GetMatrixProfileAsync(GetRequiredClient().UserId());

    public async Task<MatrixProfile> GetMatrixProfileAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var displayName = userId;
        string? avatarUrl = null, bio = null, timeZone = null;
        IReadOnlyList<string> pronouns = [];

        try
        {
            var basic = await GetRequiredClient().GetProfile(userId);
            displayName = string.IsNullOrWhiteSpace(basic.DisplayName) ? userId : basic.DisplayName;
            avatarUrl = string.IsNullOrWhiteSpace(basic.AvatarUrl) ? null : basic.AvatarUrl;
        }
        catch { }

        try
        {
            using var response = await SendHttpRequestAsync(HttpMethod.Get,
                $"/_matrix/client/v3/profile/{Uri.EscapeDataString(userId)}");
            using var profile = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = profile.RootElement;
            if (root.TryGetProperty("displayname", out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
                displayName = value.GetString()!;
            if (root.TryGetProperty("avatar_url", out value) && !string.IsNullOrWhiteSpace(value.GetString()))
                avatarUrl = value.GetString();
            bio = root.TryGetProperty("m.bio", out value) ? value.GetString() : null;
            timeZone = root.TryGetProperty("m.tz", out value) || root.TryGetProperty("us.cloke.msc4175.tz", out value)
                ? value.GetString() : null;
            if (!(root.TryGetProperty("m.pronouns", out var pronounValue) ||
                  root.TryGetProperty("io.fsky.nyx.pronouns", out pronounValue)) ||
                pronounValue.ValueKind != JsonValueKind.Array)
            {
                pronounValue = default;
            }
            pronouns = pronounValue.ValueKind == JsonValueKind.Array
                ? pronounValue.EnumerateArray().Where(pronoun => pronoun.TryGetProperty("summary", out _))
                    .Select(pronoun => pronoun.GetProperty("summary").GetString()).OfType<string>().ToArray()
                : [];
        }
        catch { }

        string? presence = null, status = null;
        try
        {
            using var presenceResponse = await SendHttpRequestAsync(HttpMethod.Get,
                $"/_matrix/client/v3/presence/{Uri.EscapeDataString(userId)}/status");
            using var presenceDocument = JsonDocument.Parse(await presenceResponse.Content.ReadAsStringAsync());
            presence = presenceDocument.RootElement.TryGetProperty("presence", out var presenceValue) ? presenceValue.GetString() : null;
            status = presenceDocument.RootElement.TryGetProperty("status_msg", out presenceValue) ? presenceValue.GetString() : null;
        }
        catch { }

        return new MatrixProfile(userId,
            displayName,
            avatarUrl,
            bio,
            status, presence,
            timeZone,
            pronouns,
            userId[(userId.IndexOf(':') + 1)..]);
    }

    public async Task SetOwnMatrixProfileAsync(MatrixProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await SetOwnDisplayNameAsync(profile.DisplayName);
        await SetProfileFieldAsync("m.bio", profile.Bio);
        await SetProfileFieldAsync("m.tz", profile.TimeZone);
        await SetProfileFieldAsync("m.pronouns", profile.Pronouns.Select(summary => new { language = "en", summary }).ToArray());
        using var body = JsonContent.Create(new { presence = profile.Presence ?? "online", status_msg = profile.Status });
        using var response = await SendHttpRequestAsync(HttpMethod.Put,
            $"/_matrix/client/v3/presence/{Uri.EscapeDataString(GetRequiredClient().UserId())}/status", body);
    }

    private async Task SetProfileFieldAsync(string field, object? value)
    {
        using var body = JsonContent.Create(new Dictionary<string, object?> { [field] = value });
        using var response = await SendHttpRequestAsync(HttpMethod.Put,
            $"/_matrix/client/v3/profile/{Uri.EscapeDataString(GetRequiredClient().UserId())}/{Uri.EscapeDataString(field)}", body);
    }

    /// <summary>Updates the authenticated user's display name.</summary>
    public Task SetOwnDisplayNameAsync(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return GetRequiredClient().SetDisplayName(displayName);
    }

    /// <summary>Uploads and sets the authenticated user's avatar.</summary>
    public async Task SetOwnAvatarAsync(string mimeType, byte[] data)
    {
        var url = await UploadMediaAsync(mimeType, data);
        await GetRequiredClient().SetAvatarUrl(url);
    }

    /// <summary>Gets the user's active Matrix sessions.</summary>
    public async Task<IReadOnlyList<MatrixSession>> GetSessionsAsync()
    {
        using var response = await SendHttpRequestAsync(
            HttpMethod.Get,
            "/_matrix/client/v3/devices");
        var content = await response.Content.ReadAsStringAsync();
        var devices = JsonSerializer.Deserialize<DeviceListResponse>(content) ?? new([]);
        var session = GetRequiredClient().Session();
        var verifiedDeviceIds = await GetVerifiedDeviceIdsAsync(session.UserId);

        return devices.Devices.Select(device => new MatrixSession(
            device.DeviceId,
            device.DisplayName ?? device.DeviceId,
            device.LastSeenTimestamp is { } timestamp
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : null,
            device.LastSeenIp,
            device.DeviceId == session.DeviceId,
            verifiedDeviceIds.Contains(device.DeviceId))).ToArray();
    }

    /// <summary>Renames a Matrix session.</summary>
    public async Task RenameSessionAsync(string deviceId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        using var body = JsonContent.Create(new { display_name = displayName });
        using var response = await SendHttpRequestAsync(
            HttpMethod.Put,
            $"/_matrix/client/v3/devices/{Uri.EscapeDataString(deviceId)}",
            body);
    }

    /// <summary>Removes a Matrix session after password-based UI authentication.</summary>
    public async Task RemoveSessionAsync(string deviceId, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var session = GetRequiredClient().Session();
        using var body = JsonContent.Create(new
        {
            auth = new
            {
                type = "m.login.password",
                identifier = new { type = "m.id.user", user = session.UserId },
                password,
            },
        });

        using var response = await SendHttpRequestAsync(
            HttpMethod.Delete,
            $"/_matrix/client/v3/devices/{Uri.EscapeDataString(deviceId)}",
            body);
    }

    /// <summary>Reads a global account-data event.</summary>
    public async Task<string?> GetAccountDataAsync(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return await GetRequiredClient().AccountData(eventType);
    }

    /// <summary>Writes a global account-data event.</summary>
    public Task SetAccountDataAsync(string eventType, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        return GetRequiredClient().SetAccountData(eventType, content);
    }

    private const string WallpaperTag = "u.fennec.wallpaper";
    private const string LegacyWallpaperTagPrefix = WallpaperTag + ".";
    private const string GlobalWallpaperEventType = "dev.naamloos.fennec.wallpaper";

    public event Action<string?>? GlobalWallpaperChanged;

    public async Task<string?> GetGlobalWallpaperAsync()
    {
        var content = await GetAccountDataAsync(GlobalWallpaperEventType);
        if (string.IsNullOrWhiteSpace(content)) return null;

        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("url", out var url) &&
               !string.IsNullOrWhiteSpace(url.GetString())
            ? url.GetString()
            : null;
    }

    public async Task SetGlobalWallpaperAsync(string mxcUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mxcUrl);
        await SetAccountDataAsync(GlobalWallpaperEventType, JsonSerializer.Serialize(new { url = mxcUrl }));
        GlobalWallpaperChanged?.Invoke(mxcUrl);
    }

    public async Task ClearGlobalWallpaperAsync()
    {
        await SetAccountDataAsync(GlobalWallpaperEventType, "{}");
        GlobalWallpaperChanged?.Invoke(null);
    }

    public async Task<string?> GetRoomWallpaperAsync(string roomId)
    {
        using var response = await SendHttpRequestAsync(HttpMethod.Get,
            RoomTagsPath(roomId));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("tags", out var tags)) return null;
        if (tags.TryGetProperty(WallpaperTag, out var wallpaperTag) &&
            wallpaperTag.TryGetProperty("url", out var url) &&
            !string.IsNullOrWhiteSpace(url.GetString()))
        {
            return url.GetString();
        }

        var tag = tags.EnumerateObject().FirstOrDefault(tag => tag.Name.StartsWith(LegacyWallpaperTagPrefix, StringComparison.Ordinal));
        if (tag.Name is null) return null;
        var encoded = tag.Name[LegacyWallpaperTagPrefix.Length..].Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=')));
    }

    public async Task SetRoomWallpaperAsync(string roomId, string mxcUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mxcUrl);

        await ClearRoomWallpaperAsync(roomId);
        var tagsPath = RoomTagsPath(roomId);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(mxcUrl))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tag = LegacyWallpaperTagPrefix + encoded;
        using var body = JsonContent.Create(new { });
        using var response = await SendHttpRequestAsync(HttpMethod.Put,
            $"{tagsPath}/{Uri.EscapeDataString(tag)}", body);

    }

    public async Task ClearRoomWallpaperAsync(string roomId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        var tagsPath = RoomTagsPath(roomId);
        using (var existingResponse = await SendHttpRequestAsync(HttpMethod.Get, tagsPath))
        using (var document = JsonDocument.Parse(await existingResponse.Content.ReadAsStringAsync()))
        {
            if (document.RootElement.TryGetProperty("tags", out var tags))
            {
                foreach (var existing in tags.EnumerateObject()
                             .Where(tag => tag.Name == WallpaperTag || tag.Name.StartsWith(LegacyWallpaperTagPrefix, StringComparison.Ordinal))
                             .Select(tag => tag.Name)
                             .ToArray())
                {
                    using var deleteResponse = await SendHttpRequestAsync(
                        HttpMethod.Delete,
                        $"{tagsPath}/{Uri.EscapeDataString(existing)}");
                }
            }
        }

    }

    private string RoomTagsPath(string roomId) =>
        $"/_matrix/client/v3/user/{Uri.EscapeDataString(GetRequiredClient().UserId())}/rooms/{Uri.EscapeDataString(roomId)}/tags";

    /// <summary>Gets Fennec's personal emote pack.</summary>
    public async Task<IReadOnlyList<MatrixEmote>> GetUserEmotesAsync()
    {
        var content = await GetAccountDataAsync("im.ponies.user_emotes");
        if (string.IsNullOrWhiteSpace(content)) return [];

        using var document = JsonDocument.Parse(content);
        var images = document.RootElement.TryGetProperty("images", out var pack)
            ? pack : document.RootElement;
        if (images.ValueKind != JsonValueKind.Object) return [];

        return images.EnumerateObject()
            .Where(image => image.Value.TryGetProperty("url", out var url) &&
                !string.IsNullOrWhiteSpace(url.GetString()))
            .Select(image => new MatrixEmote(
                image.Name,
                image.Value.TryGetProperty("body", out var body)
                    ? body.GetString() ?? image.Name
                    : image.Name,
                image.Value.GetProperty("url").GetString()!))
            .ToArray();
    }

    /// <summary>Saves Fennec's personal emote pack.</summary>
    public Task SetUserEmotesAsync(IEnumerable<MatrixEmote> emotes) =>
        SetAccountDataAsync(
            "im.ponies.user_emotes",
            JsonSerializer.Serialize(new
            {
                images = emotes.ToDictionary(
                    emote => emote.Name,
                    emote => new { url = emote.Source, body = emote.Body, usage = new[] { "emoticon", "sticker" } }),
            }));

    /// <summary>Returns the global account-data events currently visible to this client.</summary>
    public async Task<IReadOnlyList<GlobalAccountData>> GetGlobalAccountDataAsync()
    {
        var filter = JsonSerializer.Serialize(new
        {
            presence = new { limit = 0 },
            room = new
            {
                timeline = new { limit = 0 },
                state = new { lazy_load_members = true },
                ephemeral = new { limit = 0 },
                account_data = new { limit = 0 },
            },
            account_data = new { limit = 1000 },
        });

        using var response = await SendHttpRequestAsync(
            HttpMethod.Get,
            $"/_matrix/client/v3/sync?timeout=0&filter={Uri.EscapeDataString(filter)}");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var result = new List<GlobalAccountData>();
        if (!document.RootElement.TryGetProperty("account_data", out var accountData) ||
            !accountData.TryGetProperty("events", out var events))
        {
            return result;
        }

        foreach (var @event in events.EnumerateArray())
        {
            if (@event.TryGetProperty("type", out var type) &&
                @event.TryGetProperty("content", out var content))
            {
                result.Add(new GlobalAccountData(
                    type.GetString() ?? string.Empty,
                    content.GetRawText()));
            }
        }

        return result;
    }

    /// <summary>Gets the access token for the current authenticated session.</summary>
    public string GetAccessToken() => GetRequiredClient().Session().AccessToken;

    private async Task<HashSet<string>> GetVerifiedDeviceIdsAsync(string userId)
    {
        try
        {
            using var body = JsonContent.Create(new
            {
                device_keys = new Dictionary<string, string[]> { [userId] = [] },
            });
            using var response = await SendHttpRequestAsync(
                HttpMethod.Post,
                "/_matrix/client/v3/keys/query",
                body);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            if (!document.RootElement.TryGetProperty("self_signing_keys", out var selfSigningKeys) ||
                !selfSigningKeys.TryGetProperty(userId, out var selfSigningKey) ||
                !selfSigningKey.TryGetProperty("keys", out var keys) ||
                !document.RootElement.TryGetProperty("device_keys", out var deviceKeys) ||
                !deviceKeys.TryGetProperty(userId, out var userDevices))
            {
                return [];
            }

            var signingKeyIds = keys.EnumerateObject()
                .Select(key => key.Name)
                .ToHashSet(StringComparer.Ordinal);
            var verified = new HashSet<string>(StringComparer.Ordinal);
            foreach (var device in userDevices.EnumerateObject())
            {
                if (device.Value.TryGetProperty("signatures", out var signatures) &&
                    signatures.TryGetProperty(userId, out var userSignatures) &&
                    userSignatures.EnumerateObject().Any(signature =>
                        signingKeyIds.Contains(signature.Name)))
                {
                    verified.Add(device.Name);
                }
            }

            return verified;
        }
        catch
        {
            // Treat unavailable verification data as unverified rather than hiding it.
            return [];
        }
    }

    /// <summary>
    /// Downloads a Matrix media thumbnail.
    /// </summary>
    /// <param name="source">The serialized or URL media source.</param>
    /// <param name="width">The requested thumbnail width.</param>
    /// <param name="height">The requested thumbnail height.</param>
    /// <param name="isJson">
    /// Whether <paramref name="source"/> is a serialized media source.
    /// </param>
    public async Task<byte[]> GetThumbnailAsync(
        string source,
        ulong width,
        ulong height,
        bool isJson = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        await _thumbnailLoadGate.WaitAsync(cancellationToken);
        try
        {
            return await DownloadThumbnailAsync(
                source,
                width,
                height,
                isJson);
        }
        finally
        {
            _thumbnailLoadGate.Release();
        }
    }

    /// <summary>
    /// Downloads the complete content of a Matrix media source.
    /// </summary>
    /// <param name="sourceJson">
    /// The serialized Matrix media source.
    /// </param>
    public Task<byte[]> GetMediaContentAsync(string sourceJson) =>
        GetMediaContentAsync(sourceJson, isJson: true);

    public async Task<byte[]> GetMediaContentAsync(
        string sourceValue,
        bool isJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceValue);

        using var source = isJson
            ? MediaSource.FromJson(sourceValue)
            : MediaSource.FromUrl(sourceValue);

        return await GetRequiredClient()
            .GetMediaContent(source);
    }

    /// <summary>
    /// Downloads and caches a Matrix video as a local file.
    /// </summary>
    /// <param name="sourceJson">
    /// The serialized Matrix media source.
    /// </param>
    /// <param name="filename">The original video filename.</param>
    /// <param name="mimeType">The video MIME type.</param>
    public Task<string> GetVideoFileAsync(
        string sourceJson,
        string filename,
        string mimeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var cacheKey =
            $"{mimeType}:{filename}:{sourceJson}";

        return GetCachedValueAsync(
            _videoCache,
            cacheKey,
            () => DownloadVideoAsync(
                sourceJson,
                filename,
                mimeType));
    }

    private Client GetRequiredClient()
    {
        ThrowIfDisposed();

        return _client ??
            throw new InvalidOperationException(
                "The client is not logged in.");
    }

    private async Task StartSyncingAsync()
    {
        var client = GetRequiredClient();

        await StopAndDisposeSyncServiceAsync();

        var syncService =
            await client.SyncService().Finish();

        var stateObserver =
            new SyncStateObserver(OnSyncStateChanged);

        TaskHandle? stateHandle = null;

        try
        {
            // Register the observer before starting so that no initial state
            // transitions are missed.
            stateHandle =
                syncService.State(stateObserver);

            _state = SyncServiceState.Offline;
            _syncService = syncService;
            _syncStateObserver = stateObserver;
            _syncStateHandle = stateHandle;

            await syncService.Start();
        }
        catch
        {
            try
            {
                stateHandle?.Cancel();
            }
            catch
            {
                // The UniFFI listener may not have become active yet.
            }

            stateHandle?.Dispose();

            if (ReferenceEquals(_syncService, syncService))
            {
                _syncService = null;
                _syncStateObserver = null;
                _syncStateHandle = null;
            }

            DestroySyncService(syncService);

            throw;
        }
    }

    private async Task<bool> TryRestartSyncServiceAsync(
        CancellationToken cancellationToken)
    {
        var syncService = _syncService;

        if (syncService is null)
        {
            return false;
        }

        try
        {
            try
            {
                await syncService.Stop();
            }
            catch
            {
                // The service may already have stopped while the app was
                // suspended.
            }

            cancellationToken.ThrowIfCancellationRequested();

            _state = SyncServiceState.Offline;

            await syncService.Start();

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A UniFFI object can remain referenced by managed code while its
            // underlying Rust task is no longer reusable. Rebuild it.
            await StopAndDisposeSyncServiceAsync();

            return false;
        }
    }

    private void OnSyncStateChanged(
        SyncServiceState state)
    {
        _state = state;

        if (state is
            SyncServiceState.Error or
            SyncServiceState.Terminated)
        {
            _ = CheckSessionAfterSyncFailureAsync();
        }
    }

    private async Task CheckSessionAfterSyncFailureAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(
                ref _isCheckingSession,
                1) != 0)
        {
            return;
        }

        try
        {
            var validity =
                await GetSessionValidityAsync(
                    cancellationToken);

            if (validity == SessionValidity.Invalid)
            {
                await InvalidateSessionAsync();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A homeserver or network failure is not proof that the session
            // itself is invalid. A later state change or app resume can retry.
        }
        finally
        {
            Interlocked.Exchange(
                ref _isCheckingSession,
                0);
        }
    }

    private async Task InvalidateSessionAsync()
    {
        await LogoutAsync();

        SessionInvalidated?.Invoke(
            this,
            EventArgs.Empty);
    }

    private async Task<SessionValidity> GetSessionValidityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_client is null || !IsLoggedIn)
        {
            return SessionValidity.Invalid;
        }

        var session = _client.Session();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildMatrixUrl(
                session.HomeserverUrl,
                "/_matrix/client/v3/account/whoami"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        try
        {
            using var response =
                await GetHttpClient().SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return SessionValidity.Valid;
            }

            return response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden
                    ? SessionValidity.Invalid
                    : SessionValidity.Unknown;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return SessionValidity.Unknown;
        }
    }

    private async Task<byte[]> DownloadThumbnailAsync(
        string sourceValue,
        ulong width,
        ulong height,
        bool isJson)
    {
        using var source = isJson
            ? MediaSource.FromJson(sourceValue)
            : MediaSource.FromUrl(sourceValue);

        return await GetRequiredClient()
            .GetMediaThumbnail(
                source,
                width,
                height);
    }

    private async Task<string> DownloadVideoAsync(
        string sourceJson,
        string filename,
        string mimeType)
    {
        var directory = Path.Combine(
            _accountPath,
            "cache",
            "media");

        Directory.CreateDirectory(directory);

        var extension =
            GetVideoExtension(mimeType);

        var hash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(sourceJson)));

        var path =
            Path.Combine(
                directory,
                hash + extension);

        if (File.Exists(path))
        {
            return path;
        }

        using var source =
            MediaSource.FromJson(sourceJson);

        using var handle =
            await GetRequiredClient().GetMediaFile(
                source,
                filename,
                mimeType,
                true,
                directory);

        if (!handle.Persist(path))
        {
            throw new IOException(
                "Could not persist the downloaded video.");
        }

        return path;
    }

    private static string GetVideoExtension(
        string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-matroska" => ".mkv",
            _ => ".mp4",
        };
    }

    private async Task<HttpResponseMessage> SendHttpRequestAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var session =
            GetRequiredClient().Session();

        using var request = new HttpRequestMessage(
            method,
            BuildMatrixUrl(
                session.HomeserverUrl,
                path))
        {
            Content = content,
        };

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                session.AccessToken);

        var response =
            await GetHttpClient().SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            var status =
                (int)response.StatusCode;

            var detail =
                await response.Content.ReadAsStringAsync();

            throw new InvalidOperationException(
                $"Matrix request failed ({status}): {detail}");
        }
        finally
        {
            response.Dispose();
        }
    }

    private sealed record DeviceListResponse(
        [property: JsonPropertyName("devices")]
        DeviceResponse[] Devices);

    private sealed record DeviceResponse(
        [property: JsonPropertyName("device_id")]
        string DeviceId,
        [property: JsonPropertyName("display_name")]
        string? DisplayName,
        [property: JsonPropertyName("last_seen_ip")]
        string? LastSeenIp,
        [property: JsonPropertyName("last_seen_ts")]
        long? LastSeenTimestamp);

    private HttpClient GetHttpClient()
    {
        return _httpClient ??= new HttpClient();
    }

    private static string BuildMatrixUrl(
        string homeserver,
        string path)
    {
        return $"{homeserver.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private async Task ClearSavedSessionAsync()
    {
        await _secureStore.RemoveAsync(SessionStorageKey);
        await NativeCleanupAsync();
    }

    private (
        string DataPath,
        string CachePath)
        EnsureAccountDirectoriesExist(
            bool reset = false)
    {
        var dataPath =
            Path.Combine(
                _accountPath,
                "data");

        var cachePath =
            Path.Combine(
                _accountPath,
                "cache");

        if (reset && Directory.Exists(_accountPath))
        {
            Directory.Delete(
                _accountPath,
                recursive: true);
        }

        Directory.CreateDirectory(_accountPath);
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(cachePath);

        return (dataPath, cachePath);
    }

    private async Task<byte[]> GetOrGenerateStoreKeyAsync()
    {
        var storedKey =
            await _secureStore.GetAsync(
                StoreKeyStorageKey);

        if (!string.IsNullOrWhiteSpace(storedKey))
        {
            byte[] key;

            try
            {
                key =
                    Convert.FromBase64String(storedKey);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "The Matrix store key in secure storage is invalid.",
                    exception);
            }

            if (key.Length != StoreKeyLength)
            {
                throw new InvalidOperationException(
                    $"The Matrix store key must be " +
                    $"{StoreKeyLength} bytes.");
            }

            return key;
        }

        var newKey =
            RandomNumberGenerator.GetBytes(
                StoreKeyLength);

        await _secureStore.SetAsync(
            StoreKeyStorageKey,
            Convert.ToBase64String(newKey));

        return newKey;
    }

    private async Task NativeCleanupAsync()
    {
        _videoCache.Clear();

        await StopNativeDependentsAsync();

        await StopAndDisposeSyncServiceAsync();

        if (_client is not null)
        {
            DestroyClient(_client);
            _client = null;
        }

        _state = SyncServiceState.Offline;
        _isPaused = false;

        _httpClient?.Dispose();
        _httpClient = null;
    }

    private async Task StopNativeDependentsAsync()
    {
        var handlers = NativeResourcesDisposing;

        if (handlers is null)
        {
            return;
        }

        foreach (Func<Task> handler in handlers.GetInvocationList())
        {
            try
            {
                await handler();
            }
            catch
            {
                // Cleanup must still release the Matrix client and its store.
            }
        }
    }

    private async Task StopAndDisposeSyncServiceAsync()
    {
        var stateHandle = _syncStateHandle;

        _syncStateHandle = null;
        _syncStateObserver = null;

        if (stateHandle is not null)
        {
            try
            {
                stateHandle.Cancel();
            }
            catch
            {
                // The native observer may already have stopped.
            }

            stateHandle.Dispose();
        }

        var syncService = _syncService;

        _syncService = null;
        _state = SyncServiceState.Offline;

        if (syncService is null)
        {
            return;
        }

        try
        {
            await syncService.Stop();
        }
        catch
        {
            // The native sync service may already have stopped or terminated.
        }

        DestroySyncService(syncService);
    }

    private async Task ResetAccountDirectoryAsync()
    {
        for (var attempt = 0;
             attempt <= DirectoryDeleteRetryCount;
             attempt++)
        {
            try
            {
                if (Directory.Exists(_accountPath))
                {
                    Directory.Delete(
                        _accountPath,
                        recursive: true);
                }

                EnsureAccountDirectoriesExist();

                return;
            }
            catch (IOException)
                when (attempt < DirectoryDeleteRetryCount)
            {
                await DelayDirectoryRetryAsync(attempt);
            }
            catch (UnauthorizedAccessException)
                when (attempt < DirectoryDeleteRetryCount)
            {
                await DelayDirectoryRetryAsync(attempt);
            }
        }
    }

    private static Task DelayDirectoryRetryAsync(
        int attempt)
    {
        return Task.Delay(
            TimeSpan.FromMilliseconds(
                100 * (attempt + 1)));
    }

    private static async Task<T> GetCachedValueAsync<T>(
        ConcurrentDictionary<string, Lazy<Task<T>>> cache,
        string key,
        Func<Task<T>> factory)
    {
        var lazy = cache.GetOrAdd(
            key,
            _ => new Lazy<Task<T>>(
                factory,
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value;
        }
        catch
        {
            cache.TryRemove(
                new KeyValuePair<string, Lazy<Task<T>>>(
                    key,
                    lazy));

            throw;
        }
    }

    private static void DestroyClient(
        Client client)
    {
        try
        {
            client.Dispose();
        }
        finally
        {
            client.Destroy();
        }
    }

    private static void DestroySyncService(
        SyncService syncService)
    {
        try
        {
            syncService.Dispose();
        }
        finally
        {
            syncService.Destroy();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _isDisposed,
            this);
    }

    /// <summary>
    /// Stops synchronization and releases all managed and native resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await _initializationLock.WaitAsync();

        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            await _connectionLock.WaitAsync();

            try
            {
                await NativeCleanupAsync();
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        finally
        {
            _initializationLock.Release();
        }

        _initializationLock.Dispose();
        _connectionLock.Dispose();
    }

    private enum SessionValidity
    {
        Valid,
        Invalid,
        Unknown,
    }

    private sealed class SyncStateObserver(
        Action<SyncServiceState> onUpdate)
        : uniffi.matrix_sdk_ffi.SyncServiceStateObserver
    {
        public void OnUpdate(
            SyncServiceState state)
        {
            onUpdate(state);
        }
    }
}
