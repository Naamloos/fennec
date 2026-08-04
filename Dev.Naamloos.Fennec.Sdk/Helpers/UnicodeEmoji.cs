using System.Reflection;

namespace Dev.Naamloos.Fennec.Sdk.Helpers;

/// <summary>Small trust-boundary validator for Matrix reaction keys.</summary>
public static class UnicodeEmoji
{
    private static readonly Lazy<HashSet<string>> ValidSequences = new(LoadValidSequences);

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && ValidSequences.Value.Contains(value);

    private static HashSet<string> LoadValidSequences()
    {
        using var stream =
            Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("Dev.Naamloos.Fennec.Sdk.emoji-test.txt")
            ?? throw new InvalidOperationException("Bundled Unicode emoji data is missing.");
        using var reader = new StreamReader(stream);
        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (!line.Contains("; fully-qualified", StringComparison.Ordinal))
                continue;
            var semicolon = line.IndexOf(';');
            if (semicolon > 0)
                result.Add(
                    string.Concat(
                        line[..semicolon]
                            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(hex => char.ConvertFromUtf32(Convert.ToInt32(hex, 16)))
                    )
                );
        }
        return result;
    }
}
