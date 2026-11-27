using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lumerist.Core;

/// <summary>
/// Where an update may be downloaded from (the Mac's UpdateURLRule; shared
/// cases: Shared/fixtures/updates). Every request, and every redirect it
/// leads to, is checked here before it is sent.
/// </summary>
public static class UpdateDownloadRules
{
    /// <summary>GitHub's API and site, and the two hosts it serves release files from.</summary>
    public static IReadOnlyList<string> TrustedHosts { get; } =
        ["api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    /// <summary>Every file of a release is served from here, under its tag.</summary>
    public const string DownloadPrefix = "https://github.com/ziago/Lumerist/releases/download/";

    /// <summary>Redirects followed per download (GitHub uses one).</summary>
    public const int MaxRedirects = 5;

    /// <summary>
    /// A step of a download, the first address or a redirect: https to one of
    /// <see cref="TrustedHosts"/> (any case), on the default port, with no
    /// user name or password and no "%" in the host. A local file (file:///…,
    /// no host) only when <paramref name="allowLocalFiles"/> (debug builds'
    /// test feed); a redirect never reaches one (<see cref="UpdateDownloader"/>).
    /// </summary>
    public static bool IsTrustedHop(Uri url, bool allowLocalFiles = false)
    {
        if (!url.IsAbsoluteUri) return false;
        if (url.Scheme == Uri.UriSchemeFile) return allowLocalFiles && IsLocalFile(url);
        return url.Scheme == Uri.UriSchemeHttps
            && !Authority(url.OriginalString).Contains('%')
            && url.IsDefaultPort
            && url.UserInfo.Length == 0
            && TrustedHosts.Contains(url.IdnHost, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary><see cref="IsTrustedHop(Uri, bool)"/> for an address as written; one that doesn't parse is never trusted.</summary>
    public static bool IsTrustedHop(string text, bool allowLocalFiles = false) =>
        Uri.TryCreate(text, UriKind.Absolute, out var url) && text.StartsWith(url.Scheme + ":", StringComparison.OrdinalIgnoreCase)
        && IsTrustedHop(url, allowLocalFiles);

    /// <summary>
    /// A file's <c>browser_download_url</c>, as text: exactly
    /// <c>https://github.com/ziago/Lumerist/releases/download/&lt;tag&gt;/&lt;name&gt;</c>
    /// for the release's own tag and the file's own name. With
    /// <paramref name="allowLocalFiles"/>, a local file:// address as well.
    /// </summary>
    public static bool IsTrustedAssetUrl(string text, string tag, string name, bool allowLocalFiles = false)
    {
        if (text == DownloadPrefix + tag + "/" + name) return true;
        return allowLocalFiles && text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(text, UriKind.Absolute, out var url) && IsLocalFile(url);
    }

    /// <summary>file:///C:/… : no host (so not a share), no user.</summary>
    internal static bool IsLocalFile(Uri url) =>
        url.IsAbsoluteUri && url.IsFile && !url.IsUnc && url.Host.Length == 0 && url.UserInfo.Length == 0;

    /// <summary>The text between "//" and the path, as written (before any decoding).</summary>
    private static string Authority(string text)
    {
        var start = text.IndexOf("//", StringComparison.Ordinal);
        if (start < 0) return "";
        start += 2;
        var end = text.IndexOfAny(['/', '?', '#'], start);
        return end < 0 ? text[start..] : text[start..end];
    }
}

/// <summary>One file "Update now" downloads.</summary>
/// <param name="Size">The size the feed declares: the download must have exactly this many bytes.</param>
/// <param name="Sha256">GitHub's digest as lower-case hex, when it is a SHA-256.</param>
public sealed record UpdateFile(string Name, Uri Url, long Size, string? Sha256);

/// <summary>What "Update now" downloads: the installer, and the signed list of checksums that vouches for it.</summary>
/// <param name="Version">The release's version, e.g. "0.9.2".</param>
/// <param name="Checksum">The installer's own .sha256 file, when the release has one: it must agree too.</param>
public sealed record UpdatePackage(string Version, UpdateFile Installer, UpdateFile ChecksumList, UpdateFile Signature, UpdateFile? Checksum);

/// <summary>Why a release can't be installed by "Update now" (the release page still can).</summary>
public enum UpdateRefusal
{
    /// <summary>Not strictly newer than this copy, or this copy's version is unknown (a development build).</summary>
    NotNewer,
    /// <summary>The tag is not <c>v</c> plus a semantic version.</summary>
    BadTag,
    /// <summary>Not an x64 or arm64 PC.</summary>
    UnsupportedArchitecture,
    /// <summary>The installer, SHA256SUMS or SHA256SUMS.sig is missing.</summary>
    MissingFile,
    /// <summary>A needed file is listed twice, not at its GitHub address, empty or too large, or with an odd digest.</summary>
    BadFile,
}

/// <summary>Either the files to download, or why there are none.</summary>
public abstract record UpdateChoice
{
    private UpdateChoice() { }

    public sealed record Ready(UpdatePackage Package) : UpdateChoice;

    public sealed record Refused(UpdateRefusal Reason) : UpdateChoice;
}

/// <summary>Which files of a release "Update now" uses (the Mac's UpdateChoice.assets).</summary>
public static class UpdateAssets
{
    public const string ChecksumListName = "SHA256SUMS";
    public const string SignatureName = "SHA256SUMS.sig";

    /// <summary>The largest file accepted for each role, in bytes.</summary>
    public static class Limit
    {
        public const long Installer = 300L * 1024 * 1024;
        public const long Checksum = 1024;
        public const long ChecksumList = 64 * 1024;
        public const long Signature = 1024;
    }

    /// <summary>"x64" or "arm64" for the PC's processor (not the app's), null for any other.</summary>
    public static string? ArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        _ => null,
    };

    /// <summary>The installer's name in a release, as Scripts/build-windows-installer.ps1 names it.</summary>
    public static string InstallerName(string version, string architecture) => $"Lumerist-{version}-windows-{architecture}-setup.exe";

    /// <summary>
    /// The installer of a system ("mac", "windows-x64", "windows-arm64", as
    /// the shared cases name them): <c>Lumerist-&lt;v&gt;.dmg</c> or
    /// <c>Lumerist-&lt;v&gt;-windows-&lt;arch&gt;-setup.exe</c>; null for another.
    /// </summary>
    public static string? PlatformInstallerName(string platform, string version) => platform switch
    {
        "mac" => $"Lumerist-{version}.dmg",
        "windows-x64" => InstallerName(version, "x64"),
        "windows-arm64" => InstallerName(version, "arm64"),
        _ => null,
    };

    /// <summary>
    /// The files to download for this PC's <paramref name="architecture"/>,
    /// only for a version strictly newer than <paramref name="runningVersion"/>
    /// (see <see cref="Files"/>).
    /// </summary>
    /// <param name="allowLocalFiles">Debug builds' test feed: file:// files are accepted too.</param>
    public static UpdateChoice Choose(UpdateRelease release, string runningVersion, string? architecture, bool allowLocalFiles = false)
    {
        if (UpdateCheck.TagVersion(release.TagName) is not { } version) return new UpdateChoice.Refused(UpdateRefusal.BadTag);
        if (UpdateCheck.CurrentVersion(runningVersion) is not { } running || running >= version)
            return new UpdateChoice.Refused(UpdateRefusal.NotNewer);
        if (architecture is not ("x64" or "arm64")) return new UpdateChoice.Refused(UpdateRefusal.UnsupportedArchitecture);
        return Files(release, version, InstallerName(version.ToString(), architecture), allowLocalFiles);
    }

    /// <summary>
    /// The installer named <paramref name="installerName"/>, its <c>.sha256</c>
    /// when the release has one, <c>SHA256SUMS</c> and <c>SHA256SUMS.sig</c>,
    /// each found by its exact name in the release's own list of files (never
    /// built from its text). Refused when the installer, the list or its
    /// signature is missing, or when any of them (the .sha256 only when
    /// present) is listed twice, is not at
    /// <see cref="UpdateDownloadRules.IsTrustedAssetUrl"/>, is empty or over its
    /// <see cref="Limit"/>, or has a "sha256:" digest that isn't 64 hex digits.
    /// An unfinished upload (a state other than "uploaded") doesn't count.
    /// </summary>
    public static UpdateChoice Files(UpdateRelease release, SemanticVersion version, string installerName, bool allowLocalFiles = false)
    {
        UpdateRefusal? problem = null;
        UpdateFile? Pick(string name, long limit, bool required)
        {
            var found = release.Assets.Where(asset => asset.Name == name && asset.State is null or "uploaded").ToList();
            if (found.Count == 0)
            {
                if (required) problem ??= UpdateRefusal.MissingFile;
                return null;
            }
            var asset = found[0];
            if (found.Count > 1 || asset.Size <= 0 || asset.Size > limit
                || !UpdateDownloadRules.IsTrustedAssetUrl(asset.DownloadUrl, release.TagName, name, allowLocalFiles)
                || !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var url)
                || !UpdateChecksums.TryReadDigest(asset.Digest, out var sha256))
            {
                problem = UpdateRefusal.BadFile;
                return null;
            }
            return new UpdateFile(name, url, asset.Size, sha256);
        }

        var installer = Pick(installerName, Limit.Installer, required: true);
        var list = Pick(ChecksumListName, Limit.ChecksumList, required: true);
        var signature = Pick(SignatureName, Limit.Signature, required: true);
        var checksum = Pick(installerName + ".sha256", Limit.Checksum, required: false);
        if (problem is { } reason || installer is null || list is null || signature is null)
            return new UpdateChoice.Refused(problem ?? UpdateRefusal.MissingFile);
        return new UpdateChoice.Ready(new UpdatePackage(version.ToString(), installer, list, signature, checksum));
    }
}

/// <summary>Where an expected SHA-256 came from (the Mac's UpdateHashSource).</summary>
public enum UpdateHashSource
{
    /// <summary>The signed SHA256SUMS.</summary>
    List,
    /// <summary>The installer's own .sha256 file.</summary>
    ChecksumFile,
    /// <summary>GitHub's digest in the feed.</summary>
    Digest,
}

/// <summary>Checksums in the formats a release uses (the Mac's UpdateChecksums).</summary>
public static class UpdateChecksums
{
    /// <summary>64 hex digits, any case.</summary>
    public static bool IsSha256Hex(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);

