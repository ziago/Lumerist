using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// "Report a problem": Shared/fixtures/bug-report (the same links as the Mac's
/// SharedFixtureTests.bugReportLinks), plus the Windows-only system line.
/// </summary>
public class BugReportTests
{
    [Fact]
    public void Links() => Fixtures.Run("bug-report", Fixtures.Cases("bug-report"), (testCase, _) => Run(testCase));

    private static IEnumerable<string> Run(JsonObject testCase)
    {
        if (testCase["computers"] is JsonArray computers)
        {
            foreach (var row in computers.Select(node => node!.AsObject()))
            {
                bool isMac = row["isMac"]!.GetValue<bool>(), isArm = row["isArm"]!.GetValue<bool>();
                var name = BugReport.Computer(isMac, isArm);
                if (name != row["name"]!.GetValue<string>()) yield return $"Computer(isMac: {isMac}, isArm: {isArm}) = {name}";
            }
            yield break;
        }
        var details = testCase["details"]?.AsObject() ?? throw new FixtureException("a bug-report case needs details and url, or computers");
        string Field(string name) => details[name]?.GetValue<string>() ?? "";
        var url = BugReport.Url(new BugReport.Details(Field("lumerist"), Field("system"), Field("computer"), Field("keyboard")));
        var expected = testCase["url"]?.GetValue<string>() ?? throw new FixtureException("no url");
        if (url.AbsoluteUri != expected) yield return "url " + url.AbsoluteUri;
    }

    [Theory]
    [InlineData(26100, "24H2", "Windows 11 24H2 (build 26100)")]
    [InlineData(22000, null, "Windows 11 (build 22000)")]
    [InlineData(19045, "22H2", "Windows 10 22H2 (build 19045)")]
    [InlineData(19045, "  ", "Windows 10 (build 19045)")]
    public void WindowsDescription(int build, string? displayVersion, string expected) =>
        Assert.Equal(expected, BugReport.WindowsDescription(build, displayVersion));
}
