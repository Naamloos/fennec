using System.Text.Json;
using Dev.Naamloos.Fennec.App.Components;

namespace Dev.Naamloos.Fennec.App.Services;

/// <summary>Small local index; catalog metadata stays out of user settings.</summary>
public sealed class EmojiUsageService
{
    private const string StorageKey = "fennec.emoji.usage.v1";
    private EmojiUsageState _state;

    public EmojiUsageService()
    {
        try
        {
            _state = JsonSerializer.Deserialize<EmojiUsageState>(Preferences.Default.Get(StorageKey, "")) ?? new();
        }
        catch (JsonException)
        {
            _state = new();
        }
    }

    public IReadOnlyList<string> RecentIds(EmojiPickerMode mode) =>
        (mode == EmojiPickerMode.Reaction ? _state.Reaction : _state.Composer)
            .OrderByDescending(entry => entry.LastUsed)
            .Select(entry => entry.Id)
            .ToArray();

    public IReadOnlySet<string> FavouriteIds => _state.Favourites.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

    public void Record(EmojiPickerMode mode, string id)
    {
        var entries = mode == EmojiPickerMode.Reaction ? _state.Reaction : _state.Composer;
        var existing = entries.FirstOrDefault(entry => entry.Id == id);
        if (existing is not null)
            entries.Remove(existing);
        entries.Insert(0, new EmojiUsageEntry(id, DateTimeOffset.UtcNow, (existing?.UseCount ?? 0) + 1));
        if (entries.Count > 48)
            entries.RemoveRange(48, entries.Count - 48);
        Save();
    }

    public void ToggleFavourite(string id)
    {
        var existing = _state.Favourites.FirstOrDefault(entry => entry.Id == id);
        if (existing is null)
            _state.Favourites.Add(new EmojiUsageEntry(id, DateTimeOffset.UtcNow, 0));
        else
            _state.Favourites.Remove(existing);
        Save();
    }

    private void Save() => Preferences.Default.Set(StorageKey, JsonSerializer.Serialize(_state));

    private sealed record EmojiUsageEntry(string Id, DateTimeOffset LastUsed, int UseCount);
    private sealed class EmojiUsageState
    {
        public List<EmojiUsageEntry> Reaction { get; init; } = [];
        public List<EmojiUsageEntry> Composer { get; init; } = [];
        public List<EmojiUsageEntry> Favourites { get; init; } = [];
    }
}
