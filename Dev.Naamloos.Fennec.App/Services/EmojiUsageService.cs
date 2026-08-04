using System.Text.Json;
using System.Text.Json.Serialization;
using Dev.Naamloos.Fennec.Sdk;
using Dev.Naamloos.Fennec.Sdk.Helpers;

namespace Dev.Naamloos.Fennec.App.Services;

public sealed class EmojiUsageService
{
    private const string FavouritesKey = "fennec.emoji.favourites.v1";
    private const string AccountDataType = "m.recent_emoji";
    private readonly ManagedMatrixClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<RecentEmojiEntry> _recent = [];
    private readonly HashSet<string> _favourites;

    public EmojiUsageService(ManagedMatrixClient client)
    {
        _client = client;
        _favourites = Preferences.Default.Get(FavouritesKey, string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        _ = RefreshAsync();
    }

    public IReadOnlyList<string> RecentEmoji => _recent.Select(entry => entry.Emoji).ToArray();
    public IReadOnlySet<string> FavouriteIds => _favourites;

    public void Record(string? emoji)
    {
        if (!UnicodeEmoji.IsValid(emoji)) return;
        _ = RecordAsync(emoji!);
    }

    public void ToggleFavourite(string id)
    {
        if (!_favourites.Add(id)) _favourites.Remove(id);
        Preferences.Default.Set(FavouritesKey, string.Join('|', _favourites));
    }

    public async Task RefreshAsync()
    {
        if (!_client.IsLoggedIn) return;
        await _gate.WaitAsync();
        try
        {
            var content = await _client.GetAccountDataAsync(AccountDataType);
            _recent.Clear();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var state = JsonSerializer.Deserialize<RecentEmojiContent>(content);
                if (state?.Recent is not null)
                    _recent.AddRange(state.Recent.Where(entry => UnicodeEmoji.IsValid(entry.Emoji)).Take(100));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load recent emoji: {exception}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordAsync(string emoji)
    {
        await _gate.WaitAsync();
        try
        {
            var existing = _recent.FirstOrDefault(entry => entry.Emoji == emoji);
            if (existing is not null) _recent.Remove(existing);
            _recent.Insert(0, new RecentEmojiEntry(emoji, (existing?.Total ?? 0) + 1));
            if (_recent.Count > 100) _recent.RemoveRange(100, _recent.Count - 100);
            await _client.SetAccountDataAsync(
                AccountDataType,
                JsonSerializer.Serialize(new RecentEmojiContent(_recent.ToArray()))
            );
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Could not save recent emoji: {exception}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record RecentEmojiEntry(
        [property: JsonPropertyName("emoji")] string Emoji,
        [property: JsonPropertyName("total")] ulong Total
    );

    private sealed record RecentEmojiContent(
        [property: JsonPropertyName("recent_emoji")] RecentEmojiEntry[] Recent
    );
}
