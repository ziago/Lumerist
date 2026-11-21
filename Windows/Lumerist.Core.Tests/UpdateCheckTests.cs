using System.Net;
using System.Text;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The update check (the Mac's UpdateChecker): the newest non-draft release
/// against the running version, only github.com release pages are opened,
/// only real answers count as a check, and the automatic check runs at most
/// once a day unless turned off.
/// </summary>
public class UpdateCheckTests
{
    private static UpdateRelease Release(string tag, bool draft = false) =>
        new(tag, new Uri($"https://github.com/ziago/Lumerist/releases/tag/{tag}"), draft);

    [Fact]
    public void OffersANewerRelease()
    {
        var state = UpdateCheck.Evaluate([Release("v0.9.0"), Release("v0.9.1")], "0.9.0");
        var available = Assert.IsType<UpdateCheckState.Available>(state);
        Assert.Equal("0.9.1", available.Version);
        Assert.Equal("https://github.com/ziago/Lumerist/releases/tag/v0.9.1", available.Page.AbsoluteUri);
    }

    [Fact]
    public void IsUpToDateWithTheNewestOrANewerBuild()
    {
        Assert.Equal(new UpdateCheckState.UpToDate("0.9.1"), UpdateCheck.Evaluate([Release("v0.9.0"), Release("v0.9.1")], "0.9.1"));
        Assert.Equal(new UpdateCheckState.UpToDate("1.0.0"), UpdateCheck.Evaluate([Release("v0.9.1")], "1.0.0"));
    }

    [Fact]
    public void SkipsDraftsButNotPreReleases()
    {
        Assert.Equal(new UpdateCheckState.UpToDate("0.9.0"), UpdateCheck.Evaluate([Release("v0.9.0"), Release("v1.0.0", draft: true)], "0.9.0"));
        var state = Assert.IsType<UpdateCheckState.Available>(UpdateCheck.Evaluate([Release("v0.9.0"), Release("v0.10.0-beta.1")], "0.9.0"));
        Assert.Equal("0.10.0-beta.1", state.Version);
        // A pre-release sorts before its release.
        Assert.Equal(new UpdateCheckState.UpToDate("1.0.0"), UpdateCheck.Evaluate([Release("v1.0.0-beta.2")], "1.0.0"));
    }

    [Fact]
    public void NoReleasesWhenNothingUsableIsPublished()
    {
        Assert.Equal(new UpdateCheckState.NoReleases(), UpdateCheck.Evaluate([], "0.9.0"));
        Assert.Equal(new UpdateCheckState.NoReleases(), UpdateCheck.Evaluate([Release("nightly"), Release("v2.0.0", draft: true)], "0.9.0"));
    }

    [Fact]
    public void ADevelopmentBuildPointsAtTheNewestRelease()
    {
        var state = Assert.IsType<UpdateCheckState.Available>(UpdateCheck.Evaluate([Release("v0.9.1"), Release("v0.9.0")], "?"));
        Assert.Equal("0.9.1", state.Version);
    }

    [Fact]
    public void TheFirstOfEqualVersionsGivesThePage()
    {
        UpdateRelease[] releases =
        [
            new("v1.0.0", new Uri("https://github.com/ziago/Lumerist/releases/tag/first"), false),
            new("1.0.0", new Uri("https://github.com/ziago/Lumerist/releases/tag/second"), false),
        ];
        var state = Assert.IsType<UpdateCheckState.Available>(UpdateCheck.Evaluate(releases, "0.9.0"));
        Assert.EndsWith("/first", state.Page.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/ziago/Lumerist/releases/tag/v0.9.1", true)]
    [InlineData("https://github.com/ziago/Lumerist/releases/download/v0.9.1/Lumerist.zip", true)]
    [InlineData("http://github.com/ziago/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://example.com/ziago/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://github.com.example.com/ziago/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://github.com/someone/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://github.com/ziago/Lumerist/releases-elsewhere/tag/v0.9.1", false)]
    [InlineData("https://github.com/ziago/Lumerist/releases", false)]
    [InlineData("https://github.com/ziago/Lumerist/releases/../../other/page", false)]
    [InlineData("https://github.com:8443/ziago/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://someone@github.com/ziago/Lumerist/releases/tag/v0.9.1", false)]
    [InlineData("https://github.com/ziago/Lumerist/releases/%2E%2E%2Fother", false)]
    [InlineData("https://github.com/ziago/Lumerist/releases%2Ftag/v0.9.1", false)]
    public void OpensOnlyThisProjectsReleasePages(string page, bool trusted)
    {
        Assert.Equal(trusted, UpdateCheck.IsTrustedReleasePage(new Uri(page)));
    }

    [Fact]
    public void ARelativePageIsNeverTrusted()
    {
        Assert.False(UpdateCheck.IsTrustedReleasePage(new Uri("releases/tag/v1", UriKind.Relative)));
    }

    [Fact]
    public void OnlyARealAnswerCountsAsACheck()
    {
        var (missing, missingCounts) = UpdateCheck.FromResponse(404, "{}"u8, "0.9.0");
        Assert.Equal(new UpdateCheckState.NoReleases(), missing);
        Assert.True(missingCounts);

        var (limited, limitedCounts) = UpdateCheck.FromResponse(403, "{}"u8, "0.9.0");
        Assert.Equal(new UpdateCheckState.Failed("GitHub answered with status 403. Try again later."), limited);
        Assert.False(limitedCounts);

        var (garbled, garbledCounts) = UpdateCheck.FromResponse(200, "{\"message\":\"hi\"}"u8, "0.9.0");
        Assert.Equal(new UpdateCheckState.Failed("Could not reach GitHub: The data couldn't be read because it isn't in the correct format."), garbled);
        Assert.False(garbledCounts);

        var (found, foundCounts) = UpdateCheck.FromResponse(200, Feed, "0.9.0");
        Assert.Equal("0.9.1", Assert.IsType<UpdateCheckState.Available>(found).Version);
        Assert.True(foundCounts);
    }

    private static readonly byte[] Feed = Encoding.UTF8.GetBytes("""
        [
          {"tag_name": "v0.9.2", "html_url": "https://github.com/ziago/Lumerist/releases/tag/v0.9.2", "draft": true, "name": "draft"},
          {"tag_name": "v0.9.1", "html_url": "https://github.com/ziago/Lumerist/releases/tag/v0.9.1", "draft": false, "prerelease": false},
          {"tag_name": "v0.9.0", "html_url": "https://github.com/ziago/Lumerist/releases/tag/v0.9.0", "draft": false}
        ]
        """);

    [Fact]
    public void DecodesOnlyWellFormedFeeds()
    {
        var releases = UpdateCheck.DecodeReleases(Feed);
        Assert.NotNull(releases);
        Assert.Equal(3, releases.Count);
        Assert.True(releases[0].Draft);
        Assert.Equal("v0.9.1", releases[1].TagName);

        Assert.Empty(UpdateCheck.DecodeReleases("[]"u8)!);
        Assert.Null(UpdateCheck.DecodeReleases("""[{"tag_name": "v1", "html_url": "https://github.com/x"}]"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("""[{"tag_name": 1, "html_url": "https://github.com/x", "draft": false}]"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("""[{"tag_name": "v1", "html_url": null, "draft": false}]"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("""[{"tag_name": "v1", "html_url": "https://github.com/x", "draft": "no"}]"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("""[1]"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("""[] []"""u8));
        Assert.Null(UpdateCheck.DecodeReleases("not json"u8));
    }

    [Fact]
    public void TheAutomaticCheckRunsAtMostOnceADayUnlessTurnedOff()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        Assert.False(UpdateCheck.IsDue(false, null, now));
        Assert.True(UpdateCheck.IsDue(true, null, now));
        Assert.False(UpdateCheck.IsDue(true, now.AddDays(-1).AddSeconds(1), now));
        Assert.True(UpdateCheck.IsDue(true, now.AddDays(-1), now));
        // A check dated in the future waits until a day after it.
        Assert.False(UpdateCheck.IsDue(true, now.AddHours(2), now));
    }

    [Fact]
    public async Task ReadsThePublicFeedWithAPlainRequest()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Feed) });
        var (state, counts) = await new UpdateFeedClient(handler).CheckAsync("0.9.0");
        Assert.Equal("0.9.1", Assert.IsType<UpdateCheckState.Available>(state).Version);
        Assert.True(counts);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.github.com/repos/ziago/Lumerist/releases?per_page=20", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/vnd.github+json", Assert.Single(request.Headers.Accept).MediaType);
        Assert.Equal("Lumerist", string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task ANetworkFailureIsAMessageNotAnException()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
        var (state, counts) = await new UpdateFeedClient(handler).CheckAsync("0.9.0");
        Assert.Equal(new UpdateCheckState.Failed("Could not reach GitHub: No such host is known."), state);
        Assert.False(counts);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(reply(request));
        }
    }
}
