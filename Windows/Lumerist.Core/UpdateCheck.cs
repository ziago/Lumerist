using System.Net.Http.Headers;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>One entry of the public release feed: only the fields the update check reads.</summary>
/// <param name="TagName">The release's tag, e.g. "v0.9.1" or "v1.0.0-beta.2".</param>
/// <param name="HtmlUrl">Its page; opened only when <see cref="UpdateCheck.IsTrustedReleasePage"/> accepts it.</param>
public sealed record UpdateRelease(string TagName, Uri HtmlUrl, bool Draft);

/// <summary>What the update check found (the Mac's <c>UpdateChecker.State</c>).</summary>
public abstract record UpdateCheckState
{
    private UpdateCheckState() { }

    public sealed record Idle() : UpdateCheckState;

    public sealed record Checking() : UpdateCheckState;

    public sealed record UpToDate(string Version) : UpdateCheckState;

    public sealed record Available(string Version, Uri Page) : UpdateCheckState;

    public sealed record NoReleases() : UpdateCheckState;

    public sealed record Failed(string Message) : UpdateCheckState;
}

/// <summary>
/// The rules of the update check, the same as the Mac's UpdateChecker: it only
/// reads the public release feed, never downloads or installs anything (a
/// newer version only opens its release page), and checks by itself at most
/// once a day unless the user turned that off (it is on by default).
/// </summary>
public static class UpdateCheck
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
        && url.AbsolutePath.StartsWith(SupportLinks.Releases.AbsolutePath + "/", StringComparison.Ordinal);

    /// <summary>The newest non-draft release (pre-releases included) against the running version.</summary>
    public static UpdateCheckState Evaluate(IEnumerable<UpdateRelease> releases, string current)
    {
        var published = releases.Where(release => !release.Draft).ToList();
        if (SemanticVersion.Newest(published.Select(release => release.TagName)) is not { } newest)
            return new UpdateCheckState.NoReleases();
        var page = published.First(release => release.TagName == newest.Tag).HtmlUrl;
        // A development build: just point at the newest release.
        if (SemanticVersion.FromText(current) is not { } running) return new UpdateCheckState.Available(newest.Version.ToString(), page);
        return running < newest.Version
            ? new UpdateCheckState.Available(newest.Version.ToString(), page)
            : new UpdateCheckState.UpToDate(running.ToString());
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
    /// the Mac's decoder does.
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
                releases.Add(new UpdateRelease(tag.GetString()!, page, draft.GetBoolean()));
            }
            return releases;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads the release feed once per call. The handler is injectable so tests
/// never touch the network; the default one keeps no cookies.
/// </summary>
public sealed class UpdateFeedClient
{
    private readonly HttpClient http;

    /// <param name="handler">For tests; null uses a shared client.</param>
    public UpdateFeedClient(HttpMessageHandler? handler = null)
    {
        http = handler is null ? SharedClient.Value : new HttpClient(handler, disposeHandler: false) { Timeout = UpdateCheck.Timeout };
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

    /// <summary>Created on first use.</summary>
    private static readonly Lazy<HttpClient> SharedClient = new(() =>
        new HttpClient(new HttpClientHandler { UseCookies = false, UseDefaultCredentials = false }) { Timeout = UpdateCheck.Timeout });
}
