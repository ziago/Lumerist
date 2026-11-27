using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lumerist.Core;

/// <summary>
/// The "What's new" lines of the update window, from a release's notes (the
/// Mac's UpdateHighlights; shared cases: Shared/fixtures/updates). Release
/// text is never rendered: no links, Markdown or HTML, only short plain lines.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The section starts after the first line that reads "### Highlights"
/// (any case, spaces around ignored) and ends at the next line starting with
/// "#" (after leading spaces), the next heading.</item>
/// <item>A line starting with "- " or "* " (after leading spaces) starts a
/// bullet; a following line indented by spaces or tabs continues it (joined
/// with one space); a blank line or a line of plain, unindented text ends it.
/// Lines end with LF or CRLF.</item>
/// <item>Plain text: links become their text, code spans their content, bold
/// and italics lose their markers; control characters become spaces,
/// bidirectional controls go; runs of whitespace become one space.</item>
/// <item>A bullet longer than <see cref="MaxLength"/> Unicode scalars is cut,
/// at a space when there is one, and ends with "…". At most
/// <see cref="MaxCount"/> bullets and <see cref="MaxTotal"/> scalars in all
/// (the bullet that would pass it and those after it are left out).</item>
/// </list>
/// </remarks>
public static partial class ReleaseHighlights
{
    public const int MaxCount = 5;
    public const int MaxLength = 140;
    public const int MaxTotal = 600;

    /// <summary>Shown instead when the notes have no highlights.</summary>
    public const string Fallback = "See what's new on the release page.";

    public static IReadOnlyList<string> Parse(string? body)
    {
        if (string.IsNullOrEmpty(body)) return [];
        var bullets = new List<string>();
        string? current = null;
        var inSection = false;
        void Close()
        {
            if (current is not null) bullets.Add(current);
            current = null;
        }
        foreach (var row in body.Split('\n'))
        {
            var line = row.EndsWith('\r') ? row[..^1] : row;
            if (!inSection)
            {
                if (TrimHorizontal(line).ToLowerInvariant() == "### highlights") inSection = true;
                continue;
            }
            var stripped = line.TrimStart(' ', '\t');
            if (stripped.StartsWith('#')) break;
            if (stripped.StartsWith("- ", StringComparison.Ordinal) || stripped.StartsWith("* ", StringComparison.Ordinal))
            {
                Close();
                current = stripped[2..];
            }
            else if (TrimHorizontal(stripped).Length == 0)
            {
                Close();
            }
            else if (current is not null && line.Length > 0 && line[0] is ' ' or '\t')
            {
                current = current + " " + stripped;
            }
            else
            {
                Close();
            }
        }
        Close();

        var result = new List<string>();
        var total = 0;
        foreach (var bullet in bullets)
        {
            var text = Cut(PlainText(bullet));
            if (text.Length == 0) continue;
            var length = Scalars(text);
            if (result.Count >= MaxCount || total + length > MaxTotal) break;
            result.Add(text);
            total += length;
        }
        return result;
    }

    /// <summary>One bullet's Markdown as plain text (see the remarks).</summary>
    public static string PlainText(string markdown)
    {
        var cleaned = new StringBuilder(markdown.Length);
        foreach (var rune in markdown.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is <= 0x1F or (>= 0x7F and <= 0x9F) or 0x2028 or 0x2029) cleaned.Append(' ');
            else if (value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069) or 0xFEFF) continue;
            else cleaned.Append(rune.ToString());
        }
        var text = Link().Replace(cleaned.ToString(), "$1");
        // Code spans keep their content as is; Markdown is stripped around them.
        var plain = new StringBuilder();
        var rest = 0;
        foreach (Match match in Code().Matches(text))
        {
            plain.Append(StripEmphasis(text[rest..match.Index]));
            plain.Append(match.Groups[1].Value);
            rest = match.Index + match.Length;
        }
        plain.Append(StripEmphasis(text[rest..]));
        return string.Join(' ', plain.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string StripEmphasis(string text)
    {
        foreach (var pattern in new[] { StrongStars(), StrongUnderscores(), EmphasisStar(), EmphasisUnderscore() })
            text = pattern.Replace(text, "$1");
        return text;
    }

    /// <summary>
    /// At most <see cref="MaxLength"/> scalars: cut after one fewer, back to
    /// the last space when the cut falls inside a word, then "…".
    /// </summary>
    internal static string Cut(string text)
    {
        var scalars = text.EnumerateRunes().ToList();
        if (scalars.Count <= MaxLength) return text;
        var kept = scalars.GetRange(0, MaxLength - 1);
        var space = new Rune(' ');
        if (scalars[MaxLength - 1] != space && kept.LastIndexOf(space) is var last and >= 0) kept = kept.GetRange(0, last);
        while (kept.Count > 0 && kept[^1] == space) kept.RemoveAt(kept.Count - 1);
        return string.Concat(kept.Select(rune => rune.ToString())) + "…";
    }

    /// <summary>The length as both apps count it: Unicode scalars.</summary>
    internal static int Scalars(string text) => text.EnumerateRunes().Count();

    /// <summary>Spaces (Unicode Zs) and tabs at both ends, as Swift's CharacterSet.whitespaces.</summary>
    private static string TrimHorizontal(string text)
    {
        static bool IsHorizontal(char c) => c == '\t' || char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
        var start = 0;
        var end = text.Length;
        while (start < end && IsHorizontal(text[start])) start++;
        while (end > start && IsHorizontal(text[end - 1])) end--;
        return text[start..end];
    }

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex Link();

    [GeneratedRegex(@"`([^`]*)`", RegexOptions.CultureInvariant)]
    private static partial Regex Code();

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex StrongStars();

    [GeneratedRegex(@"(?<![A-Za-z0-9])__(?=\S)(.+?)(?<=\S)__(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex StrongUnderscores();

    [GeneratedRegex(@"\*(?=\S)(.+?)(?<=\S)\*", RegexOptions.CultureInvariant)]
    private static partial Regex EmphasisStar();

    [GeneratedRegex(@"(?<![A-Za-z0-9])_(?=\S)(.+?)(?<=\S)_(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex EmphasisUnderscore();
}
