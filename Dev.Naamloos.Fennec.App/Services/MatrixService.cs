using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dev.Naamloos.Fennec.App.Models;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.App.Services
{
    public sealed class MatrixService : IAsyncDisposable
    {
        private const string SessionStorageKey =
            "matrix.session";

        private const string StoreKeyStorageKey =
            "matrix.store-key";

        private readonly SemaphoreSlim _initializationLock =
            new(1, 1);
        private static readonly HttpClient Http = new();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<byte[]>>> _thumbnailCache = [];

        public Client? Client { get; private set; }

        public SyncService? SyncService { get; private set; }

        public bool IsLoggedIn =>
            Client?.UserId() is not null;

        public async Task<bool> TryRestoreSessionAsync()
        {
            await _initializationLock.WaitAsync();

            try
            {
                if (IsLoggedIn)
                    return true;

                var serializedSession =
                    await SecureStorage.Default.GetAsync(
                        SessionStorageKey);

                if (string.IsNullOrWhiteSpace(serializedSession))
                    return false;

                var session =
                    JsonSerializer.Deserialize<Session>(
                        serializedSession);

                if (session is null)
                    return false;

                Client = await BuildClientAsync(
                    session.UserId);

                await Client.RestoreSession(session);

                await StartSyncAsync();

                return true;
            }
            catch
            {
                await DisposeNativeObjectsAsync();
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task LoginAsync(
            string homeserver,
            string username,
            string password)
        {
            await _initializationLock.WaitAsync();

            try
            {
                await DisposeNativeObjectsAsync();

                Client = await BuildClientAsync(
                    username,
                    homeserver);

                await Client.Login(
                    username: username,
                    password: password,
                    initialDeviceName: $"Fennec ({DeviceInfo.Current.Platform})",
                    deviceId: null);

                var session = Client.Session();

                await SecureStorage.Default.SetAsync(
                    SessionStorageKey,
                    JsonSerializer.Serialize(session));

                await StartSyncAsync();
            }
            catch
            {
                await DisposeNativeObjectsAsync();
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task<Client> BuildClientAsync(
            string userId,
            string? homeserver = null)
        {
            var accountDirectory =
                GetAccountDirectory(userId);

            var dataPath = Path.Combine(
                accountDirectory,
                "data");

            var cachePath = Path.Combine(
                accountDirectory,
                "cache");

            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(cachePath);

            var storeKey =
                await GetOrCreateStoreKeyAsync();

            var storeBuilder =
                new SqliteStoreBuilder(
                    dataPath,
                    cachePath)
                    .Key(storeKey);

            var clientBuilder =
                new ClientBuilder()
                    .Username(userId)
                    .SqliteStore(storeBuilder)
                    .SlidingSyncVersionBuilder(
                        SlidingSyncVersionBuilder
                            .DiscoverNative);

            if (!string.IsNullOrWhiteSpace(homeserver))
            {
                clientBuilder =
                    clientBuilder.HomeserverUrl(
                        homeserver);
            }

            return await clientBuilder.Build();
        }

        private async Task StartSyncAsync()
        {
            if (Client is null)
            {
                throw new InvalidOperationException(
                    "The Matrix client is unavailable.");
            }

            if (SyncService is not null)
                return;

            var builder = Client.SyncService();

            SyncService = await builder.Finish();

            await SyncService.Start();
        }

        public async Task LogoutAsync()
        {
            await _initializationLock.WaitAsync();

            try
            {
                if (Client is not null)
                {
                    try
                    {
                        await Client.Logout();
                    }
                    catch
                    {
                        // Clear the local session even when the server logout
                        // request fails.
                    }
                }

                await ClearSavedSessionAsync();
                await DisposeNativeObjectsAsync();
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task ClearSavedSessionAsync()
        {
            SecureStorage.Default.Remove(
                SessionStorageKey);

            await Task.CompletedTask;
        }

        private static string GetAccountDirectory(
            string userId)
        {
            var safeUserId = Convert.ToHexString(
                    SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            userId)))
                .ToLowerInvariant();

            return Path.Combine(
                FileSystem.AppDataDirectory,
                "fennec-store",
                safeUserId);
        }

        private static async Task<byte[]>
            GetOrCreateStoreKeyAsync()
        {
            var encodedKey =
                await SecureStorage.Default.GetAsync(
                    StoreKeyStorageKey);

            if (encodedKey is not null)
            {
                var existingKey =
                    Convert.FromBase64String(encodedKey);

                if (existingKey.Length != 32)
                {
                    throw new InvalidDataException(
                        "The Matrix store key is invalid.");
                }

                return existingKey;
            }

            var key =
                RandomNumberGenerator.GetBytes(32);

            await SecureStorage.Default.SetAsync(
                StoreKeyStorageKey,
                Convert.ToBase64String(key));

            return key;
        }

        private async Task DisposeNativeObjectsAsync()
        {
            if (SyncService is not null)
            {
                try
                {
                    await SyncService.Stop();
                }
                catch
                {
                    // Ignore shutdown failures.
                }

                SyncService.Dispose();
                SyncService = null;
            }

            Client?.Dispose();
            Client = null;
        }

        public Task<byte[]> GetThumbnailAsync(string url, ulong width, ulong height) =>
            _thumbnailCache.GetOrAdd($"{width}x{height}:{url}", _ =>
                new(() => DownloadThumbnailAsync(url, width, height))).Value;

        public Task<byte[]> GetMediaThumbnailAsync(string sourceJson, ulong width, ulong height) =>
            _thumbnailCache.GetOrAdd($"media:{width}x{height}:{sourceJson}", _ =>
                new(() => DownloadMediaThumbnailAsync(sourceJson, width, height))).Value;

        public async Task<byte[]> GetMediaContentAsync(string sourceJson)
        {
            var client = Client ?? throw new InvalidOperationException("Matrix client is not initialized.");
            using var source = MediaSource.FromJson(sourceJson);
            return await client.GetMediaContent(source);
        }

        public async Task<IReadOnlyList<MatrixDevice>> GetDevicesAsync()
        {
            var session = Client?.Session() ?? throw new InvalidOperationException("Matrix client is not initialized.");
            using var response = await SendClientRequestAsync(HttpMethod.Get, "/_matrix/client/v3/devices");
            var result = JsonSerializer.Deserialize<DevicesResponse>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new([]);
            return result.Devices.Select(device => new MatrixDevice(
                device.DeviceId,
                device.DisplayName ?? device.DeviceId,
                device.LastSeenIp,
                device.LastSeenTimestamp is { } timestamp
                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                    : null,
                device.DeviceId == session.DeviceId)).ToArray();
        }

        public async Task UpdateDeviceNameAsync(string deviceId, string displayName)
        {
            using var content = JsonContent.Create(new { display_name = displayName });
            using var response = await SendClientRequestAsync(
                HttpMethod.Put,
                $"/_matrix/client/v3/devices/{Uri.EscapeDataString(deviceId)}",
                content);
        }

        public async Task SetProfileAvatarAsync(byte[] data, string mimeType)
        {
            var client = Client ?? throw new InvalidOperationException("Matrix client is not initialized.");
            var mxcUrl = await client.UploadMedia(mimeType, data, null);
            await client.SetAvatarUrl(mxcUrl);
        }

        public async Task<IReadOnlyList<MatrixImageAsset>> GetRoomImagePackAsync(string roomId)
        {
            try
            {
                using var response = await SendClientRequestAsync(
                    HttpMethod.Get,
                    $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/state/im.ponies.room_emotes");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!document.RootElement.TryGetProperty("images", out var images)) return [];
                var result = new List<MatrixImageAsset>();
                foreach (var image in images.EnumerateObject())
                {
                    var content = image.Value;
                    if (!content.TryGetProperty("url", out var urlProperty)) continue;
                    var body = content.TryGetProperty("body", out var bodyProperty)
                        ? bodyProperty.GetString() ?? image.Name
                        : image.Name;
                    var usages = content.TryGetProperty("usage", out var usageProperty)
                        ? usageProperty.EnumerateArray().Select(value => value.GetString()).ToHashSet()
                        : new HashSet<string?> { "emoticon", "sticker" };
                    result.Add(new MatrixImageAsset(
                        image.Name,
                        body,
                        urlProperty.GetString()!,
                        usages.Contains("sticker"),
                        usages.Contains("emoticon")));
                }
                return result;
            }
            catch
            {
                return [];
            }
        }

        private async Task<HttpResponseMessage> SendClientRequestAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null)
        {
            var session = Client?.Session() ?? throw new InvalidOperationException("Matrix client is not initialized.");
            using var request = new HttpRequestMessage(
                method,
                $"{session.HomeserverUrl.TrimEnd('/')}{path}")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var detail = await response.Content.ReadAsStringAsync();
                response.Dispose();
                throw new InvalidOperationException($"Matrix request failed ({status}): {detail}");
            }
            return response;
        }

        private sealed record DevicesResponse(
            [property: JsonPropertyName("devices")] DeviceResponse[] Devices);

        private sealed record DeviceResponse(
            [property: JsonPropertyName("device_id")] string DeviceId,
            [property: JsonPropertyName("display_name")] string? DisplayName,
            [property: JsonPropertyName("last_seen_ip")] string? LastSeenIp,
            [property: JsonPropertyName("last_seen_ts")] long? LastSeenTimestamp);

        private async Task<byte[]> DownloadThumbnailAsync(string url, ulong width, ulong height)
        {
            var client = Client ?? throw new InvalidOperationException("Matrix client is not initialized.");
            using var source = MediaSource.FromUrl(url);
            return await client.GetMediaThumbnail(source, width, height);
        }

        private async Task<byte[]> DownloadMediaThumbnailAsync(string sourceJson, ulong width, ulong height)
        {
            var client = Client ?? throw new InvalidOperationException("Matrix client is not initialized.");
            using var source = MediaSource.FromJson(sourceJson);
            return await client.GetMediaThumbnail(source, width, height);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeNativeObjectsAsync();

            _initializationLock.Dispose();
        }
    }

}
