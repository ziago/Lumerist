using Xunit;

namespace Lumerist.Core.Tests;

// Port of Tests/LumeristCoreTests/PluginInstallTests.swift. Every test works in
// its own temporary folder; the real Lightroom Modules folder is never touched.

public class SemanticVersionTests
{
    private static SemanticVersion V(string text) => SemanticVersion.FromText(text) ?? throw new ArgumentException(text);

    [Fact]
    public void ParsesTagsAndPlainVersions()
    {
        Assert.Equal(new SemanticVersion(0, 9, 0), SemanticVersion.FromText("0.9.0"));
        Assert.Equal(new SemanticVersion(1, 2, 3), SemanticVersion.FromText("v1.2.3"));
        Assert.Equal(new SemanticVersion(1, 2, 0), SemanticVersion.FromText("1.2"));
        Assert.Equal("beta.2", SemanticVersion.FromText("1.0.0-beta.2")?.Prerelease);
        Assert.Equal(new SemanticVersion(1, 0, 0), SemanticVersion.FromText("1.0.0+42"));
        Assert.Null(SemanticVersion.FromText(""));
        Assert.Null(SemanticVersion.FromText("1.x.0"));
        Assert.Null(SemanticVersion.FromText("1.2.3.4"));
        Assert.Null(SemanticVersion.FromText("1.-2.3"));
        Assert.Null(SemanticVersion.FromText("99999999999.0.0"));
        Assert.True(SemanticVersion.TryParse(" V0.9.1 ", out var parsed));
        Assert.Equal("0.9.1", parsed.ToString());
        Assert.Equal("1.0.0-beta.2", V("1.0.0-beta.2").ToString());
        Assert.Null(SemanticVersion.FromText("1.0.0-")?.Prerelease);
    }

    [Fact]
    public void OrdersVersionsIncludingPrereleases()
    {
        var order = new[] { "0.3.0", "0.9.0-beta.1", "0.9.0-beta.2", "0.9.0", "0.10.0", "1.0.0" }.Select(V).ToList();
        Assert.Equal(order, order.Order().ToList());
        Assert.True(V("0.9.0-beta.10") > V("0.9.0-beta.9"));
        Assert.True(V("0.9.0-alpha") < V("0.9.0-beta"));
        Assert.False(V("0.9.0") < V("0.9.0"));
        Assert.True(V("0.9.0") <= V("0.9.0"));
    }

    [Fact]
    public void PicksTheNewestReleaseTag()
    {
        var newest = SemanticVersion.Newest(["v0.9.0", "v0.9.1-beta.1", "notes", "v0.9.1", "v0.10.0-beta.1"]);
        Assert.Equal("v0.10.0-beta.1", newest?.Tag);
        Assert.Null(SemanticVersion.Newest(["nightly"]));
    }

    [Fact]
    public void ReadsThePluginInfoVersionTable()
    {
        const string info = """
            return {
                LrPluginName = "Lumerist",
                VERSION = { minor = 9, revision = 1, major = 0, display = "0.9.1 (protocol 3)" },
            }
            """;
        Assert.Equal(new SemanticVersion(0, 9, 1), SemanticVersion.FromPluginInfo(info));
        Assert.Null(SemanticVersion.FromPluginInfo("return { LrPluginName = \"x\" }"));
        Assert.Equal(new SemanticVersion(1, 2, 0), SemanticVersion.FromPluginInfo("VERSION = { major = 1, minor = 2 }"));
    }

    [Fact]
    public void ReadsTheRealPluginInfo()
    {
        var info = File.ReadAllText(Path.Combine(Fixtures.Root, "Lightroom", "Lumerist.lrplugin", "Info.lua"));
        Assert.NotNull(SemanticVersion.FromPluginInfo(info));
    }
}

public class PluginInstallStatusTests
{
    private static readonly SemanticVersion V09 = new(0, 9, 0);
    private static readonly SemanticVersion V03 = new(0, 3, 0);

