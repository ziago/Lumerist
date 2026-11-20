namespace Lumerist.Core;

/// <summary>
/// Every external link the app shows, in one place (the Mac's SupportLinks.swift).
/// </summary>
/// <remarks>
/// Donation accounts: set each username below once the account exists. Until
/// then the matching button stays disabled with "coming soon".
/// </remarks>
public static class SupportLinks
{
    public static Uri Repository { get; } = new("https://github.com/ziago/Lumerist");

    public static Uri Releases { get; } = new("https://github.com/ziago/Lumerist/releases");

    public static Uri Issues { get; } = new("https://github.com/ziago/Lumerist/issues/new/choose");

    public static Uri Discussions { get; } = new("https://github.com/ziago/Lumerist/discussions");

    public static Uri License { get; } = new("https://github.com/ziago/Lumerist/blob/main/LICENSE");

    /// <summary>The public release feed the update check reads. Includes pre-releases (the <c>/releases/latest</c> endpoint skips them).</summary>
    public static Uri ReleasesApi { get; } = new("https://api.github.com/repos/ziago/Lumerist/releases?per_page=20");

    // Galerist (the maker's photo-delivery site). Tracked short links that
    // only count the visit: they carry no personal data. Opened in the browser
    // on click; Lumerist itself sends nothing.

    /// <summary>About &amp; Support.</summary>
    public static Uri GaleristFromSettings => GaleristOptIn.TrackedLink.Settings.Url();

    /// <summary>The quiet item in the tray menu (the Mac's menu bar window).</summary>
    public static Uri GaleristFromMenu => GaleristOptIn.TrackedLink.Menu.Url();

    /// <summary>The "Privacy" link in the "Stay in touch" notice.</summary>
    public static Uri GaleristPrivacy => GaleristOptIn.PrivacyUrl;

    // Donations: PLACEHOLDERS until the owner creates the accounts (the same
    // as on the Mac).

    /// <summary>PLACEHOLDER: Buy Me a Coffee username (buymeacoffee.com/&lt;username&gt;).</summary>
    public const string? BuyMeACoffeeUsername = null;

    /// <summary>PLACEHOLDER: Ko-fi username (ko-fi.com/&lt;username&gt;).</summary>
    public const string? KoFiUsername = null;

    /// <summary>PLACEHOLDER: PayPal.me name (paypal.me/&lt;name&gt;).</summary>
    public const string? PayPalMeName = null;

    /// <summary>PLACEHOLDER: GitHub Sponsors account (github.com/sponsors/&lt;account&gt;), usable once the Sponsors profile is approved.</summary>
    public const string? GitHubSponsorsAccount = null;

    /// <summary>One donation button: <see cref="Url"/> is null until the account exists.</summary>
    /// <param name="Id">"coffee", "kofi", "paypal" or "sponsors" (the app picks the icon by it).</param>
    public sealed record Donation(string Id, string Title, string Detail, Uri? Url);

    public static IReadOnlyList<Donation> Donations =>
        DonationsFor(BuyMeACoffeeUsername, KoFiUsername, PayPalMeName, GitHubSponsorsAccount);

    /// <summary>The donation buttons for the given accounts (null: not created yet).</summary>
    public static IReadOnlyList<Donation> DonationsFor(string? coffee, string? kofi, string? payPal, string? sponsors) =>
    [
        new("coffee", "Buy me a coffee", "Buy Me a Coffee", Link("https://buymeacoffee.com/", coffee)),
        new("kofi", "Ko-fi", "A one-off tip", Link("https://ko-fi.com/", kofi)),
        new("paypal", "PayPal", "Send a pizza", Link("https://paypal.me/", payPal)),
        new("sponsors", "GitHub Sponsors", "Support on GitHub", Link("https://github.com/sponsors/", sponsors)),
    ];

    /// <summary>The account's page, or null when there is no account or the link would not parse (as on the Mac).</summary>
    internal static Uri? Link(string prefix, string? account) =>
        account is not null && Uri.TryCreate(prefix + account, UriKind.Absolute, out var url) ? url : null;
}
