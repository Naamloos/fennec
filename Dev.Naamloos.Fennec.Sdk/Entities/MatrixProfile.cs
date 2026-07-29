namespace Dev.Naamloos.Fennec.Sdk.Entities;

public sealed record MatrixProfile(
    string UserId,
    string DisplayName,
    string? AvatarUrl,
    string? Bio,
    string? Status,
    string? Presence,
    string? TimeZone,
    IReadOnlyList<string> Pronouns,
    string Homeserver
);
