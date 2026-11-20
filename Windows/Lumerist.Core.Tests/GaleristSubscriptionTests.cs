using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// The optional "Stay in touch" sign-up (contract lk-optin-v2). Port of
/// Tests/LumeristCoreTests/GaleristSubscriptionTests.swift; no test here touches
/// the network (the requests go to a fake handler; the Swift live test is not ported).
/// </summary>
public class GaleristSubscriptionTests
{
    private const string ConsentHash = "4bd6382380b390149de25bed81c1639306528d76b97e6c4161cb99b6062f50b7";

    // Texts: stored by the server as proof of consent, so exact.

    [Fact]
    public void ConsentTextsMatchTheContractCharacterForCharacter()
    {
        Assert.Equal("lk-optin-v2", GaleristOptIn.ConsentVersion);
        Assert.Equal("Stay in touch", GaleristOptIn.Copy.Title);
        Assert.Equal("Leave your email to hear about new versions of Lumerist and about other photography tools from its developer.", GaleristOptIn.Copy.Message);
        Assert.Equal("you@example.com", GaleristOptIn.Copy.EmailPlaceholder);
        Assert.Equal("Optional and free. No spam: every email has a one-click unsubscribe.", GaleristOptIn.Copy.Notice);
        Assert.Equal("Privacy", GaleristOptIn.Copy.PrivacyLink);
        Assert.Equal("Keep me posted", GaleristOptIn.Copy.Button);
        Assert.Equal("Not now", GaleristOptIn.Copy.NotNow);
        Assert.Equal("Don't ask me again", GaleristOptIn.Copy.DontAskAgain);
        Assert.Equal("Thanks, you're on the list.", GaleristOptIn.Copy.Success);
        Assert.Equal("Done", GaleristOptIn.Copy.Done);
        Assert.Equal("News about Lumerist and other photography tools from its developer, by email.", GaleristOptIn.Copy.AboutDetail);
        Assert.Equal("Leave your email…", GaleristOptIn.Copy.AboutButton);
        Assert.Equal("You're on the list", GaleristOptIn.Copy.AboutSubscribed);
        Assert.Equal("Lumerist is made by the creator of Galerist, online galleries where photographers and videographers deliver their work to clients.", GaleristOptIn.Copy.Credit);
    }

