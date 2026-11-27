using System.Net.Http.Headers;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>One entry of the public release feed: only the fields the update check reads.</summary>
/// <param name="TagName">The release's tag, e.g. "v0.9.1" or "v1.0.0-beta.2".</param>
/// <param name="HtmlUrl">Its page; opened only when <see cref="UpdateCheck.IsTrustedReleasePage"/> accepts it.</param>
public sealed record UpdateRelease(string TagName, Uri HtmlUrl, bool Draft)
{
    /// <summary>The release notes (Markdown), or null. Only shown as plain text (<see cref="ReleaseHighlights"/>).</summary>
    public string? Body { get; init; }

    /// <summary>The files of the release, as the feed lists them (<c>assets</c>).</summary>
    public IReadOnlyList<ReleaseAsset> Assets { get; init; } = [];
}

/// <summary>One file of a release (an entry of the feed's <c>assets</c>).</summary>
/// <param name="Name">The file name, e.g. "Lumerist-0.9.2-windows-x64-setup.exe".</param>
/// <param name="DownloadUrl">Its <c>browser_download_url</c>, kept as written: compared as text (<see cref="UpdateDownloadRules.IsTrustedAssetUrl"/>).</param>
/// <param name="Size">Its size in bytes, as the feed declares it.</param>
/// <param name="Digest">GitHub's <c>digest</c> ("sha256:&lt;hex&gt;"), when the feed gives one.</param>
/// <param name="State">"uploaded", or "open" while an upload is unfinished; null when the feed doesn't say.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Digest = null, string? State = "uploaded");

/// <summary>A release newer than the running app (<see cref="UpdateCheck.Newer"/>).</summary>
/// <param name="Page">Its release page, or the list of releases when the feed's isn't trusted.</param>
public sealed record AvailableUpdate(SemanticVersion Version, string Tag, Uri Page, UpdateRelease Release)
{
    /// <summary>The "What's new" lines, as plain text.</summary>
    public IReadOnlyList<string> Highlights => ReleaseHighlights.Parse(Release.Body);
}

/// <summary>What the update check found (the Mac's <c>UpdateChecker.State</c>).</summary>
public abstract record UpdateCheckState
{
    private UpdateCheckState() { }

    public sealed record Idle() : UpdateCheckState;

    public sealed record Checking() : UpdateCheckState;

    public sealed record UpToDate(string Version) : UpdateCheckState;

    /// <param name="Release">The newer release: its notes and files, for the update window.</param>
    public sealed record Available(string Version, Uri Page) : UpdateCheckState
    {
        public UpdateRelease? Release { get; init; }
    }

    public sealed record NoReleases() : UpdateCheckState;

    public sealed record Failed(string Message) : UpdateCheckState;
}

/// <summary>
/// The rules of the update check, the same as the Mac's UpdateChecker: it only
/// reads the public release feed, and checks by itself at most once a day
/// unless the user turned that off (it is on by default). Nothing is
/// downloaded unless the user presses "Update now" (<see cref="UpdatePreparation"/>).
/// </summary>
public static partial class UpdateCheck
{
    /// <summary>The automatic check: at launch, at most once a day, unless the user turned it off.</summary>
    public static readonly TimeSpan AutomaticInterval = TimeSpan.FromDays(1);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The message after a decoding failure, as the Mac shows it.</summary>
    internal const string UnreadableFeed = "The data couldn't be read because it isn't in the correct format.";

    /// <summary>
    /// The automatic check is due: turned on, and never checked or the last
    /// real answer is at least <see cref="AutomaticInterval"/> (a day) old.
    /// </summary>
    public static bool IsDue(bool checksAutomatically, DateTimeOffset? lastCheck, DateTimeOffset now) =>
        checksAutomatically && (lastCheck is not { } last || now - last >= AutomaticInterval);

