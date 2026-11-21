using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

// %APPDATA%\Lumerist\plugin-link.json, read by OpenApp.lua. The rejected
// examples are the ones in Tests/Lua/run.lua ("a link file that is unreadable
// or names another program is ignored").

public sealed class PluginLinkTests : IDisposable
{
    private const string WindowsExe = "C:\\Users\\Test\\AppData\\Local\\Programs\\Lumerist\\Lumerist.exe";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "lumerist-link-" + Guid.NewGuid().ToString("N"), "Lumerist");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The Lua tests' linkJSON helper.</summary>
    private static string LinkJson(string exe, bool? openWithLightroom = null)
    {
        var text = "{\"exe\":\"" + exe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (openWithLightroom is { } open) text += ",\"openWithLightroom\":" + (open ? "true" : "false");
        return text + "}";
    }

    [Fact]
    public void UsesTheContractFileName()
    {
        Assert.Equal("plugin-link.json", Contract.PluginLinkFileName);
        Assert.Equal(Path.Combine(folder, "plugin-link.json"), PluginLink.PathIn(folder));
    }

    [Fact]
    public void AcceptsANormalInstallPath()
    {
        Assert.True(PluginLink.IsAcceptedByPlugin(WindowsExe));
        Assert.True(PluginLink.IsAcceptedByPlugin("D:\\Apps\\LUMERIST.EXE"));
        Assert.True(PluginLink.IsAcceptedByPlugin("c:\\Lumerist.exe"));
        Assert.True(PluginLink.IsAcceptedByPlugin("C:\\Users\\Zoë Müller\\AppData\\Local\\Programs\\Lumerist\\Lumerist.exe"));
        Assert.True(PluginLink.IsAcceptedByPlugin("C:\\Program Files (x86)\\A & B\\Lumerist.exe"));
    }

    [Theory]
    [InlineData("Lumerist.exe")]
    [InlineData("C:\\Tools\\other.exe")]
    [InlineData("C:\\Lumerist\" & calc & \".exe\\Lumerist.exe")]
    [InlineData("C:\\%APPDATA%\\Lumerist.exe")]
    [InlineData("C:\\Lumerist^\\Lumerist.exe")]
    // More cases of the same three rules.
    [InlineData("")]
    [InlineData("C:")]
    [InlineData("C:Lumerist.exe")]
    [InlineData("C:/Programs/Lumerist.exe")]
    [InlineData("C:\\Programs/Lumerist.exe")]
    [InlineData("\\\\server\\share\\Lumerist.exe")]
    [InlineData("1:\\Lumerist.exe")]
    [InlineData("É:\\Lumerist.exe")]
    [InlineData("C:\\Lumerist.exe\\")]
    [InlineData("C:\\Lumerist.exe ")]
    [InlineData("C:\\NotLumerist.exe")]
    [InlineData("C:\\Lumerist.exe\n")]
    [InlineData("C:\\a\tb\\Lumerist.exe")]
    [InlineData("C:\\a\u007fb\\Lumerist.exe")]
    [InlineData("C:\\a\u0000b\\Lumerist.exe")]
    public void RejectsWhatThePluginRejects(string exe)
    {
        Assert.False(PluginLink.IsAcceptedByPlugin(exe), exe);
        Assert.False(new PluginLink(exe).IsAccepted);
        // Written by the app (control characters \u-escaped), then read back.
        var reading = PluginLink.Decode(Encoding.UTF8.GetString(new PluginLink(exe).Encode()));
        Assert.Null(reading.Link);
        Assert.Equal(PluginLinkProblem.Invalid, reading.Problem);
        // The Lua tests' own text, where it is valid JSON (no raw control characters).
        if (!exe.Any(char.IsControl)) Assert.Equal(PluginLinkProblem.Invalid, PluginLink.Decode(LinkJson(exe, true)).Problem);
    }

    [Fact]
    public void LowersOnlyAsciiLettersLikeLua()
    {
        // OrdinalIgnoreCase would match the long s (U+017F) with "s"; Lua's lower() does not.
        Assert.False(PluginLink.IsAcceptedByPlugin("C:\\Lumeri\u017Ft.exe"));
        Assert.False(PluginLink.IsAcceptedByPlugin("C:\\Lumer\u0130st.exe"));
        Assert.False(PluginLink.IsAcceptedByPlugin(null));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"openWithLightroom\":true}")]
    [InlineData("{\"exe\":42}")]
    [InlineData("{\"exe\":null}")]
    [InlineData("[\"C:\\\\Lumerist.exe\"]")]
    [InlineData("\"C:\\\\Lumerist.exe\"")]
    [InlineData("{\"exe\":\"C:\\\\Lumerist.exe\"} trailing")]
    public void RejectsUnreadableFiles(string text)
    {
        var reading = PluginLink.Decode(text);
        Assert.Null(reading.Link);
        Assert.Equal(PluginLinkProblem.Unreadable, reading.Problem);
    }

    [Fact]
    public void OpenWithLightroomIsOnUnlessExplicitlyFalse()
    {
        // OpenApp.lua: link.openWithLightroom ~= false
        Assert.True(PluginLink.Decode(LinkJson(WindowsExe)).Link!.OpenWithLightroom);
        Assert.True(PluginLink.Decode(LinkJson(WindowsExe, true)).Link!.OpenWithLightroom);
        Assert.False(PluginLink.Decode(LinkJson(WindowsExe, false)).Link!.OpenWithLightroom);
        Assert.True(PluginLink.Decode("{\"exe\":\"C:\\\\Lumerist.exe\",\"openWithLightroom\":null}").Link!.OpenWithLightroom);
        Assert.True(PluginLink.Decode("{\"exe\":\"C:\\\\Lumerist.exe\",\"openWithLightroom\":\"false\"}").Link!.OpenWithLightroom);
        Assert.True(PluginLink.Decode("{\"exe\":\"C:\\\\Lumerist.exe\",\"openWithLightroom\":0}").Link!.OpenWithLightroom);
        // Duplicate keys: the last one wins, as in the plug-in's decoder.
        Assert.Equal("C:\\B\\Lumerist.exe",
            PluginLink.Decode("{\"exe\":\"C:\\\\A\\\\Lumerist.exe\",\"exe\":\"C:\\\\B\\\\Lumerist.exe\"}").Link!.Exe);
    }

    [Fact]
    public void EncodesTheContractShape()
    {
        var bytes = new PluginLink(WindowsExe, OpenWithLightroom: false).Encode();
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Equal("{\"exe\":\"C:\\\\Users\\\\Test\\\\AppData\\\\Local\\\\Programs\\\\Lumerist\\\\Lumerist.exe\",\"openWithLightroom\":false}", text);
        var json = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(new[] { "exe", "openWithLightroom" }, json.Select(pair => pair.Key).ToArray());
        Assert.Equal(WindowsExe, json["exe"]!.GetValue<string>());
        Assert.False(json["openWithLightroom"]!.GetValue<bool>());
        // ASCII only and no byte order mark: the plug-in's decoder reads \u escapes.
        Assert.All(bytes, b => Assert.True(b < 0x80));
        Assert.Contains("\\u00EB", Encoding.ASCII.GetString(new PluginLink("C:\\Zoë\\Lumerist.exe").Encode()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavesAtomicallyAndReadsBack()
    {
        Assert.Equal(PluginLinkProblem.Missing, PluginLink.Load(folder).Problem);

        var link = new PluginLink("C:\\Users\\Zoë\\AppData\\Local\\Programs\\Lumerist\\Lumerist.exe", OpenWithLightroom: true);
        var path = link.Save(folder);
        Assert.Equal(PluginLink.PathIn(folder), path);
        Assert.Equal(new PluginLinkReading(link, PluginLinkProblem.None), PluginLink.Load(folder));

        var changed = link with { OpenWithLightroom = false };
        changed.Save(folder);
        Assert.Equal(changed, PluginLink.Load(folder).Link);
        // Only the file itself: no temporary files left behind.
        Assert.Equal(new[] { "plugin-link.json" }, Directory.EnumerateFileSystemEntries(folder).Select(entry => Path.GetFileName(entry)).ToArray());
    }

    [Fact]
    public void LoadReportsUnreadableAndInvalidFiles()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(PluginLink.PathIn(folder), "not json");
        Assert.Equal(PluginLinkProblem.Unreadable, PluginLink.Load(folder).Problem);
        File.WriteAllText(PluginLink.PathIn(folder), LinkJson("C:\\Tools\\other.exe", true));
        Assert.Equal(PluginLinkProblem.Invalid, PluginLink.Load(folder).Problem);
        // A byte order mark is refused by the plug-in's decoder, so here too.
        File.WriteAllBytes(PluginLink.PathIn(folder), [0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(LinkJson(WindowsExe, true))]);
        Assert.Equal(PluginLinkProblem.Unreadable, PluginLink.Load(folder).Problem);
    }
}
