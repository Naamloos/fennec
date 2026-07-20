using System.Net;
using System.Text.RegularExpressions;

namespace Dev.Naamloos.Fennec.App.Services;

public static partial class MessageFormatter
{
    public static string? MarkdownToHtml(string text)
    {
        var html = WebUtility.HtmlEncode(text);
        html = CodeMarkdown().Replace(html, "<code>$1</code>");
        html = BoldMarkdown().Replace(html, "<strong>$1</strong>");
        html = ItalicMarkdown().Replace(html, "<em>$1</em>");
        html = StrikeMarkdown().Replace(html, "<del>$1</del>");
        html = LinkMarkdown().Replace(html, "<a href=\"$2\">$1</a>");
        html = string.Join("<br>", html.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            if (HeadingMarkdown().Match(line) is { Success: true } heading)
            {
                var level = heading.Groups[1].Value.Length;
                return $"<h{level}>{heading.Groups[2].Value}</h{level}>";
            }
            if (line.StartsWith("&gt; ")) return $"<blockquote>{line[5..]}</blockquote>";
            if (line.StartsWith("- ")) return $"<ul><li>{line[2..]}</li></ul>";
            return line;
        }));
        return html == WebUtility.HtmlEncode(text) ? null : html;
    }

    public static FormattedString HtmlToFormattedString(string html)
    {
        var result = new FormattedString();
        var bold = 0;
        var italic = 0;
        var code = 0;
        var underline = 0;
        var strike = 0;
        var heading = 0;
        Uri? link = null;

        html = ReplyFallback().Replace(html, string.Empty);

        void AddBreak()
        {
            if (result.Spans.LastOrDefault()?.Text?.EndsWith(Environment.NewLine) == true) return;
            result.Spans.Add(new Span { Text = Environment.NewLine });
        }

        void AddText(string text)
        {
            if (text.Length == 0) return;
            var span = new Span
            {
                Text = text,
                FontAttributes = (bold > 0 ? FontAttributes.Bold : FontAttributes.None) |
                                 (italic > 0 ? FontAttributes.Italic : FontAttributes.None),
                FontFamily = code > 0 ? "monospace" : null,
                FontSize = heading > 0 ? Math.Max(18, 30 - heading * 2) : 14,
                TextDecorations = (underline > 0 ? TextDecorations.Underline : TextDecorations.None) |
                                  (strike > 0 ? TextDecorations.Strikethrough : TextDecorations.None)
            };
            if (link is not null)
                span.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () => await Launcher.Default.OpenAsync(link))
                });
            result.Spans.Add(span);
        }

        foreach (var part in HtmlTokens().Split(html).Where(part => part.Length > 0))
        {
            if (!part.StartsWith('<'))
            {
                AddText(WebUtility.HtmlDecode(part));
                continue;
            }

            var tagMatch = TagName().Match(part);
            if (!tagMatch.Success) continue;
            var tag = tagMatch.Groups[1].Value.ToLowerInvariant();
            var closing = part.StartsWith("</", StringComparison.Ordinal);
            switch (tag)
            {
                case "strong" or "b": bold = Math.Max(0, bold + (closing ? -1 : 1)); break;
                case "em" or "i": italic = Math.Max(0, italic + (closing ? -1 : 1)); break;
                case "code" or "pre": code = Math.Max(0, code + (closing ? -1 : 1)); if (tag == "pre") AddBreak(); break;
                case "u" or "ins": underline = Math.Max(0, underline + (closing ? -1 : 1)); break;
                case "del" or "s" or "strike": strike = Math.Max(0, strike + (closing ? -1 : 1)); break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    heading = closing ? 0 : int.Parse(tag[1..]);
                    if (closing) AddBreak();
                    break;
                case "br" or "hr": AddBreak(); break;
                case "p" or "div" or "blockquote" or "tr": if (closing) AddBreak(); break;
                case "li": if (!closing) AddText("• "); else AddBreak(); break;
                case "td" or "th": if (closing) AddText("  "); break;
                case "a":
                    if (closing) link = null;
                    else if (Href().Match(part) is { Success: true } href &&
                             Uri.TryCreate(WebUtility.HtmlDecode(href.Groups[1].Value), UriKind.Absolute, out var uri) &&
                             uri.Scheme is "http" or "https" or "matrix") link = uri;
                    break;
                case "img":
                    if (Alt().Match(part) is { Success: true } alt)
                        AddText(WebUtility.HtmlDecode(alt.Groups[1].Value));
                    break;
            }
        }
        return result;
    }

#if DEBUG
    public static void AssertFormatting()
    {
        System.Diagnostics.Debug.Assert(MarkdownToHtml("**bold**") == "<strong>bold</strong>");
        System.Diagnostics.Debug.Assert(HtmlToFormattedString("<strong>bold</strong>").Spans[0].FontAttributes.HasFlag(FontAttributes.Bold));
    }
#endif

    [GeneratedRegex("`(.+?)`", RegexOptions.Singleline)]
    private static partial Regex CodeMarkdown();

    [GeneratedRegex("\\*\\*(.+?)\\*\\*", RegexOptions.Singleline)]
    private static partial Regex BoldMarkdown();

    [GeneratedRegex("(?<!\\*)\\*(?!\\*)(.+?)(?<!\\*)\\*(?!\\*)", RegexOptions.Singleline)]
    private static partial Regex ItalicMarkdown();

    [GeneratedRegex("~~(.+?)~~", RegexOptions.Singleline)]
    private static partial Regex StrikeMarkdown();

    [GeneratedRegex("\\[([^\\]]+)\\]\\((https?://[^\\s)]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkMarkdown();

    [GeneratedRegex("^(#{1,6})\\s+(.+)$")]
    private static partial Regex HeadingMarkdown();

    [GeneratedRegex("(<[^>]*>)", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTokens();

    [GeneratedRegex("^</?\\s*([a-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TagName();

    [GeneratedRegex("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex("alt\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex Alt();

    [GeneratedRegex("<mx-reply\\b[^>]*>.*?</mx-reply>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReplyFallback();
}
