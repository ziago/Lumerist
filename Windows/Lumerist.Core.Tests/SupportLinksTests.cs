using Lumerist.Core;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>The links the app shows: the same addresses as the Mac's SupportLinks.swift.</summary>
public class SupportLinksTests
{
    [Fact]
    public void ProjectLinksMatchTheMac()
    {
        Assert.Equal("https://github.com/ziago/Lumerist", SupportLinks.Repository.OriginalString);
        Assert.Equal("https://github.com/ziago/Lumerist/releases", SupportLinks.Releases.OriginalString);
        Assert.Equal("https://github.com/ziago/Lumerist/issues/new/choose", SupportLinks.Issues.OriginalString);
        Assert.Equal("https://github.com/ziago/Lumerist/discussions", SupportLinks.Discussions.OriginalString);
        Assert.Equal("https://github.com/ziago/Lumerist/blob/main/LICENSE", SupportLinks.License.OriginalString);
        Assert.Equal("https://api.github.com/repos/ziago/Lumerist/releases?per_page=20", SupportLinks.ReleasesApi.OriginalString);
    }

    [Fact]
    public void GaleristLinksAreTheTrackedShortLinks()
    {
        Assert.Equal("https://galerist.photo/c/lk-settings", SupportLinks.GaleristFromSettings.AbsoluteUri);
        Assert.Equal("https://galerist.photo/c/lk-menu", SupportLinks.GaleristFromMenu.AbsoluteUri);
        Assert.Equal("https://galerist.photo/en/legal/privacy#lumenkeys", SupportLinks.GaleristPrivacy.AbsoluteUri);
    }

    [Fact]
    public void DonationButtonsLinkTheAccountsThatExist()
    {
        var donations = SupportLinks.Donations;
        Assert.Equal(["coffee", "kofi", "paypal", "sponsors"], donations.Select(donation => donation.Id));
        Assert.Equal(["Buy me a coffee", "Ko-fi", "PayPal", "GitHub Sponsors"], donations.Select(donation => donation.Title));
        Assert.Equal(["Buy Me a Coffee", "A one-off tip", "Send a pizza", "Support on GitHub"], donations.Select(donation => donation.Detail));
        // The same accounts as on the Mac; a service without one has no button.
        Assert.Equal(["https://buymeacoffee.com/lumerist", null, null, null],
            donations.Select(donation => donation.Url?.AbsoluteUri));
        Assert.Equal(["coffee"], SupportLinks.AvailableDonations.Select(donation => donation.Id));
    }

    [Fact]
    public void DonationLinksAreBuiltFromTheAccounts()
    {
        var donations = SupportLinks.DonationsFor("maker", "maker2", "maker3", "maker4");
        Assert.Equal(
            ["https://buymeacoffee.com/maker", "https://ko-fi.com/maker2", "https://paypal.me/maker3", "https://github.com/sponsors/maker4"],
            donations.Select(donation => donation.Url!.AbsoluteUri));
        Assert.Null(SupportLinks.DonationsFor(null, "x", null, null)[0].Url);
        Assert.NotNull(SupportLinks.DonationsFor(null, "x", null, null)[1].Url);
    }
}

/// <summary>Dates as the Mac shows them in English.</summary>
public class DisplayDatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 15, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void AbbreviatedDate()
    {
        Assert.Equal("Nov 20, 2026", DisplayDates.Abbreviated(Now));
        Assert.Equal("Jan 2, 2027", DisplayDates.Abbreviated(new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(-30, "30 seconds ago")]
    [InlineData(-1, "1 second ago")]
    [InlineData(-60, "1 minute ago")]
    [InlineData(-150, "2 minutes ago")]
    [InlineData(-7_200, "2 hours ago")]
    [InlineData(-86_400, "yesterday")]
    [InlineData(-86_400 - 3_600 * 5, "yesterday")]
    [InlineData(-86_400 * 3, "3 days ago")]
    [InlineData(-86_400 * 7, "last week")]
    [InlineData(-86_400 * 15, "2 weeks ago")]
    [InlineData(86_400, "tomorrow")]
    [InlineData(7_200, "in 2 hours")]
    [InlineData(45, "in 45 seconds")]
    public void RelativeNamed(int seconds, string expected)
    {
        Assert.Equal(expected, DisplayDates.RelativeNamed(Now.AddSeconds(seconds), Now));
    }

    [Fact]
    public void RelativeNamedUsesCalendarMonthsAndYears()
    {
        Assert.Equal("last month", DisplayDates.RelativeNamed(new DateTimeOffset(2026, 10, 20, 15, 30, 0, Now.Offset), Now));
        Assert.Equal("2 years ago", DisplayDates.RelativeNamed(new DateTimeOffset(2024, 11, 1, 0, 0, 0, Now.Offset), Now));
        Assert.Equal("next year", DisplayDates.RelativeNamed(new DateTimeOffset(2027, 12, 1, 0, 0, 0, Now.Offset), Now));
        // Another offset is read on now's clock.
        Assert.Equal("1 hour ago", DisplayDates.RelativeNamed(new DateTimeOffset(2026, 11, 20, 13, 30, 0, TimeSpan.Zero), Now));
    }
}

/// <summary>The status lines of the tray menu (the Mac's StatusMenu).</summary>
public class StatusMenuTextTests
{
    [Fact]
    public void LinkLine()
    {
        Assert.Equal("Lightroom linked", StatusMenuText.Link(true));
        Assert.Equal("Waiting for Lightroom", StatusMenuText.Link(false));
    }

    [Fact]
    public void ContextLine()
    {
        Assert.Equal("Plug-in not connected", StatusMenuText.Context(false, new LightroomContext("develop", "")));
        Assert.Equal("Develop", StatusMenuText.Context(true, new LightroomContext("develop", "")));
        Assert.Equal("Develop · mask selected", StatusMenuText.Context(true, new LightroomContext("develop", "masking")));
        Assert.Equal("Library", StatusMenuText.Context(true, new LightroomContext("library", "masking")));
        Assert.Equal("—", StatusMenuText.Context(true, new LightroomContext("", "")));
    }

    [Fact]
    public void CapitalizedLikeSwift()
    {
        Assert.Equal("Slideshow", StatusMenuText.Capitalized("SLIDESHOW"));
        Assert.Equal("Two Words", StatusMenuText.Capitalized("two wORDS"));
    }
}
