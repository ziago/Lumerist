using Microsoft.Win32;

namespace Lumerist.App.Views;

/// <summary>
/// General → Open at Login (the Mac's login item): the value <c>Lumerist</c>
/// under HKCU\Software\Microsoft\Windows\CurrentVersion\Run, holding the
/// quoted path of Lumerist.exe. The uninstaller removes the same value.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumerist";

    /// <summary>Where Task Manager's Startup tab records an entry the user turned off.</summary>
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string? Command => Environment.ProcessPath is { Length: > 0 } exe ? $"\"{exe}\"" : null;

    /// <summary>
    /// Read live from the registry, like the Mac reads its login item state: on
    /// only when the value starts this copy (an app moved since starts nothing)
    /// and Task Manager has not turned it off.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                if (key?.GetValue(ValueName) is not string value || Command is not { } command) return false;
                if (!string.Equals(value, command, StringComparison.OrdinalIgnoreCase)) return false;
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                // 12 bytes; an odd first byte (0x03, 0x07) means disabled in Task Manager.
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Turns starting at sign-in on or off. Returns null, or why it could not be changed.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                if (Command is not { } command) return "Windows did not say where Lumerist.exe is.";
                key.SetValue(ValueName, command, RegistryValueKind.String);
                // Turning it on here also undoes "Disable" in Task Manager.
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return null;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return error.Message;
        }
    }
}
