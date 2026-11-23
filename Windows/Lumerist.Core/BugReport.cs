namespace Lumerist.Core;

/// <summary>
/// "Report a problem": GitHub's bug report form (.github/ISSUE_TEMPLATE/bug_report.yml)
/// opened in the browser with what Lumerist already knows filled in (the Mac's
/// BugReport.swift; the links are pinned down in Shared/fixtures/bug-report).
/// </summary>
/// <remarks>
/// Lumerist itself sends nothing: the person reads the form, says what
/// happened and creates the issue from their GitHub account.
/// </remarks>
public static class BugReport
{
    /// <summary>The fields Lumerist fills in, named by their id in bug_report.yml.</summary>
    public sealed record Details(string Lumerist, string System, string Computer, string Keyboard);

    internal const string Form = "https://github.com/ziago/Lumerist/issues/new";
    internal const string Template = "bug_report.yml";

    /// <summary>The form's address with the details filled in. A blank detail is left out, so the form shows its placeholder there.</summary>
    public static Uri Url(Details details)
    {
        (string Name, string Value)[] fields =
        [
            ("template", Template), ("lumerist", details.Lumerist), ("system", details.System),
            ("computer", details.Computer), ("keyboard", details.Keyboard),
        ];
        var query = fields
            .Select(field => (field.Name, Value: (field.Value ?? "").Trim()))
            .Where(field => field.Value.Length > 0)
            .Select(field => $"{field.Name}={Uri.EscapeDataString(field.Value)}");
        return new Uri(Form + "?" + string.Join("&", query));
    }

    /// <summary>The "Computer" field, worded as in the form's placeholder.</summary>
    public static string Computer(bool isMac, bool isArm) => (isMac, isArm) switch
    {
        (true, true) => "Mac with Apple Silicon",
        (true, false) => "Mac with Intel",
        (false, true) => "Windows PC (Arm)",
        (false, false) => "Windows PC (x64)",
    };

    /// <summary>"Windows 11 24H2 (build 26100)": Windows 11 from build 22000; the release name when the registry has one.</summary>
    public static string WindowsDescription(int build, string? displayVersion)
    {
        var name = build >= 22000 ? "Windows 11" : "Windows 10";
        var release = string.IsNullOrWhiteSpace(displayVersion) ? "" : " " + displayVersion.Trim();
        return $"{name}{release} (build {build})";
    }
}
