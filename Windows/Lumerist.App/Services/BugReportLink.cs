using System.Globalization;
using System.Runtime.InteropServices;
using Lumerist.Core;
using Microsoft.Win32;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Services;

/// <summary>
/// "Report a problem" (the Mac's SupportLinks.bugReport): GitHub's bug form
/// with the version, Windows, the kind of PC and the keyboard filled in.
/// Built on click and opened in the browser, where the person sees everything
/// before sending; Lumerist itself sends nothing.
/// </summary>
internal static class BugReportLink
{
    public static Uri Build() => BugReport.Url(new BugReport.Details(
        AppController.AppVersion,
        BugReport.WindowsDescription(Environment.OSVersion.Version.Build, DisplayVersion()),
        // The system's architecture: an x64 build running on an Arm PC still says Arm.
        BugReport.Computer(isMac: false, isArm: RuntimeInformation.OSArchitecture == Architecture.Arm64),
        KeyboardLanguage()));

    /// <summary>"24H2", or null when the registry does not say.</summary>
    private static string? DisplayVersion()
    {
        try
        {
            return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string;
        }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The language of the current keyboard layout ("Italian (Italy)"), or "" when unknown.</summary>
    private static string KeyboardLanguage()
    {
        var languageId = (int)(GetKeyboardLayout(0) & 0xFFFF);
        if (languageId == 0) return "";
        try
        {
            return CultureInfo.GetCultureInfo(languageId).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return "";
        }
    }
}
