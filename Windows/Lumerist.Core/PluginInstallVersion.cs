using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumerist.Core;

/// <summary>
/// A <c>major.minor.patch</c> version, as in the top-level <c>VERSION</c> file, the
/// app's version, the plug-in's <c>Info.lua</c> VERSION table, and GitHub release
/// tags (<c>v0.9.0</c>, <c>v1.0.0-beta.2</c>).
/// </summary>
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>
    /// Text after <c>-</c> (e.g. "beta.2"); a version with one sorts before the
    /// same version without.
    /// </summary>
    public string? Prerelease { get; }

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    /// <summary>
    /// Accepts "0.9.0", "v0.9.0", "1.2", "1.0.0-beta.1" and ignores build
    /// metadata ("+42"). Returns false for anything else.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = FromText(text);
        return version is not null;
    }

    /// <summary>Like <see cref="TryParse"/>, returning null for anything unparsable.</summary>
    public static SemanticVersion? FromText(string? text)
    {
        if (text is null) return null;
        var core = text.Trim();
        if (core.StartsWith('v') || core.StartsWith('V')) core = core[1..];
        var plus = core.IndexOf('+');
        if (plus >= 0) core = core[..plus];
        string? prerelease = null;
        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = core[(dash + 1)..];
            core = core[..dash];
        }
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return null;
        var numbers = new List<int>(3);
        foreach (var part in parts)
        {
            if (part.Length == 0 || !part.All(char.IsAsciiDigit)) return null;
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return null;
            numbers.Add(value);
        }
        while (numbers.Count < 3) numbers.Add(0);
        return new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease);
    }

    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return Prerelease is null ? core : core + "-" + Prerelease;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        return (Prerelease, other.Prerelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (left, right) => CompareNumerically(left, right),
        };
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Compares runs of digits by value and everything else character by
    /// character, so "beta.10" sorts after "beta.9".
    /// </summary>
    internal static int CompareNumerically(string left, string right)
    {
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                int startI = i, startJ = j;
                while (i < left.Length && char.IsAsciiDigit(left[i])) i++;
                while (j < right.Length && char.IsAsciiDigit(right[j])) j++;
                var a = left[startI..i].TrimStart('0');
                var b = right[startJ..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var digits = string.CompareOrdinal(a, b);
                if (digits != 0) return Math.Sign(digits);
                continue;
            }
            if (left[i] != right[j]) return left[i].CompareTo(right[j]);
            i++;
            j++;
        }
        return (left.Length - i).CompareTo(right.Length - j);
    }

    /// <summary>The highest version among release tags (unparsable tags are skipped).</summary>
    public static (SemanticVersion Version, string Tag)? Newest(IEnumerable<string> tags)
    {
        (SemanticVersion Version, string Tag)? best = null;
        foreach (var tag in tags)
        {
            if (FromText(tag) is not { } version) continue;
            if (best is null || best.Value.Version < version) best = (version, tag);
        }
        return best;
    }

    /// <summary>
    /// The version in a Lightroom plug-in's <c>Info.lua</c>, read from its
    /// <c>VERSION = { major = …, minor = …, revision = … }</c> table (key order free).
    /// </summary>
    public static SemanticVersion? FromPluginInfo(string infoLua)
    {
        var start = infoLua.IndexOf("VERSION", StringComparison.Ordinal);
        if (start < 0) return null;
        var open = infoLua.IndexOf('{', start + "VERSION".Length);
        if (open < 0) return null;
        var close = infoLua.IndexOf('}', open);
        if (close < 0) return null;
        var table = infoLua[(open + 1)..close];

        int? Number(string key)
        {
            var match = Regex.Match(table, $@"\b{key}\s*=\s*([0-9]+)", RegexOptions.CultureInvariant);
            return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        if (Number("major") is not { } major || Number("minor") is not { } minor) return null;
        return new SemanticVersion(major, minor, Number("revision") ?? 0);
    }
}
