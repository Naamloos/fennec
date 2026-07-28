namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed record MatrixSession(
    string DeviceId,
    string DisplayName,
    DateTimeOffset? LastSeen,
    string? LastSeenIp,
    bool IsCurrent,
    bool IsVerified);