    [Fact]
    public void ComparesInstalledAndBundledCopies()
    {
        Assert.Equal(new PluginInstallStatus.NotInstalled(), PluginInstallStatus.Compare(null, false, V09, false));
        Assert.Equal(new PluginInstallStatus.Outdated(V03, V09), PluginInstallStatus.Compare(V03, true, V09, false));
        Assert.Equal(new PluginInstallStatus.Outdated(null, V09), PluginInstallStatus.Compare(null, true, V09, false));
        Assert.Equal(new PluginInstallStatus.UpToDate(V09), PluginInstallStatus.Compare(V09, true, V09, true));
        Assert.Equal(new PluginInstallStatus.Modified(V09), PluginInstallStatus.Compare(V09, true, V09, false));
        Assert.Equal(new PluginInstallStatus.NewerInstalled(V09, V03), PluginInstallStatus.Compare(V09, true, V03, false));
        Assert.True(new PluginInstallStatus.NotInstalled().NeedsAction);
        Assert.True(new PluginInstallStatus.Outdated(null, V09).NeedsAction);
        Assert.True(new PluginInstallStatus.Modified(V09).NeedsAction);
        Assert.False(new PluginInstallStatus.UpToDate(V09).NeedsAction);
        Assert.False(new PluginInstallStatus.NewerInstalled(V09, V03).NeedsAction);
        Assert.True(new PluginInstallStatus.Outdated(V03, V09).UpdatesAutomatically);
        Assert.True(new PluginInstallStatus.Outdated(null, V09).UpdatesAutomatically);
        Assert.False(new PluginInstallStatus.NotInstalled().UpdatesAutomatically);
        Assert.False(new PluginInstallStatus.Modified(V09).UpdatesAutomatically);
        Assert.False(new PluginInstallStatus.UpToDate(V09).UpdatesAutomatically);
        Assert.False(new PluginInstallStatus.NewerInstalled(V09, V03).UpdatesAutomatically);
    }

    [Fact]
    public void BackupNamesSortByTime()
    {
        var name = PluginInstallLayout.BackupName(DateTimeOffset.FromUnixTimeSeconds(0), TimeZoneInfo.Utc);
        Assert.Equal("Lumerist.lrplugin.backup-19700101-000000", name);
    }

    [Fact]
    public void LayoutKeepsBackupsOutsideModules()
    {
        var appData = Path.Combine(Path.GetTempPath(), "AppData", "Roaming");
        var modules = PluginInstallLayout.ModulesDirectory(appData);
        var backups = PluginInstallLayout.BackupDirectory(appData);
        Assert.Equal(Path.Combine(appData, "Adobe", "Lightroom", "Modules"), modules);
        Assert.Equal(Path.Combine(appData, "Lumerist", "plugin-backups"), backups);
        Assert.False(backups.StartsWith(modules, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Contract.PluginBundleName, PluginInstallLayout.BundleName);
    }
}

public sealed class PluginInstallOperationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lumerist-install-" + Guid.NewGuid().ToString("N"));
    private readonly PluginInstallOperation operation;