    private static string Sha256(params string[] texts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", texts))));

    /// <summary>
    /// Drift guard: the Galerist server and the Mac app run the same check, so
    /// the texts the app shows and the proof of consent the server stores can
    /// never drift apart. SHA-256 of Message and Notice joined with "\n"
    /// (UTF-8, no trailing newline).
    /// </summary>
    [Fact]
    public void ConsentTextsMatchTheServerDriftGuard()
    {
        Assert.Equal("lk-optin-v2", GaleristOptIn.ConsentVersion);
        Assert.Equal(ConsentHash, Sha256(GaleristOptIn.Copy.Message, GaleristOptIn.Copy.Notice));
    }

    [Fact]
    public void ConsentMatchesSharedContract()
    {
        var consent = Fixtures.Object(Path.Combine("Shared", "contract.json"))["consent"]!.AsObject();
        Assert.Equal(GaleristOptIn.ConsentVersion, consent["version"]!.GetValue<string>());
        Assert.Equal(ConsentHash, consent["sha256"]!.GetValue<string>());
    }

    [Fact]
    public void ConsentTextsUseStraightAsciiPunctuation()
    {
        foreach (var text in new[]
        {
            GaleristOptIn.Copy.Title, GaleristOptIn.Copy.Message, GaleristOptIn.Copy.Notice, GaleristOptIn.Copy.Button,
            GaleristOptIn.Copy.NotNow, GaleristOptIn.Copy.DontAskAgain, GaleristOptIn.Copy.Success,
            GaleristOptIn.Copy.AboutDetail, GaleristOptIn.Copy.AboutSubscribed, GaleristOptIn.Copy.Credit,
        })
        {
            Assert.True(text.All(char.IsAscii), text);
        }
    }

    [Fact]
    public void LinksPointAtGalerist()
    {
        Assert.Equal("https://galerist.photo/", GaleristOptIn.ProductionBaseUrl.AbsoluteUri);
        Assert.Equal("https://galerist.photo/en/legal/privacy#lumenkeys", GaleristOptIn.PrivacyUrl.AbsoluteUri);
        Assert.Equal("lumerist.galerist.subscribed-at", GaleristOptIn.SubscribedAtDefaultsKey);
        Assert.Equal("https://galerist.photo/c/lk-onboarding", GaleristOptIn.TrackedLink.Onboarding.Url().AbsoluteUri);
        Assert.Equal("https://galerist.photo/c/lk-settings", GaleristOptIn.TrackedLink.Settings.Url().AbsoluteUri);
        Assert.Equal("https://galerist.photo/c/lk-menu", GaleristOptIn.TrackedLink.Menu.Url().AbsoluteUri);
        Assert.Equal("https://galerist.photo/c/lk-readme", GaleristOptIn.TrackedLink.Readme.Url().AbsoluteUri);
        Assert.Equal("https://galerist.photo/c/lk-release", GaleristOptIn.TrackedLink.Release.Url().AbsoluteUri);
    }

    // Encoding

    private static JsonElement Json(GaleristSubscription subscription)
    {
        using var document = JsonDocument.Parse(subscription.Encoded());
        return document.RootElement.Clone();
    }

    private static string[] Keys(JsonElement body) => body.EnumerateObject().Select(property => property.Name).ToArray();

    [Fact]
    public void EncodesEveryFieldWithTheContractKeys()
    {
        var subscription = new GaleristSubscription("name@example.com", "it", "0.9.0", new GaleristPrompt.Scheduled(2));
        var body = Json(subscription);
        Assert.Equal(new[] { "appVersion", "consentVersion", "email", "galeristNews", "language", "lumenkeysUpdates", "prompt" }, Keys(body));
        Assert.Equal("name@example.com", body.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.True, body.GetProperty("lumenkeysUpdates").ValueKind);
        Assert.Equal(JsonValueKind.True, body.GetProperty("galeristNews").ValueKind);
        Assert.Equal("it", body.GetProperty("language").GetString());
        Assert.Equal("lk-optin-v2", body.GetProperty("consentVersion").GetString());
        Assert.Equal("0.9.0", body.GetProperty("appVersion").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("prompt").ValueKind);
        Assert.Equal(2, body.GetProperty("prompt").GetInt32());
        Assert.False(body.TryGetProperty("shootsForClients", out _));
        // Booleans are JSON booleans, not 0/1; the prompt number is a JSON number.
        var text = Encoding.UTF8.GetString(subscription.Encoded());
        Assert.Contains("\"lumenkeysUpdates\":true", text);
        Assert.Contains("\"galeristNews\":true", text);
        Assert.Contains("\"prompt\":2", text);
        Assert.True(subscription.Encoded().Length < 2_048);
    }

    [Fact]
    public void EncodesTheSameBytesAsTheMacApp()
    {
        // Swift's JSONEncoder with .sortedKeys and .withoutEscapingSlashes.
        var subscription = new GaleristSubscription("name@example.com", "it", "0.9.0/beta", new GaleristPrompt.About());
        Assert.Equal(
            "{\"appVersion\":\"0.9.0/beta\",\"consentVersion\":\"lk-optin-v2\",\"email\":\"name@example.com\",\"galeristNews\":true,\"language\":\"it\",\"lumenkeysUpdates\":true,\"prompt\":\"about\"}",
            Encoding.UTF8.GetString(subscription.Encoded()));
        Assert.Equal(
            "{\"consentVersion\":\"lk-optin-v2\",\"email\":\"a@b.co\",\"galeristNews\":true,\"lumenkeysUpdates\":true}",
            Encoding.UTF8.GetString(new GaleristSubscription("a@b.co").Encoded()));
    }

    [Fact]
    public void V2AlwaysSendsBothConsents()
    {
        // The v2 text covers Lumerist news and the developer's other tools:
        // there is no way to send one without the other (the server answers
        // invalid_request to anything but true).
        var subscription = new GaleristSubscription("a@b.co");
        Assert.True(subscription.LumenkeysUpdates && subscription.GaleristNews);
        var body = Json(subscription);
        Assert.Equal(JsonValueKind.True, body.GetProperty("lumenkeysUpdates").ValueKind);
        Assert.Equal(JsonValueKind.True, body.GetProperty("galeristNews").ValueKind);
    }

    [Fact]
    public void EncodesThePromptAsANumberOrAbout()
    {
        for (var number = 1; number <= 3; number++)
        {
            var prompt = Json(new GaleristSubscription("a@b.co", prompt: new GaleristPrompt.Scheduled(number))).GetProperty("prompt");
            Assert.Equal(JsonValueKind.Number, prompt.ValueKind);
            Assert.Equal(number, prompt.GetInt32());
        }
        var about = Json(new GaleristSubscription("a@b.co", prompt: new GaleristPrompt.About())).GetProperty("prompt");
        Assert.Equal(JsonValueKind.String, about.ValueKind);
        Assert.Equal("about", about.GetString());
        Assert.Contains("\"prompt\":\"about\"", Encoding.UTF8.GetString(new GaleristSubscription("a@b.co", prompt: new GaleristPrompt.About()).Encoded()));
        // Outside the schedule: left out rather than sent wrong.
        foreach (var number in new[] { 0, 4, -1 })
        {
            var subscription = new GaleristSubscription("a@b.co", prompt: new GaleristPrompt.Scheduled(number));
            Assert.Null(subscription.Prompt);
            Assert.False(Json(subscription).TryGetProperty("prompt", out _));
        }
    }

    [Fact]
    public void OmitsOptionalFieldsWhenNull()
    {
        var body = Json(new GaleristSubscription("a@b.co"));
        Assert.Equal(new[] { "consentVersion", "email", "galeristNews", "lumenkeysUpdates" }, Keys(body));
        Assert.DoesNotContain("null", Encoding.UTF8.GetString(new GaleristSubscription("a@b.co").Encoded()));
    }

    [Fact]
    public void CleansTheFieldsItSends()
    {
        var subscription = new GaleristSubscription("  Name@Example.com\n", "IT", new string('9', 33));
        Assert.Equal("Name@Example.com", subscription.Email); // the server lowercases
        Assert.Null(subscription.Language);
        Assert.Null(subscription.AppVersion);
        Assert.Null(new GaleristSubscription("a@b.co", "ita", "").Language);
        Assert.Null(new GaleristSubscription("a@b.co", appVersion: "").AppVersion);
        Assert.Equal(32, new GaleristSubscription("a@b.co", appVersion: new string('1', 32)).AppVersion?.Length);
    }

    // Validation

    [Theory]
    [InlineData("name@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    [InlineData("a@b.co")]
    [InlineData("  padded@example.org ")]
    [InlineData("x_y-z@my-domain.photo")]
    [InlineData("user@xn--80ak6aa92e.xn--p1ai")]
    [InlineData("nome@dominio.it")]
    [InlineData("o'brien@example.ie")]
    public void AcceptsPlausibleEmails(string email) => Assert.True(GaleristOptIn.IsPlausibleEmail(email), email);

    [Fact]
    public void RejectsImplausibleEmails()
    {
        var tooLongLocal = new string('a', 65) + "@example.com";
        var longest = "a@" + new string('b', 60) + "." + new string('c', 60) + "." + new string('d', 60) + "." + new string('e', 63) + ".photo";
        Assert.Equal(254, longest.Length);
        Assert.True(GaleristOptIn.IsPlausibleEmail(longest));
        var tooLong = longest + "s";
        foreach (var email in new[]
        {
            "", "plain", "@example.com", "name@", "name@example", "name@@example.com",
            "na me@example.com", "name@exa mple.com", "name@example.c", "name@example.123",
            "name@-example.com", "name@example-.com", "name@example..com", ".name@example.com",
            "name.@example.com", "na..me@example.com", "name@.example.com", "name@example.com.",
            "a@b@c.com", "name@exam_ple.com", tooLongLocal, tooLong,
            // ASCII only, like the server: IDNs go as "xn--…".
            "a@münchen.de", "ñame@example.com", "İsmail@example.com", "name@exämple.com",
            // No quoted local parts, IP literals or control characters.
            "\"name\"@example.com", "a@[127.0.0.1]", "a@127.0.0.1", "na\u0000me@example.com",
            "na\u0007me@example.com", "na(me)@example.com",
        })
        {
            Assert.False(GaleristOptIn.IsPlausibleEmail(email), email);
        }
        Assert.False(GaleristOptIn.IsPlausibleEmail(null));
        Assert.True(tooLong.Length > 254);
    }

    [Fact]
    public void ReportsTheLocalProblemBeforeSending()
    {
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, new GaleristSubscription("nope").LocalProblem);
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, new GaleristSubscription("").LocalProblem);
        Assert.Null(new GaleristSubscription("a@b.co").LocalProblem);
    }

    // Language

    [Fact]
    public void ExtractsTheTwoLetterPrimaryLanguage()
    {
        Assert.Equal("it", GaleristOptIn.PrimaryLanguage(["it-IT", "en-US"]));
        Assert.Equal("en", GaleristOptIn.PrimaryLanguage(["en"]));
        Assert.Equal("zh", GaleristOptIn.PrimaryLanguage(["zh-Hans-CN"]));
        Assert.Equal("pt", GaleristOptIn.PrimaryLanguage(["pt_BR"]));
        Assert.Equal("de", GaleristOptIn.PrimaryLanguage(["DE-de"]));
        Assert.Null(GaleristOptIn.PrimaryLanguage(["fil-PH", "en"])); // only the first counts
        Assert.Null(GaleristOptIn.PrimaryLanguage([""]));
        Assert.Null(GaleristOptIn.PrimaryLanguage([]));
    }

    // Base URL

    private static Dictionary<string, string> Env(string value) => new() { [GaleristOptIn.BaseUrlEnvironmentKey] = value };

    [Fact]
    public void UsesProductionUnlessOverridden()
    {
        Assert.Equal(GaleristOptIn.ProductionBaseUrl, GaleristOptIn.BaseUrl(new Dictionary<string, string>()));
        Assert.Equal(GaleristOptIn.ProductionBaseUrl, GaleristOptIn.BaseUrl(Env("")));
        Assert.Equal(GaleristOptIn.ProductionBaseUrl, GaleristOptIn.BaseUrl(Env("  ")));
    }

    [Theory]
    // .NET normalises the scheme, host and an empty path ("https://galerist.photo" → ".../").
    [InlineData("https://galerist.photo/", "https://galerist.photo/")]
    [InlineData("https://galerist.photo", "https://galerist.photo/")]
    [InlineData("HTTPS://GALERIST.PHOTO", "https://galerist.photo/")]
    [InlineData("http://127.0.0.1:3000", "http://127.0.0.1:3000/")]
    public void AcceptsOnlyGaleristHttpsAndLocalHttpOverrides(string raw, string expected)
    {
        Assert.Equal("LUMERIST_GALERIST_BASE_URL", GaleristOptIn.BaseUrlEnvironmentKey);
        Assert.Equal(expected, GaleristOptIn.BaseUrl(Env(raw))?.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://other.vercel.app")]
    [InlineData("http://192.168.1.2:3000")]
    [InlineData("ftp://galerist.photo")]
    [InlineData("galerist.photo")]
    [InlineData("https://")]
    [InlineData("https://user:pass@galerist.photo")]
    [InlineData("https://galerist.photo?x=1")]
    [InlineData("https://galerist.photo#x")]
    [InlineData("not a url")]
    // The form says the email goes to Galerist: no other host, ever.
    [InlineData("https://example.com")]
    [InlineData("https://galerist.photo.example.com")]
    [InlineData("https://evil-galerist.photo")]
    [InlineData("http://localhost:3000")]
    [InlineData("http://galerist.photo")]
    // Forms .NET would otherwise quietly repair.
    [InlineData("https:\\\\galerist.photo")]
    [InlineData("https://galerist.photo\\@example.com")]
    [InlineData("https://galerist.photo?")]
    [InlineData("/galerist.photo")]
    [InlineData("file:///galerist.photo")]
    public void RefusesOtherOverridesInsteadOfFallingBackToProduction(string raw) =>
        Assert.Null(GaleristOptIn.BaseUrl(Env(raw)));

    // Form (the privacy rules of the app's sign-up)

    [Fact]
    public void FormStartsEmpty()
    {
        var form = new GaleristSignUpForm();
        Assert.Equal("", form.Email);
        Assert.False(form.EmailLooksValid);
    }

    [Fact]
    public void FormPreparesTheRequestOnlyForAPlausibleEmail()
    {
        var form = new GaleristSignUpForm(" name@example.com ");
        var expected = new GaleristSubscription("name@example.com", "it", "0.9.0", new GaleristPrompt.Scheduled(1));
        Assert.Equal(new GaleristSignUpForm.Submission.Send(GaleristOptIn.ProductionBaseUrl, expected),
            form.Prepare(new Dictionary<string, string>(), ["it-IT"], "0.9.0", new GaleristPrompt.Scheduled(1)));
        form.Email = "name@example";
        Assert.Equal(new GaleristSignUpForm.Submission.Refuse(GaleristSubscriptionOutcome.InvalidEmail),
            form.Prepare(new Dictionary<string, string>(), [], "0.9.0", new GaleristPrompt.About()));
        form.Email = "";
        Assert.Equal(new GaleristSignUpForm.Submission.Refuse(GaleristSubscriptionOutcome.InvalidEmail),
            form.Prepare(new Dictionary<string, string>(), [], "0.9.0", null));
    }

    [Fact]
    public void FormNeverPreparesARequestForAnUnusableOverride()
    {
        var form = new GaleristSignUpForm("name@example.com");
        Assert.Equal(new GaleristSignUpForm.Submission.Refuse(GaleristSubscriptionOutcome.InvalidRequest),
            form.Prepare(Env("https://example.com"), ["en"], "0.9.0", new GaleristPrompt.Scheduled(1)));
    }

    [Fact]
    public void FormForgetsEverythingAfterASignUp()
    {
        var form = new GaleristSignUpForm("name@example.com");
        form.Reset();
        Assert.Equal(new GaleristSignUpForm(), form);
    }

    // Redirects and cookies

    [Fact]
    public void TheDefaultHandlerFollowsNoRedirectsAndKeepsNoCookies()
    {
        using var handler = GaleristSubscriptionClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseDefaultCredentials);
        Assert.Equal(TimeSpan.FromSeconds(15), GaleristSubscriptionClient.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(15), new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0").HttpTimeout);
        // A 3xx handed back (not followed) never counts as a sign-up.
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(307, ""));
    }

    [Theory]
    [InlineData("https://galerist.photo")]
    [InlineData("https://galerist.photo/")]
    [InlineData("http://127.0.0.1:3000")]
    public void BuildsTheEndpointFromTheBase(string baseUrl)
    {
        var client = new GaleristSubscriptionClient(new Uri(baseUrl), "0.9.0", new FakeHandler(200, "{\"ok\":true}"));
        Assert.EndsWith("/api/lumenkeys/subscribe", client.Endpoint.AbsoluteUri);
        Assert.DoesNotContain("//api", client.Endpoint.AbsoluteUri);
        Assert.Equal("https://galerist.photo/api/lumenkeys/subscribe",
            new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0", new FakeHandler(200, "")).Endpoint.AbsoluteUri);
        Assert.Equal("https://galerist.photo/base/api/lumenkeys/subscribe",
            new GaleristSubscriptionClient(new Uri("https://galerist.photo/base"), "0.9.0", new FakeHandler(200, "")).Endpoint.AbsoluteUri);
    }

    // Response → message

    private static GaleristSubscriptionOutcome Outcome(int status, string body) =>
        GaleristSubscriptionOutcomes.FromResponse(status, Encoding.UTF8.GetBytes(body));

    [Fact]
    public void MapsEveryContractResponse()
    {
        Assert.Equal(GaleristSubscriptionOutcome.Subscribed, Outcome(200, "{\"ok\":true}"));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, Outcome(400, "{\"ok\":false,\"error\":\"invalid_email\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.NoConsent, Outcome(400, "{\"ok\":false,\"error\":\"no_consent\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.UnknownConsentVersion, Outcome(400, "{\"ok\":false,\"error\":\"unknown_consent_version\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(400, "{\"ok\":false,\"error\":\"invalid_request\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.RateLimited, Outcome(429, "{\"ok\":false,\"error\":\"rate_limited\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(500, "{\"ok\":false,\"error\":\"server_error\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(502, "<html>Bad gateway</html>"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(503, ""));
    }

    [Fact]
    public void NeverReportsAnUnconfirmedSignUpAsSuccess()
    {
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, ""));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "<html>captive portal</html>"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "{\"ok\":false}"));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, Outcome(200, "{\"ok\":false,\"error\":\"invalid_email\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.Subscribed, Outcome(201, "{\"ok\":true}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(301, ""));
        // A field of the wrong type makes the whole reply unreadable, as on the Mac.
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "{\"ok\":1}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "{\"ok\":\"true\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "{\"ok\":true,\"error\":5}"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "[{\"ok\":true}]"));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, Outcome(200, "{\"ok\":true} {}"));
    }

    [Fact]
    public void MapsUnexpectedClientErrors()
    {
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(400, ""));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(400, "{\"ok\":false,\"error\":\"something_new\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(404, "Not found"));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(413, ""));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidRequest, Outcome(415, "{\"ok\":false,\"error\":\"invalid_request\"}"));
        Assert.Equal(GaleristSubscriptionOutcome.RateLimited, Outcome(429, ""));
    }

    [Fact]
    public void MessagesMatchTheContract()
    {
        Assert.Equal("Thanks, you're on the list.", GaleristSubscriptionOutcome.Subscribed.Message());
        Assert.Equal("That email address doesn't look right.", GaleristSubscriptionOutcome.InvalidEmail.Message());
        // v2 always sends both consents: "no_consent" only means a mismatch with the server.
        Assert.Equal("Something went wrong. Please try again later.", GaleristSubscriptionOutcome.NoConsent.Message());
        Assert.Equal("Please update Lumerist and try again.", GaleristSubscriptionOutcome.UnknownConsentVersion.Message());
        Assert.Equal("Something went wrong. Please try again later.", GaleristSubscriptionOutcome.InvalidRequest.Message());
        Assert.Equal("Too many attempts. Please try again later.", GaleristSubscriptionOutcome.RateLimited.Message());
        // The prompt doesn't introduce Galerist, so no message names it.
        Assert.Equal("Couldn't connect. Check your connection and try again.", GaleristSubscriptionOutcome.Unreachable.Message());
    }

    // Client (fake handler, never the network)

    /// <summary>Answers every request itself and records what it was sent.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;

        public FakeHandler(int status, string body) : this(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        })
        { }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        public List<(HttpRequestMessage Request, byte[] Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request, body));
            return _reply(request);
        }
    }

    private static readonly GaleristSubscription Valid = new("name@example.com", "en", "0.9.0", new GaleristPrompt.Scheduled(1));

    [Fact]
    public async Task PostsJsonWithTheContractHeaders()
    {
        var handler = new FakeHandler(200, "{\"ok\":true}");
        var client = new GaleristSubscriptionClient(new Uri("https://galerist.photo"), "0.9.0", handler);
        Assert.Equal(GaleristSubscriptionOutcome.Subscribed, await client.SubscribeAsync(Valid));
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://galerist.photo/api/lumenkeys/subscribe", request.RequestUri?.AbsoluteUri);
        Assert.Equal("application/json", request.Content?.Headers.ContentType?.ToString());
        Assert.Equal("application/json", request.Headers.Accept.ToString());
        Assert.Equal("Lumerist/0.9.0", string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Equal(Valid.Encoded(), body);
    }

    [Fact]
    public async Task TheBodySentMatchesTheServerContract()
    {
        // The Galerist server answers invalid_request unless both consents are
        // true and the prompt is a number 1–3 or "about".
        foreach (var (prompt, kind) in new (GaleristPrompt, JsonValueKind)[]
        {
            (new GaleristPrompt.Scheduled(1), JsonValueKind.Number),
            (new GaleristPrompt.Scheduled(2), JsonValueKind.Number),
            (new GaleristPrompt.Scheduled(3), JsonValueKind.Number),
            (new GaleristPrompt.About(), JsonValueKind.String),
        })
        {
            var handler = new FakeHandler(200, "{\"ok\":true}");
            var client = new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0", handler);
            await client.SubscribeAsync(new GaleristSubscription(" name@example.com ", "it", "0.9.0", prompt));
            using var document = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
            var sent = document.RootElement;
            Assert.Equal(JsonValueKind.Object, sent.ValueKind);
            Assert.Equal(new[] { "appVersion", "consentVersion", "email", "galeristNews", "language", "lumenkeysUpdates", "prompt" }, Keys(sent));
            Assert.Equal("name@example.com", sent.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.True, sent.GetProperty("lumenkeysUpdates").ValueKind);
            Assert.Equal(JsonValueKind.True, sent.GetProperty("galeristNews").ValueKind);
            Assert.Equal("lk-optin-v2", sent.GetProperty("consentVersion").GetString());
            Assert.Equal(JsonValueKind.String, sent.GetProperty("language").ValueKind);
            Assert.Equal("it", sent.GetProperty("language").GetString());
            Assert.Equal(JsonValueKind.String, sent.GetProperty("appVersion").ValueKind);
            Assert.Equal("0.9.0", sent.GetProperty("appVersion").GetString());
            var sentPrompt = sent.GetProperty("prompt");
            Assert.Equal(kind, sentPrompt.ValueKind);
            if (prompt is GaleristPrompt.Scheduled scheduled)
            {
                Assert.True(sentPrompt.TryGetInt32(out var number));
                Assert.Equal(scheduled.Number, number);
                Assert.InRange(number, 1, 3);
            }
            else
            {
                Assert.Equal("about", sentPrompt.GetString());
            }
        }
    }

    [Fact]
    public void UserAgentStaysOneToken()
    {
        Assert.Equal("Lumerist/development-build", new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "development build").UserAgent);
        Assert.Equal("Lumerist/unknown", new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "").UserAgent);
        Assert.Equal("Lumerist/0.9.0", new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, " 0.9.0\n").UserAgent);
    }

    [Theory]
    [InlineData(400, "{\"ok\":false,\"error\":\"invalid_email\"}", GaleristSubscriptionOutcome.InvalidEmail)]
    [InlineData(400, "{\"ok\":false,\"error\":\"no_consent\"}", GaleristSubscriptionOutcome.NoConsent)]
    [InlineData(400, "{\"ok\":false,\"error\":\"unknown_consent_version\"}", GaleristSubscriptionOutcome.UnknownConsentVersion)]
    [InlineData(400, "{\"ok\":false,\"error\":\"invalid_request\"}", GaleristSubscriptionOutcome.InvalidRequest)]
    [InlineData(429, "{\"ok\":false,\"error\":\"rate_limited\"}", GaleristSubscriptionOutcome.RateLimited)]
    [InlineData(500, "{\"ok\":false,\"error\":\"server_error\"}", GaleristSubscriptionOutcome.Unreachable)]
    [InlineData(307, "", GaleristSubscriptionOutcome.Unreachable)]
    public async Task MapsServerAnswersThroughTheClient(int status, string body, GaleristSubscriptionOutcome expected)
    {
        var handler = new FakeHandler(status, body);
        var client = new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0", handler);
        Assert.Equal(expected, await client.SubscribeAsync(Valid));
        Assert.Single(handler.Requests); // one request, never retried
    }

    [Fact]
    public async Task NetworkFailuresMeanUnreachable()
    {
        var offline = new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0",
            new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, await offline.SubscribeAsync(Valid));
        var timedOut = new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0",
            new FakeHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException())));
        Assert.Equal(GaleristSubscriptionOutcome.Unreachable, await timedOut.SubscribeAsync(Valid));
    }

    [Fact]
    public async Task SendsNothingWhenTheFormIsNotValid()
    {
        var handler = new FakeHandler(200, "{\"ok\":true}");
        var client = new GaleristSubscriptionClient(GaleristOptIn.ProductionBaseUrl, "0.9.0", handler);
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, await client.SubscribeAsync(new GaleristSubscription("nope")));
        Assert.Equal(GaleristSubscriptionOutcome.InvalidEmail, await client.SubscribeAsync(new GaleristSubscription("  ")));
        Assert.Empty(handler.Requests);
    }
}
