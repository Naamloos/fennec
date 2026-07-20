using CommunityToolkit.Mvvm.ComponentModel;

namespace Dev.Naamloos.Fennec.App.Models;

public sealed partial class MatrixDevice(
    string deviceId,
    string displayName,
    string? lastSeenIp,
    DateTimeOffset? lastSeen,
    bool isCurrent) : ObservableObject
{
    public string DeviceId { get; } = deviceId;
    public string? LastSeenIp { get; } = lastSeenIp;
    public DateTimeOffset? LastSeen { get; } = lastSeen;
    public bool IsCurrent { get; } = isCurrent;
    public string LastSeenText => LastSeen?.ToLocalTime().ToString("g") ?? "Unknown";

    [ObservableProperty]
    public partial string DisplayName { get; set; } = displayName;
}

public sealed record VerificationEmoji(string Symbol, string Description);