    /// <summary>
    /// GitHub's <c>digest</c>: "sha256:&lt;64 hex&gt;" gives the lower-case hex;
    /// none, or another algorithm, gives null (nothing to compare); "sha256:"
    /// with anything else is false.
    /// </summary>
    public static bool TryReadDigest(string? digest, out string? sha256)
    {
        sha256 = null;
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal)) return true;
        var hex = digest["sha256:".Length..];
        if (!IsSha256Hex(hex)) return false;
        sha256 = hex.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// A <c>.sha256</c> file as <c>shasum -a 256</c> writes it: one line, 64
    /// hex digits (any case), two spaces (or a space and <c>*</c>) and the
    /// file's name, which must be <paramref name="fileName"/>; one final LF or
    /// CRLF is dropped. Returns the digest in lower case, or null.
    /// </summary>
    public static string? ParseSha256File(ReadOnlySpan<byte> data, string fileName) => ReadSha256File(data, fileName).Sha256;

    /// <summary>
    /// <see cref="ParseSha256File(ReadOnlySpan{byte}, string)"/> with the
    /// reason when it fails, as the shared cases name it:
    /// "malformedChecksumFile" (not one such line) or "nameMismatch" (a line
    /// about another file).
    /// </summary>
    public static (string? Sha256, string? Error) ReadSha256File(ReadOnlySpan<byte> data, string fileName)
    {
        const string malformed = "malformedChecksumFile";
        if (data.EndsWith("\n"u8))
        {
            data = data[..^1];
            if (data.EndsWith("\r"u8)) data = data[..^1];
        }
        if (data.Length <= 66 || data.Contains((byte)'\n') || data.Contains((byte)'\r')) return (null, malformed);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return (null, malformed);
        }
        // Bytes 0–63 are ASCII when they are hex digits, so the text keeps their positions.
        var hex = text[..64];
        if (!IsSha256Hex(hex) || text[64] != ' ' || text[65] is not (' ' or '*')) return (null, malformed);
        return text[66..] == fileName ? (hex.ToLowerInvariant(), null) : (null, "nameMismatch");
    }

    /// <summary><see cref="ParseSha256File(ReadOnlySpan{byte}, string)"/> for text.</summary>
    public static string? ParseSha256File(string text, string fileName) => ParseSha256File(Encoding.UTF8.GetBytes(text), fileName);

    /// <summary>
    /// The downloaded file's SHA-256 against every source, in this order: the
    /// signed list, then the .sha256 file and GitHub's digest when there are
    /// any. Returns the first that disagrees, or null when all agree.
    /// </summary>
    public static UpdateHashSource? Disagreement(string actual, string listed, string? checksumFile, string? digest)
    {
        if (!string.Equals(listed, actual, StringComparison.OrdinalIgnoreCase)) return UpdateHashSource.List;
        if (checksumFile is not null && !string.Equals(checksumFile, actual, StringComparison.OrdinalIgnoreCase)) return UpdateHashSource.ChecksumFile;
        if (digest is not null && !string.Equals(digest, actual, StringComparison.OrdinalIgnoreCase)) return UpdateHashSource.Digest;
        return null;
    }

    public static string Hex(ReadOnlySpan<byte> digest) => Convert.ToHexStringLower(digest);

    /// <summary>A Windows program starts with "MZ".</summary>
    public static bool IsWindowsProgram(ReadOnlySpan<byte> start) => start.Length >= 2 && start[0] == (byte)'M' && start[1] == (byte)'Z';

    /// <summary>
    /// Reads <paramref name="stream"/> to the end: its SHA-256 in lower-case
    /// hex, its length and whether it is a Windows program.
    /// </summary>
    public static (string Sha256, long Length, bool IsProgram) Measure(Stream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        var head = new byte[2];
        var headLength = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (headLength < head.Length)
            {
                var take = Math.Min(head.Length - headLength, read);
                Array.Copy(buffer, 0, head, headLength, take);
                headLength += take;
            }
            hash.AppendData(buffer, 0, read);
            length += read;
        }
        return (Hex(hash.GetHashAndReset()), length, IsWindowsProgram(head.AsSpan(0, headLength)));
    }
}

