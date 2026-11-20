using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lumerist.Core;

/// <summary>
/// The optional "Stay in touch" prompt: texts, rules and links shared by the
/// app and its tests. <see cref="Copy.Message"/> and <see cref="Copy.Notice"/>
/// are shown to the user AND stored by Galerist as proof of consent under
/// <see cref="ConsentVersion"/>, so they must never change without a new
/// consent version agreed with the server (Shared/contract.json holds its hash).
/// <para>
/// Nothing here runs by itself: the only network call is
/// <see cref="GaleristSubscriptionClient.SubscribeAsync"/>, made when the user
/// has typed an email and presses "Keep me posted" (or Enter in the email field).
/// </para>
/// </summary>
public static class GaleristOptIn
{
    /// <summary>Consent version id; the server maps it to <see cref="Copy.Message"/> and <see cref="Copy.Notice"/>.</summary>
    public const string ConsentVersion = "lk-optin-v2";

    /// <summary>User-facing texts, character for character (straight ASCII apostrophes).</summary>
    public static class Copy
    {
        public const string Title = "Stay in touch";

        /// <summary>Consent text: the purpose (Lumerist news and the developer's other tools).</summary>
        public const string Message = "Leave your email to hear about new versions of Lumerist and about other photography tools from its developer.";

        public const string EmailPlaceholder = "you@example.com";

        /// <summary>Consent text, followed by the <see cref="PrivacyLink"/> (which names who holds the data).</summary>
        public const string Notice = "Optional and free. No spam: every email has a one-click unsubscribe.";

        public const string PrivacyLink = "Privacy";
        public const string Button = "Keep me posted";
        public const string NotNow = "Not now";
        public const string DontAskAgain = "Don't ask me again";
        public const string Success = "Thanks, you're on the list.";
        public const string Done = "Done";

        /// <summary>General → About &amp; Support.</summary>
        public const string AboutDetail = "News about Lumerist and other photography tools from its developer, by email.";

        public const string AboutButton = "Leave your email…";
        public const string AboutSubscribed = "You're on the list";

        /// <summary>App About section and README.</summary>
        public const string Credit = "Lumerist is made by the creator of Galerist, online galleries where photographers and videographers deliver their work to clients.";
    }

    /// <summary>Production server; tests may point elsewhere with <see cref="BaseUrlEnvironmentKey"/>.</summary>
    public static Uri ProductionBaseUrl { get; } = new("https://galerist.photo");

    public const string BaseUrlEnvironmentKey = "LUMERIST_GALERIST_BASE_URL";

    public static Uri PrivacyUrl { get; } = new("https://galerist.photo/en/legal/privacy#lumenkeys");

    /// <summary>
    /// The date of a confirmed sign-up, the only thing remembered about it (the
    /// email never is). Same key name as the Mac app.
    /// </summary>
    public const string SubscribedAtDefaultsKey = "lumerist.galerist.subscribed-at";

    /// <summary>Tracked short links https://galerist.photo/c/&lt;code&gt; (production only; they carry no personal data).</summary>
    public enum TrackedLink
    {
        /// <summary>Not shown by the app any more; the code stays valid on the server.</summary>
        Onboarding,
        Settings,
        Menu,
        Readme,
        Release,
    }

    public static string Code(this TrackedLink link) => link switch
    {
        TrackedLink.Onboarding => "lk-onboarding",
        TrackedLink.Settings => "lk-settings",
        TrackedLink.Menu => "lk-menu",
        TrackedLink.Readme => "lk-readme",
        TrackedLink.Release => "lk-release",
        _ => throw new ArgumentOutOfRangeException(nameof(link)),
    };

    public static Uri Url(this TrackedLink link) => new(ProductionBaseUrl, "c/" + link.Code());

    // Rules