    /// <summary>
    /// Only a https page of this project's releases on github.com is opened,
    /// whatever the feed answered.
    /// </summary>
    /// <remarks>
    /// The path is compared as the browser will request it, and an escaped
    /// character (%2F, %2E…) is refused, so nothing can hide a step out of
    /// the releases folder; so are another port and user info.
    /// </remarks>
    public static bool IsTrustedReleasePage(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && url.Host == "github.com"
        && url.IsDefaultPort
        && url.UserInfo.Length == 0
        && !url.AbsolutePath.Contains('%')
        // As written too: .NET removes "." and ".." steps before AbsolutePath.
        && !WrittenPath(url.OriginalString).Contains('%')
        && !WrittenPath(url.OriginalString).Split('/').Contains("..")
        && url.AbsolutePath.StartsWith(SupportLinks.Releases.AbsolutePath + "/", StringComparison.Ordinal);

    /// <summary>The path of an absolute address as written: after the host, before "?" or "#".</summary>
    private static string WrittenPath(string text)
    {
        var scheme = text.IndexOf("//", StringComparison.Ordinal);
        if (scheme < 0) return text;
        var slash = text.IndexOf('/', scheme + 2);
        if (slash < 0) return "";
        var end = text.IndexOfAny(['?', '#'], slash);
        return end < 0 ? text[slash..] : text[slash..end];
    }

    /// <summary>
    /// The update "Update now" and the update window offer (the Mac's
    /// UpdateChoice.update): the newest non-draft release with a
    /// <see cref="TagVersion"/> tag (the first in the feed's order wins a
    /// tie), only when strictly newer than <paramref name="current"/>, which
    /// must be a <see cref="CurrentVersion"/>: never a downgrade, and nothing
    /// for a development build. Its page is the feed's when it is one of this
    /// project's release pages, else the list of releases.
    /// </summary>
    public static AvailableUpdate? Newer(IEnumerable<UpdateRelease> releases, string current)
    {
        if (CurrentVersion(current) is not { } running) return null;
        (SemanticVersion Version, UpdateRelease Release)? newest = null;
        foreach (var release in releases)
        {
            if (release.Draft || TagVersion(release.TagName) is not { } version) continue;
            if (newest is null || newest.Value.Version < version) newest = (version, release);
        }
        if (newest is not { } found || !(running < found.Version)) return null;
        var page = IsTrustedReleasePage(found.Release.HtmlUrl) ? found.Release.HtmlUrl : SupportLinks.Releases;
        return new AvailableUpdate(found.Version, found.Release.TagName, page, found.Release);
    }

    /// <summary>
    /// The version of a release tag: only <c>v</c> and a semantic version
    /// ("v0.9.2", "v1.0.0-beta.2"); anything else ("0.9.2", "V1.0.0",
    /// "v1.0", "nightly", build metadata) is no release of Lumerist.
    /// </summary>
    public static SemanticVersion? TagVersion(string? tag) =>
        tag is { Length: <= MaxTagLength } && ReleaseTag().IsMatch(tag) ? SemanticVersion.FromText(tag) : null;

    /// <summary>
    /// The running app's version as an update compares it: a semantic version
    /// without <c>v</c>. A development build has none ("?"), and is never
    /// offered "Update now".
    /// </summary>
    public static SemanticVersion? CurrentVersion(string? text) =>
        text is { Length: <= MaxTagLength } && PlainVersion().IsMatch(text) ? SemanticVersion.FromText(text) : null;

    /// <summary>The longest tag (or version) considered.</summary>
    internal const int MaxTagLength = 64;

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ReleaseTag();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PlainVersion();

    /// <summary>
    /// The newest non-draft release (pre-releases included) against the
    /// running version. Only <see cref="TagVersion"/> tags count, and only a
    /// strictly newer one is offered: never a downgrade.
    /// </summary>
    public static UpdateCheckState Evaluate(IEnumerable<UpdateRelease> releases, string current)
    {
        (SemanticVersion Version, UpdateRelease Release)? newest = null;
        foreach (var release in releases)
        {
            if (release.Draft || TagVersion(release.TagName) is not { } version) continue;
            // The first of equal versions wins.
            if (newest is null || newest.Value.Version < version) newest = (version, release);
        }
        if (newest is not { } found) return new UpdateCheckState.NoReleases();
        var (latest, chosen) = found;
        var available = new UpdateCheckState.Available(latest.ToString(), chosen.HtmlUrl) { Release = chosen };
        // A development build: just point at the newest release (it can't install it: UpdateAssets.Choose).
        if (SemanticVersion.FromText(current) is not { } running) return available;
        return running < latest ? available : new UpdateCheckState.UpToDate(running.ToString());
    }

    /// <summary>
    /// Maps the feed's answer. <c>Counts</c> says whether it was a real answer:
    /// only then is the time of the check remembered, so being offline or
    /// rate-limited never silences the automatic check for a day.
    /// </summary>
    public static (UpdateCheckState State, bool Counts) FromResponse(int statusCode, ReadOnlySpan<byte> body, string current)
    {
        if (statusCode == 404) return (new UpdateCheckState.NoReleases(), true);
        if (statusCode != 200) return (new UpdateCheckState.Failed($"GitHub answered with status {statusCode}. Try again later."), false);
        if (DecodeReleases(body) is not { } releases) return (new UpdateCheckState.Failed(CouldNotReach(UnreadableFeed)), false);
        return (Evaluate(releases, current), true);
    }

    public static string CouldNotReach(string reason) => $"Could not reach GitHub: {reason}";

    /// <summary>
    /// The feed: an array of objects, each with a string <c>tag_name</c>, a
    /// string <c>html_url</c> and a boolean <c>draft</c> (other fields are
    /// ignored). Anything else makes the whole answer unreadable (null), as
    /// the Mac's decoder does. The release notes (<c>body</c>) and the files
    /// (<c>assets</c>) are optional: a missing or odd one only means no
    /// highlights, or a file that cannot be installed.
    /// </summary>
    public static IReadOnlyList<UpdateRelease>? DecodeReleases(ReadOnlySpan<byte> body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            using var document = JsonDocument.ParseValue(ref reader);
            if (reader.Read()) return null; // trailing data
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var releases = new List<UpdateRelease>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) return null;
                if (!element.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) return null;
                if (!element.TryGetProperty("html_url", out var html) || html.ValueKind != JsonValueKind.String) return null;
                if (!element.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                if (!Uri.TryCreate(html.GetString(), UriKind.RelativeOrAbsolute, out var page)) return null;
                releases.Add(new UpdateRelease(tag.GetString()!, page, draft.GetBoolean())
                {
                    Body = element.TryGetProperty("body", out var notes) && notes.ValueKind == JsonValueKind.String ? notes.GetString() : null,
                    Assets = element.TryGetProperty("assets", out var assets) ? DecodeAssets(assets) : [],
                });
            }
            return releases;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The files with a string <c>name</c>, a string
    /// <c>browser_download_url</c> and an integer <c>size</c>; <c>digest</c>
    /// and <c>state</c> when they are strings. Other entries are left out,
    /// never the whole answer.
    /// </summary>
    private static List<ReleaseAsset> DecodeAssets(JsonElement assets)
    {
        var list = new List<ReleaseAsset>();
        if (assets.ValueKind != JsonValueKind.Array) return list;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            if (!asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (!asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String) continue;
            if (!asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes)) continue;
            list.Add(new ReleaseAsset(name.GetString()!, url.GetString()!, bytes, OptionalText(asset, "digest"), OptionalText(asset, "state")));
        }
        return list;
    }