    public PluginInstallOperationTests()
    {
        var source = Path.Combine(root, "bundle", "Lumerist.lrplugin");
        WritePlugin(source, (0, 9, 0), "-- new");
        operation = new PluginInstallOperation(source, Path.Combine(root, "Modules"), Path.Combine(root, "backups"));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void WritePlugin(string folder, (int Major, int Minor, int Revision) version, string body)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Info.lua"),
            $"return {{ VERSION = {{ major = {version.Major}, minor = {version.Minor}, revision = {version.Revision} }} }}\n");
        File.WriteAllText(Path.Combine(folder, "Init.lua"), body);
    }

    private static readonly SemanticVersion V09 = new(0, 9, 0);

    /// <summary>
    /// Windows creates symbolic links only with Developer Mode or as an
    /// administrator; without either, the link tests have nothing to check.
    /// </summary>
    private static bool TryLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void InstallsIntoAnEmptyModulesFolder()
    {
        Assert.Equal(new PluginInstallStatus.NotInstalled(), operation.Status());
        Assert.Null(operation.Install());
        Assert.Equal(new PluginInstallStatus.UpToDate(V09), operation.Status());
        // No staging leftovers next to the plug-in.
        var modules = Directory.EnumerateFileSystemEntries(operation.ModulesDirectory).Select(path => Path.GetFileName(path)).ToList();
        Assert.Equal(new[] { "Lumerist.lrplugin" }, modules);
        Assert.Equal("-- new", File.ReadAllText(Path.Combine(operation.Destination, "Init.lua")));
    }

    [Fact]
    public void CopiesSubfoldersAndLeavesMacFolderMetadataBehind()
    {
        Directory.CreateDirectory(Path.Combine(operation.Source, "sub"));
        File.WriteAllText(Path.Combine(operation.Source, "sub", "Extra.lua"), "-- extra");
        File.WriteAllBytes(Path.Combine(operation.Source, ".DS_Store"), [0, 1, 2]);
        operation.Install();
        Assert.Equal("-- extra", File.ReadAllText(Path.Combine(operation.Destination, "sub", "Extra.lua")));
        Assert.False(File.Exists(Path.Combine(operation.Destination, ".DS_Store")));
        Assert.Equal(new PluginInstallStatus.UpToDate(V09), operation.Status());
    }

    [Fact]
    public void UpdatingKeepsTheOldCopyAsABackup()
    {
        WritePlugin(operation.Destination, (0, 3, 0), "-- old");
        Assert.Equal(new PluginInstallStatus.Outdated(new SemanticVersion(0, 3, 0), V09), operation.Status());

        var backup = operation.Install(DateTimeOffset.FromUnixTimeSeconds(1_000));
        Assert.NotNull(backup);
        Assert.Equal(new SemanticVersion(0, 3, 0), PluginInstallOperation.VersionOfPlugin(backup));
        Assert.StartsWith(operation.BackupDirectory, backup);
        Assert.StartsWith(PluginInstallLayout.BackupPrefix, Path.GetFileName(backup));
        Assert.Equal(V09, operation.InstalledVersion);
    }

    [Fact]
    public void ADanglingSymlinkIsMovedAsideNotTreatedAsMissing()
    {
        Directory.CreateDirectory(operation.ModulesDirectory);
        if (!TryLink(() => Directory.CreateSymbolicLink(operation.Destination, Path.Combine(root, "a-working-copy-that-was-deleted")))) return;
        Assert.True(operation.IsInstalled);

        var backup = operation.Install(DateTimeOffset.FromUnixTimeSeconds(2_000));
        Assert.NotNull(backup);
        Assert.StartsWith(operation.BackupDirectory, backup);
        Assert.NotNull(new FileInfo(backup).LinkTarget);
        Assert.Equal(V09, operation.InstalledVersion);
    }

    [Fact]
    public void ALinkedWorkingCopyIsMovedAsideAndLeftIntact()
    {
        var workingCopy = Path.Combine(root, "working-copy");
        WritePlugin(workingCopy, (0, 9, 0), "-- a development build");
        Directory.CreateDirectory(operation.ModulesDirectory);
        if (!TryLink(() => Directory.CreateSymbolicLink(operation.Destination, workingCopy))) return;
        Assert.Equal(new PluginInstallStatus.Modified(V09), operation.Status());

        var backup = operation.Install();
        Assert.NotNull(backup);
        Assert.NotNull(new DirectoryInfo(backup).LinkTarget);
        Assert.Equal("-- a development build", File.ReadAllText(Path.Combine(workingCopy, "Init.lua")));
        Assert.Null(new DirectoryInfo(operation.Destination).LinkTarget);
        Assert.Equal(new PluginInstallStatus.UpToDate(V09), operation.Status());
    }

    [Fact]
    public void SameVersionWithOtherFilesIsReportedAsModified()
    {
        WritePlugin(operation.Destination, (0, 9, 0), "-- a development build");
        Assert.Equal(new PluginInstallStatus.Modified(V09), operation.Status());
        // macOS folder metadata (.DS_Store) does not count as a difference.
        operation.Install();
        File.WriteAllBytes(Path.Combine(operation.Destination, ".DS_Store"), [0, 1, 2]);
        Assert.Equal(new PluginInstallStatus.UpToDate(V09), operation.Status());
    }

    [Fact]
    public void FingerprintsNamesAndContents()
    {
        var copy = Path.Combine(root, "copy", "Lumerist.lrplugin");
        WritePlugin(copy, (0, 9, 0), "-- new");
        var original = PluginInstallOperation.FingerprintOfPlugin(operation.Source);
        Assert.Matches("^[0-9a-f]{64}$", original);
        Assert.Equal(original, PluginInstallOperation.FingerprintOfPlugin(copy));
        File.Move(Path.Combine(copy, "Init.lua"), Path.Combine(copy, "Other.lua"));
        Assert.NotEqual(original, PluginInstallOperation.FingerprintOfPlugin(copy));
        Assert.Null(PluginInstallOperation.FingerprintOfPlugin(Path.Combine(root, "nowhere")));
    }

    [Fact]
    public void RemovingMovesThePluginToTheBackupFolder()
    {
        operation.Install();
        var first = operation.Remove(DateTimeOffset.FromUnixTimeSeconds(2_000));
        Assert.NotNull(first);
        Assert.False(operation.IsInstalled);
        operation.Install();
        // Two removals in the same second never overwrite each other.
        var second = operation.Remove(DateTimeOffset.FromUnixTimeSeconds(2_000));
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.EndsWith("-2", second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.Null(operation.Remove());
    }

    [Fact]
    public void RefusesAnUnreadableBundledPlugin()
    {
        File.Delete(Path.Combine(operation.Source, "Info.lua"));
        var error = Assert.Throws<PluginInstallException>(() => operation.Install());
        Assert.Equal(PluginInstallErrorKind.BundledPluginUnreadable, error.Kind);
        Assert.Equal(operation.Source, error.FolderPath);
        Assert.Contains("Download Lumerist again", error.Message);
        Assert.Throws<PluginInstallException>(() => operation.Status());
        Assert.False(operation.IsInstalled);
    }

    [Fact]
    public void AFailedCopyPutsTheOldPluginBack()
    {
        WritePlugin(operation.Destination, (0, 3, 0), "-- old");
        // A readable Info.lua with a file that cannot be read (a dangling link):
        // the copy fails after the old copy was moved to the backup folder.
        var broken = operation with { Source = Path.Combine(root, "broken.lrplugin") };
        Directory.CreateDirectory(broken.Source);
        File.WriteAllText(Path.Combine(broken.Source, "Info.lua"), "return { VERSION = { major = 0, minor = 9 } }");
        var unreadable = Path.Combine(broken.Source, "Locked.lua");
        if (!TryLink(() => File.CreateSymbolicLink(unreadable, Path.Combine(root, "missing-target.lua")))) return;

        Assert.ThrowsAny<IOException>(() => broken.Install());
        Assert.Equal(new SemanticVersion(0, 3, 0), broken.InstalledVersion);
        Assert.Equal("-- old", File.ReadAllText(Path.Combine(broken.Destination, "Init.lua")));
        Assert.Equal(new[] { "Lumerist.lrplugin" }, Directory.EnumerateFileSystemEntries(broken.ModulesDirectory).Select(path => Path.GetFileName(path)).ToList());
    }

    [Fact]
    public void ErrorMessagesMatchTheMacApp()
    {
        Assert.Equal("Quit Lightroom Classic first: it keeps plug-in code in memory.", PluginInstallException.LightroomIsRunning().Message);
        var restore = PluginInstallException.RestoreFailed("C:\\backup");
        Assert.Equal(PluginInstallErrorKind.RestoreFailed, restore.Kind);
        Assert.Equal("Installing failed and the previous plug-in could not be put back. Your copy is safe in C:\\backup; move it back to Lightroom's Modules folder or install again.", restore.Message);
    }
}