    /// <summary>
    /// A local sanity check (the server checks syntax and the domain's DNS):
    /// ASCII only, like the server (an internationalised domain is written
    /// "xn--…"); one <c>@</c>; an unquoted local part of at most 64 characters; a
    /// dotted domain of letters, digits and hyphens whose top-level label is
    /// letters only or "xn--"; at most 254 characters.
    /// </summary>
    public static bool IsPlausibleEmail(string? raw)
    {
        var email = raw?.Trim() ?? "";
        if (email.Length is < 3 or > 254) return false;
        var parts = email.Split('@');
        if (parts.Length != 2) return false;
        var (local, domain) = (parts[0], parts[1]);
        if (local.Length is 0 or > 64 || !local.All(IsLocalPartCharacter)
            || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal)) return false;
        var labels = domain.Split('.');
        if (labels.Length < 2) return false;
        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63 || label.StartsWith('-') || label.EndsWith('-')
                || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        // Top-level label: letters only (so never an IP address), or "xn--".
        var topLevel = labels[^1];
        return topLevel.Length >= 2
            && (topLevel.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) || topLevel.All(char.IsAsciiLetter));
    }

    /// <summary>RFC 5322 "atext" plus the dot: no quotes, spaces, brackets or non-ASCII.</summary>
    private static bool IsLocalPartCharacter(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~.".Contains(c);

    /// <summary>
    /// Two-letter lowercase primary language of the first preferred language
    /// ("it-IT" → "it", "zh-Hans-CN" → "zh"); null when there is none or it is
    /// not a two-letter code (e.g. "fil").
    /// </summary>
    public static string? PrimaryLanguage(IEnumerable<string> preferredLanguages)
    {
        var first = preferredLanguages.FirstOrDefault();
        if (first is null) return null;
        var code = first.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        return IsLanguageCode(code) ? code : null;
    }

    internal static bool IsLanguageCode(string code) => code.Length == 2 && code.All(c => c is >= 'a' and <= 'z');

    /// <summary>
    /// Hosts a test override may point at over https: the Privacy link says who
    /// receives the email, so an environment variable must never be able to
    /// send it anywhere else.
    /// </summary>
    internal static readonly IReadOnlySet<string> OverrideHosts = new HashSet<string>(StringComparer.Ordinal)
    {
        // To test against another Galerist server over https, add its host here in a local build only.
        "galerist.photo",
    };

    /// <summary>
    /// Production unless LUMERIST_GALERIST_BASE_URL is set. An override must be
    /// https to a Galerist host, or http to 127.0.0.1 for a local server;
    /// anything else returns null so a mistyped test setting never falls back
    /// to the production server.
    /// </summary>
    public static Uri? BaseUrl(IReadOnlyDictionary<string, string> environment)
    {
        if (!environment.TryGetValue(BaseUrlEnvironmentKey, out var value)) return ProductionBaseUrl;
        var raw = value.Trim();
        return raw.Length == 0 ? ProductionBaseUrl : ValidatedOverride(raw);
    }

    /// <summary>The variables <see cref="BaseUrl"/> reads, taken from this process.</summary>
    public static IReadOnlyDictionary<string, string> ProcessEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Environment.GetEnvironmentVariable(BaseUrlEnvironmentKey) is { } value) environment[BaseUrlEnvironmentKey] = value;
        return environment;
    }

    internal static Uri? ValidatedOverride(string raw)
    {
        // No query, fragment, user info, spaces or backslashes (which .NET would
        // quietly turn into slashes).
        if (raw.Any(c => c is '?' or '#' or '@' or '\\' || char.IsWhiteSpace(c) || char.IsControl(c))) return null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.IsFile || url.IsUnc) return null;
        var host = url.Host.ToLowerInvariant();
        if (host.Length == 0 || url.UserInfo.Length != 0) return null;
        return url.Scheme.ToLowerInvariant() switch
        {
            "https" when OverrideHosts.Contains(host) => url,
            "http" when host == "127.0.0.1" => url,
            _ => null,
        };
    }
}

