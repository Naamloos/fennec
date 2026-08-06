using System.Globalization;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    GatewayPayload.SelfTest();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
);
builder.Services.AddMemoryCache(options => options.SizeLimit = 10_000);

FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.GetApplicationDefault() });
builder.Services.AddSingleton(FirebaseMessaging.DefaultInstance);

var app = builder.Build();
var sendGate = new SemaphoreSlim(1, 1);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost(
    "/_matrix/push/v1/notify",
    async Task<IResult> (
        NotifyRequest request,
        FirebaseMessaging firebase,
        IMemoryCache recentEvents,
        CancellationToken cancellationToken
    ) =>
    {
        if (request.Notification?.Devices is not { Count: > 0 } devices)
        {
            return Results.BadRequest(new { error = "notification.devices is required" });
        }

        if (devices.Count > 500)
        {
            return Results.BadRequest(new { error = "notification.devices cannot exceed 500" });
        }

        if (
            devices.Any(device =>
                string.IsNullOrWhiteSpace(device.AppId) || string.IsNullOrWhiteSpace(device.Pushkey)
            )
        )
        {
            return Results.BadRequest(new { error = "Every device requires app_id and pushkey" });
        }

        var notification = request.Notification;
        var tokens = devices.Select(device => device.Pushkey!).Distinct().ToArray();

        // ponytail: one process-wide gate; use a shared idempotency store before adding replicas.
        await sendGate.WaitAsync(cancellationToken);
        try
        {
            if (
                notification.EventId is { Length: > 0 } eventId
                && recentEvents.TryGetValue<string[]>(eventId, out var previousRejections)
            )
            {
                return Results.Ok(new GatewayResponse(previousRejections ?? []));
            }

            var hasEvent = notification.EventId is { Length: > 0 };
            var unread = notification.Counts?.Unread is { } unreadCount
                ? Math.Max(0, unreadCount)
                : (int?)null;
            var (title, body) = GatewayPayload.CreatePresentation(notification);
            var requiresClientResolution = notification.Type == "m.room.encrypted";

#pragma warning disable CS0618 // Matrix pushers currently provide FCM registration tokens.
            var message = new MulticastMessage
            {
                Tokens = tokens,
                Data = GatewayPayload.CreateData(notification),
                Notification = hasEvent && !requiresClientResolution
                    ? new Notification { Title = title, Body = body }
                    : null,
                Android = new AndroidConfig
                {
                    Priority = notification.Prio == "low" ? Priority.Normal : Priority.High,
                    Notification = hasEvent && !requiresClientResolution
                        ? new AndroidNotification
                        {
                            ChannelId = "fennec_messages",
                            Tag = notification.EventId,
                            EventTimestamp = DateTime.UtcNow,
                            NotificationCount = unread,
                            DefaultSound = true,
                        }
                        : null,
                },
                Apns = new ApnsConfig
                {
                    Aps = new Aps
                    {
                        Badge = unread,
                        Sound = hasEvent && !requiresClientResolution ? "default" : null,
                        ThreadId = notification.RoomId,
                        ContentAvailable = !hasEvent || requiresClientResolution,
                    },
                },
            };
#pragma warning restore CS0618

            var response = await firebase.SendEachForMulticastAsync(message, cancellationToken);
            var rejected = response
                .Responses.Select((result, index) => (result, index))
                .Where(item =>
                    !item.result.IsSuccess
                    && item.result.Exception?.MessagingErrorCode
                        is MessagingErrorCode.Unregistered
                            or MessagingErrorCode.InvalidArgument
                )
                .Select(item => tokens[item.index])
                .ToArray();

            if (
                response.Responses.Any(result =>
                    !result.IsSuccess
                    && result.Exception?.MessagingErrorCode
                        is not (
                            MessagingErrorCode.Unregistered
                            or MessagingErrorCode.InvalidArgument
                        )
                )
            )
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            if (notification.EventId is { Length: > 0 } deliveredEventId)
            {
                recentEvents.Set(
                    deliveredEventId,
                    rejected,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
                        Size = 1,
                    }
                );
            }

            return Results.Ok(new GatewayResponse(rejected));
        }
        finally
        {
            sendGate.Release();
        }
    }
);

app.Run();

static class GatewayPayload
{
    public static (string Title, string Body) CreatePresentation(MatrixNotification notification) =>
        (
            string.IsNullOrWhiteSpace(notification.RoomName)
                ? notification.SenderDisplayName ?? "Fennec"
                : notification.RoomName,
            notification.Content is not { ValueKind: JsonValueKind.Object } content
            || !content.TryGetProperty("body", out var body)
            || body.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(body.GetString())
                ? "New Matrix message"
                : body.GetString()!
        );

    public static IReadOnlyDictionary<string, string> CreateData(MatrixNotification notification)
    {
        var data = new Dictionary<string, string> { ["is_silent_in_foreground"] = "true" };

        if (notification.Counts?.Unread is { } unread)
        {
            data["unread"] = Math.Max(0, unread).ToString(CultureInfo.InvariantCulture);
        }

        if (notification.EventId is { Length: > 0 })
        {
            data["event_id"] = notification.EventId;
        }

        if (notification.RoomId is { Length: > 0 })
        {
            data["room_id"] = notification.RoomId;
        }

        if (notification.Type == "m.room.encrypted")
        {
            var (title, body) = CreatePresentation(notification);
            data["resolve_encrypted"] = "true";
            data["title"] = title;
            data["body"] = body;
        }

        return data;
    }

    public static void SelfTest()
    {
        var request = JsonSerializer.Deserialize<NotifyRequest>(
            """
            {"notification":{"event_id":"$event","room_id":"!room:example.org","room_name":"Mission Control","type":"m.room.encrypted","content":{"body":"Ground control to Major Tom"},"counts":{"unread":-1},"devices":[{"app_id":"dev.naamloos.fennec","pushkey":"token"}]}}
            """,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        var data = CreateData(request?.Notification ?? throw new InvalidOperationException());
        var presentation = CreatePresentation(request.Notification);

        if (
            data["event_id"] != "$event"
            || data["room_id"] != "!room:example.org"
            || data["unread"] != "0"
            || data["is_silent_in_foreground"] != "true"
            || data["resolve_encrypted"] != "true"
            || presentation != ("Mission Control", "Ground control to Major Tom")
            || request.Notification.Devices?[0].Pushkey != "token"
        )
        {
            throw new InvalidOperationException("Gateway payload self-test failed.");
        }

        Console.WriteLine("Gateway payload self-test passed.");
    }
}

sealed class NotifyRequest
{
    public MatrixNotification? Notification { get; init; }
}

sealed class MatrixNotification
{
    public JsonElement? Content { get; init; }
    public string? EventId { get; init; }
    public string? RoomId { get; init; }
    public string? RoomName { get; init; }
    public string? SenderDisplayName { get; init; }
    public string? Type { get; init; }
    public string? Prio { get; init; }
    public MatrixCounts? Counts { get; init; }
    public IReadOnlyList<MatrixDevice>? Devices { get; init; }
}

sealed class MatrixCounts
{
    public int? Unread { get; init; }
}

sealed class MatrixDevice
{
    public string? AppId { get; init; }
    public string? Pushkey { get; init; }
}

sealed record GatewayResponse(IReadOnlyList<string> Rejected);