/// <summary>
/// What the app and Windows/Installer/Lumerist.iss agree on for "Update now"
/// (a test reads the .iss to keep them in step).
/// </summary>
public static class WindowsInstaller
{
    /// <summary>The single-instance mutex, which is also the installer's AppMutex.</summary>
    public const string InstanceMutexName = @"Local\io.github.ziago.lumerist";

    /// <summary>Held by the Lumerist that started the installer until it has quit; the installer waits for it.</summary>
    public const string UpdatingMutexName = @"Local\io.github.ziago.lumerist.updating";

    /// <summary>Tells the installer that "Update now" started it: wait for Lumerist, then open the new version.</summary>
    public const string RelaunchSwitch = "/LUMERISTRELAUNCH";

    /// <summary>What the installer passes to the new version it opens (in the background).</summary>
    public const string AfterUpdateArgument = "--after-update";

    /// <summary>Silent, no restart, close what uses its files, and <see cref="RelaunchSwitch"/>.</summary>
    public static IReadOnlyList<string> Arguments { get; } =
        ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", RelaunchSwitch];
}

/// <summary>
/// Whether this copy of Lumerist is the one the installer manages: only then
/// can "Update now" replace it. A copy unzipped anywhere else would stay as
/// it is while the installer put a second one in its own folder.
/// </summary>
public static class UpdateInstallLocation
{
    /// <summary>The installer's uninstall entry (Windows/Installer/Lumerist.iss: AppId plus "_is1"), under HKCU.</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{1F277E78-9863-45D4-8EED-31E8CEF3D8F1}_is1";