/// <summary>Which prompt a sign-up came from. Sent as <c>1</c>, <c>2</c>, <c>3</c> or <c>"about"</c>.</summary>
public abstract record GaleristPrompt
{
    private GaleristPrompt() { }

    /// <summary>The scheduled prompt's number (1–3, <see cref="StayInTouchSchedule.MaximumPrompts"/>).</summary>
    public sealed record Scheduled(int Number) : GaleristPrompt;

    /// <summary>The button in About &amp; Support.</summary>
    public sealed record About() : GaleristPrompt;

    internal bool IsValid => this switch
    {
        Scheduled scheduled => scheduled.Number is >= 1 and <= StayInTouchSchedule.MaximumPrompts,
        _ => true,
    };
}

/// <summary>
/// The JSON body of <c>POST /api/lumenkeys/subscribe</c> (contract lk-optin-v2).
/// The v2 text covers both Lumerist news and the developer's other tools, so
/// lumenkeysUpdates and galeristNews are always sent as true. Optional fields
/// are omitted when null.
/// </summary>
public sealed record GaleristSubscription
{
    public string Email { get; }
    public string? Language { get; }
    public string? AppVersion { get; }
    public GaleristPrompt? Prompt { get; }
    public string ConsentVersion { get; }

    /// <summary>Always true in v2: the text the user agreed to names Lumerist news.</summary>
    public bool LumenkeysUpdates => true;

    /// <summary>Always true in v2: the text names the developer's other photography tools.</summary>
    public bool GaleristNews => true;

    /// <summary>
    /// Trims the email; drops a language that is not a two-letter lowercase
    /// code, an app version that is empty or longer than 32 characters, and a
    /// prompt number outside 1–3.
    /// </summary>
    public GaleristSubscription(string email, string? language = null, string? appVersion = null,
                                GaleristPrompt? prompt = null, string consentVersion = GaleristOptIn.ConsentVersion)
    {
        Email = email.Trim();
        Language = language is not null && GaleristOptIn.IsLanguageCode(language) ? language : null;
        AppVersion = appVersion is not null && new StringInfo(appVersion).LengthInTextElements is >= 1 and <= 32 ? appVersion : null;
        Prompt = prompt is { IsValid: true } ? prompt : null;
        ConsentVersion = consentVersion;
    }

    /// <summary>The local reason the server would refuse it, if any.</summary>
    public GaleristSubscriptionOutcome? LocalProblem =>
        GaleristOptIn.IsPlausibleEmail(Email) ? null : GaleristSubscriptionOutcome.InvalidEmail;

    /// <summary>UTF-8 JSON with sorted keys and unescaped slashes, byte for byte what the Mac app sends.</summary>
    public byte[] Encoded()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            if (AppVersion is not null) writer.WriteString("appVersion", AppVersion);
            writer.WriteString("consentVersion", ConsentVersion);
            writer.WriteString("email", Email);
            writer.WriteBoolean("galeristNews", GaleristNews);
            if (Language is not null) writer.WriteString("language", Language);
            writer.WriteBoolean("lumenkeysUpdates", LumenkeysUpdates);
            switch (Prompt)
            {
                case GaleristPrompt.Scheduled scheduled: writer.WriteNumber("prompt", scheduled.Number); break;
                case GaleristPrompt.About: writer.WriteString("prompt", "about"); break;
            }
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }
}

/// <summary>What the server (or the network) answered.</summary>
public enum GaleristSubscriptionOutcome
{
    Subscribed,
    InvalidEmail,
    /// <summary>The server's no_consent. The v2 body always carries both consents, so this only means the app and the server disagree.</summary>
    NoConsent,
    UnknownConsentVersion,
    InvalidRequest,
    RateLimited,
    Unreachable,
}

