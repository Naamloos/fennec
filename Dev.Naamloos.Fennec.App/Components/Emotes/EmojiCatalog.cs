using System.Globalization;
using System.Text;

namespace Dev.Naamloos.Fennec.App.Components;

public static class EmojiCatalog
{
    public static List<string> Create(IEnumerable<UnicodeRange> ranges)
    {
        var emotes = new List<string>();

        foreach (var range in ranges)
        {
            for (var codePoint = range.Start; codePoint <= range.End; codePoint++)
            {
                if (!Rune.TryCreate(codePoint, out var rune) ||
                    Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherNotAssigned)
                {
                    continue;
                }

                emotes.Add(rune.ToString());
            }
        }

        return emotes;
    }

    public sealed record UnicodeRange(int Start, int End);
}