    /// <summary>The value Inno Setup writes there with the program folder.</summary>
    public const string InstallLocationValue = "InstallLocation";

    /// <summary>The installer's folder (DefaultDirName={localappdata}\Programs\Lumerist).</summary>
    public static string DefaultFolder(string localAppData) => localAppData.TrimEnd('\\', '/') + @"\Programs\Lumerist";

    /// <summary>
    /// <paramref name="appFolder"/> (the folder of the running Lumerist.exe)
    /// is the installed program folder: the one the uninstall entry names,
    /// or, without one, the installer's default folder. Windows paths,
    /// compared without case; relative paths and "." or ".." parts never match.
    /// </summary>
    public static bool IsInstalledCopy(string? appFolder, string? localAppData, string? registeredLocation)
    {
        var target = string.IsNullOrWhiteSpace(registeredLocation)
            ? string.IsNullOrWhiteSpace(localAppData) ? null : DefaultFolder(localAppData)
            : registeredLocation;
        return Normalize(appFolder) is { } app && Normalize(target) is { } installed
            && string.Equals(app, installed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Backslashes, no trailing one; null for anything but a plain absolute path ("C:\…").</summary>
    internal static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Replace('/', '\\').TrimEnd('\\');
        if (text.Length < 3 || !char.IsAsciiLetter(text[0]) || text[1] != ':' || text[2] != '\\') return null;
        if (text.Split('\\').Skip(1).Any(part => part is "" or "." or "..")) return null;
        return text;
    }
}