public static class GaleristSubscriptionOutcomes
{
    /// <summary>The message the app shows.</summary>
    public static string Message(this GaleristSubscriptionOutcome outcome) => outcome switch
    {
        GaleristSubscriptionOutcome.Subscribed => GaleristOptIn.Copy.Success,
        GaleristSubscriptionOutcome.InvalidEmail => "That email address doesn't look right.",
        GaleristSubscriptionOutcome.NoConsent or GaleristSubscriptionOutcome.InvalidRequest => "Something went wrong. Please try again later.",
        GaleristSubscriptionOutcome.UnknownConsentVersion => "Please update Lumerist and try again.",
        GaleristSubscriptionOutcome.RateLimited => "Too many attempts. Please try again later.",
        _ => "Couldn't connect. Check your connection and try again.",
    };

    /// <summary>
    /// Maps an HTTP answer. Success needs a 2xx with <c>{"ok":true}</c>; anything
    /// that does not confirm the sign-up is never reported as one.
    /// </summary>
    public static GaleristSubscriptionOutcome FromResponse(int statusCode, ReadOnlySpan<byte> body)
    {
        var reply = Reply.Decode(body);
        return statusCode switch
        {
            >= 200 and < 300 when reply?.Ok == true => GaleristSubscriptionOutcome.Subscribed,
            >= 200 and < 300 => FromErrorCode(reply?.Error) ?? GaleristSubscriptionOutcome.Unreachable,
            429 => GaleristSubscriptionOutcome.RateLimited,
            >= 400 and < 500 => FromErrorCode(reply?.Error) ?? GaleristSubscriptionOutcome.InvalidRequest,
            // 5xx, and anything unexpected (1xx, 3xx).
            _ => GaleristSubscriptionOutcome.Unreachable,
        };
    }

    /// <summary>The error codes of the contract.</summary>
    internal static GaleristSubscriptionOutcome? FromErrorCode(string? code) => code switch
    {
        "invalid_email" => GaleristSubscriptionOutcome.InvalidEmail,
        "no_consent" => GaleristSubscriptionOutcome.NoConsent,
        "unknown_consent_version" => GaleristSubscriptionOutcome.UnknownConsentVersion,
        "invalid_request" => GaleristSubscriptionOutcome.InvalidRequest,
        "rate_limited" => GaleristSubscriptionOutcome.RateLimited,
        "server_error" => GaleristSubscriptionOutcome.Unreachable,
        _ => null,
    };

    /// <summary>
    /// <c>{"ok": bool?, "error": string?}</c>; null when the body is not such an
    /// object (a field of another type makes the whole reply unreadable, as on the Mac).
    /// </summary>
    private sealed record Reply(bool? Ok, string? Error)
    {
        public static Reply? Decode(ReadOnlySpan<byte> body)
        {
            try
            {
                var reader = new Utf8JsonReader(body);
                using var document = JsonDocument.ParseValue(ref reader);
                if (reader.Read()) return null; // trailing data
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                bool? ok = null;
                string? error = null;
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("ok"))
                    {
                        switch (property.Value.ValueKind)
                        {
                            case JsonValueKind.True: ok = true; break;
                            case JsonValueKind.False: ok = false; break;
                            case JsonValueKind.Null: ok = null; break;
                            default: return null;
                        }
                    }
                    else if (property.NameEquals("error"))
                    {
                        switch (property.Value.ValueKind)
                        {
                            case JsonValueKind.String: error = property.Value.GetString(); break;
                            case JsonValueKind.Null: error = null; break;
                            default: return null;
                        }
                    }
                }
                return new Reply(ok, error);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// What the user typed in the prompt. Pure, so the privacy rules are testable:
/// nothing is kept after a confirmed sign-up, and no request is ever prepared
/// for an implausible email or when the test override is unusable.
/// </summary>
public sealed record GaleristSignUpForm(string Email = "")
{
    public string Email { get; set; } = Email;

    public bool EmailLooksValid => GaleristOptIn.IsPlausibleEmail(Email);

    /// <summary>The request to send, or the outcome to show instead of sending.</summary>
    public abstract record Submission
    {
        private Submission() { }