    private static string? OptionalText(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// Reads the release feed once per call. The handler is injectable so tests
/// never touch the network; the default one keeps no cookies.
/// </summary>
public sealed class UpdateFeedClient
{
    /// <summary>The most a feed file may hold (debug builds' <c>LUMERIST_UPDATE_FEED_URL</c>).</summary>
    internal const int FeedFileLimit = 8 * 1024 * 1024;

    private readonly HttpClient http;
    private readonly string? feedFile;

    /// <param name="handler">For tests; null uses a shared client.</param>
    /// <param name="feedFile">
    /// A local file in the feed's format, read instead of GitHub (debug
    /// builds only, for testing the updater). Its answers are never
    /// remembered as a check.
    /// </param>
    public UpdateFeedClient(HttpMessageHandler? handler = null, string? feedFile = null)
    {
        http = handler is null ? SharedClient.Value : new HttpClient(handler, disposeHandler: false) { Timeout = UpdateCheck.Timeout };
        this.feedFile = feedFile;
    }

    /// <summary>A plain GET of <see cref="SupportLinks.ReleasesApi"/>: nothing about the user is sent.</summary>
    public static HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, SupportLinks.ReleasesApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("User-Agent", "Lumerist");
        return request;
    }

    /// <summary>Asks the feed; every failure becomes <see cref="UpdateCheckState.Failed"/>, never an exception.</summary>
    public async Task<(UpdateCheckState State, bool Counts)> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (feedFile is not null) return await CheckFileAsync(feedFile, currentVersion, cancellationToken).ConfigureAwait(false);
        try
        {
            using var request = CreateRequest();
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return UpdateCheck.FromResponse((int)response.StatusCode, body, currentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up (the window closed): no result to show.
            return (new UpdateCheckState.Idle(), false);
        }
        catch (Exception error)
        {
            return (new UpdateCheckState.Failed(UpdateCheck.CouldNotReach(error.Message)), false);
        }
    }

    private static async Task<(UpdateCheckState State, bool Counts)> CheckFileAsync(string path, string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > FeedFileLimit) return (new UpdateCheckState.Failed("The test feed is too large."), false);
            var body = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return (UpdateCheck.FromResponse(200, body, currentVersion).State, false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (new UpdateCheckState.Failed($"The test feed could not be read: {error.Message}"), false);
        }
    }

    /// <summary>Created on first use.</summary>
    private static readonly Lazy<HttpClient> SharedClient = new(() =>
        new HttpClient(new HttpClientHandler { UseCookies = false, UseDefaultCredentials = false }) { Timeout = UpdateCheck.Timeout });
}