        public sealed record Send(Uri BaseUrl, GaleristSubscription Subscription) : Submission;

        public sealed record Refuse(GaleristSubscriptionOutcome Outcome) : Submission;
    }

    public Submission Prepare(IReadOnlyDictionary<string, string> environment, IEnumerable<string> preferredLanguages,
                              string appVersion, GaleristPrompt? prompt)
    {
        var subscription = new GaleristSubscription(Email, GaleristOptIn.PrimaryLanguage(preferredLanguages), appVersion, prompt);
        if (subscription.LocalProblem is { } problem) return new Submission.Refuse(problem);
        // A test override that is not usable: never fall back to production.
        if (GaleristOptIn.BaseUrl(environment) is not { } baseUrl) return new Submission.Refuse(GaleristSubscriptionOutcome.InvalidRequest);
        return new Submission.Send(baseUrl, subscription);
    }

    /// <summary>After a confirmed sign-up nothing typed is kept.</summary>
    public void Reset() => Email = "";
}

/// <summary>
/// Sends one sign-up to Galerist. The handler is injectable so tests never
/// touch the network; the default one keeps no cookies and follows no redirects.
/// </summary>
public sealed class GaleristSubscriptionClient
{
    public const string EndpointPath = "api/lumenkeys/subscribe";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public Uri BaseUrl { get; }
    public string AppVersion { get; }
    private readonly HttpClient _http;

    /// <param name="handler">For tests; null uses a shared client built on <see cref="CreateHandler"/>.</param>
    public GaleristSubscriptionClient(Uri baseUrl, string appVersion, HttpMessageHandler? handler = null)
    {
        BaseUrl = baseUrl;
        AppVersion = appVersion;
        _http = handler is null ? SharedClient.Value : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout };
    }

    internal TimeSpan HttpTimeout => _http.Timeout;

    public Uri Endpoint
    {
        get
        {
            var text = BaseUrl.AbsoluteUri;
            var directory = text.EndsWith('/') ? BaseUrl : new Uri(text + "/");
            return new Uri(directory, EndpointPath);
        }
    }

    /// <summary>Lumerist/&lt;version&gt;, spaces replaced so the header stays one token.</summary>
    public string UserAgent
    {
        get
        {
            var version = string.Join("-", AppVersion.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return "Lumerist/" + (version.Length == 0 ? "unknown" : version);
        }
    }

    public HttpRequestMessage CreateRequest(GaleristSubscription subscription)
    {
        var content = new ByteArrayContent(subscription.Encoded());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    /// <summary>
    /// Checks locally first (no request for an implausible email), then posts
    /// once. Every failure maps to one of the contract's outcomes.
    /// </summary>
    public async Task<GaleristSubscriptionOutcome> SubscribeAsync(GaleristSubscription subscription, CancellationToken cancellationToken = default)
    {
        if (subscription.LocalProblem is { } problem) return problem;
        HttpRequestMessage request;
        try
        {
            request = CreateRequest(subscription);
        }
        catch (Exception)
        {
            return GaleristSubscriptionOutcome.InvalidRequest;
        }
        try
        {
            using (request)
            using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return GaleristSubscriptionOutcomes.FromResponse((int)response.StatusCode, body);
            }
        }
        catch (Exception)
        {
            return GaleristSubscriptionOutcome.Unreachable;
        }
    }

    /// <summary>
    /// No cookies kept or sent, and no redirects followed: a redirect would
    /// resend the POST, email included, to whatever host the Location names.
    /// A 3xx is handed back instead and maps to Unreachable.
    /// </summary>
    internal static HttpClientHandler CreateHandler() => new()
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        UseDefaultCredentials = false,
        PreAuthenticate = false,
    };

    /// <summary>Created on first use.</summary>
    private static readonly Lazy<HttpClient> SharedClient = new(() => new HttpClient(CreateHandler()) { Timeout = Timeout });
}
