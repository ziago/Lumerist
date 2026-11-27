using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// "Update now" on Windows beyond the shared cases (UpdateFixtureTests): the
/// files for this PC's processor, the installed-copy check, the downloader's
/// redirects and limits, and the whole preparation in its order. No test
/// touches the network: downloads go through <see cref="FakeWeb"/>.
/// </summary>
public class UpdateInstallTests
{
    // Tags and versions -----------------------------------------------------------

    [Theory]
    [InlineData("v0.9.2", "0.9.2")]
    [InlineData("v1.0.0-beta.2", "1.0.0-beta.2")]
    [InlineData("v10.20.30", "10.20.30")]
    [InlineData("0.9.2", null)]
    [InlineData("V0.9.2", null)]
    [InlineData("v0.9", null)]
    [InlineData("v0.9.2.1", null)]
    [InlineData("v01.0.0", null)]
    [InlineData("v1.0.0+42", null)]
    [InlineData("v1.0.0-", null)]
    [InlineData(" v1.0.0", null)]
    [InlineData("v1.0.0\n", null)]
    [InlineData("nightly", null)]
    [InlineData("v1.0.0-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)]
    public void OnlyVPlusASemanticVersionIsARelease(string tag, string? version)
    {
        Assert.Equal(version, UpdateCheck.TagVersion(tag)?.ToString());
    }

    [Fact]
    public void OnlyAStrictlyNewerReleaseIsOffered()
    {
        Assert.IsType<UpdateCheckState.UpToDate>(UpdateCheck.Evaluate([Release("v0.9.2")], "0.9.2"));
        Assert.IsType<UpdateCheckState.UpToDate>(UpdateCheck.Evaluate([Release("v0.9.1")], "0.9.2"));
        // Tags that are not v + semver never count, however high.
        Assert.IsType<UpdateCheckState.UpToDate>(UpdateCheck.Evaluate([Release("9.0.0"), Release("V9.0.0"), Release("v0.9.1")], "0.9.2"));
        var available = Assert.IsType<UpdateCheckState.Available>(UpdateCheck.Evaluate([Release("v0.9.3-beta.1")], "0.9.2"));
        Assert.Equal("v0.9.3-beta.1", available.Release?.TagName);
    }

    [Fact]
    public void TheFeedGivesTheNotesAndTheFiles()
    {
        var releases = UpdateCheck.DecodeReleases("""
            [{"tag_name": "v0.9.2", "html_url": "https://github.com/ziago/Lumerist/releases/tag/v0.9.2", "draft": false,
              "body": "### Highlights\n- Faster",
              "assets": [
                {"name": "SHA256SUMS", "browser_download_url": "https://github.com/ziago/Lumerist/releases/download/v0.9.2/SHA256SUMS", "size": 400, "digest": null},
                {"name": "a.exe", "browser_download_url": "HTTPS://github.com/x/a.exe", "size": 12, "digest": "sha256:00", "state": "open"},
                {"name": 3, "browser_download_url": "https://github.com/x", "size": 1},
                {"name": "no-url", "size": 1},
                {"name": "no-size", "browser_download_url": "https://github.com/x"},
                {"name": "odd-size", "browser_download_url": "https://github.com/x", "size": "1"},
                "odd"
              ]},
             {"tag_name": "v0.9.1", "html_url": "https://github.com/ziago/Lumerist/releases/tag/v0.9.1", "draft": false, "body": null, "assets": {}}]
            """u8);
        Assert.NotNull(releases);
        Assert.Equal("### Highlights\n- Faster", releases[0].Body);
        Assert.Equal(["SHA256SUMS", "a.exe"], releases[0].Assets.Select(asset => asset.Name));
        Assert.Equal(400, releases[0].Assets[0].Size);
        Assert.Null(releases[0].Assets[0].Digest);
        Assert.Null(releases[0].Assets[0].State);
        // Kept as written: compared as text.
        Assert.Equal("HTTPS://github.com/x/a.exe", releases[0].Assets[1].DownloadUrl);
        Assert.Equal("sha256:00", releases[0].Assets[1].Digest);
        Assert.Equal("open", releases[0].Assets[1].State);
        Assert.Null(releases[1].Body);
        Assert.Empty(releases[1].Assets);
    }

    // Which files -----------------------------------------------------------------

    private const string Download = "https://github.com/ziago/Lumerist/releases/download/v0.9.2/";

    private static ReleaseAsset Asset(string name, string? url = null, long size = 1000, string? digest = null, string? state = "uploaded") =>
        new(name, url ?? Download + name, size, digest, state);

    private static UpdateRelease Release(string tag, params ReleaseAsset[] assets) =>
        new(tag, new Uri($"https://github.com/ziago/Lumerist/releases/tag/{tag}"), false) { Assets = assets };

    private static UpdateRelease FullRelease(params ReleaseAsset[] extra) => Release("v0.9.2",
    [
        Asset("Lumerist-0.9.2.dmg"),
        Asset("Lumerist-0.9.2-windows-x64-setup.exe"),
        Asset("Lumerist-0.9.2-windows-x64-setup.exe.sha256", size: 104),
        Asset("Lumerist-0.9.2-windows-x64.zip"),
        Asset("Lumerist-0.9.2-windows-arm64-setup.exe"),
        Asset("SHA256SUMS", size: 500),
        Asset("SHA256SUMS.sig", size: 97),
        .. extra,
    ]);

    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, null)]
    [InlineData(Architecture.Arm, null)]
    public void TheInstallerFollowsThePcsProcessor(Architecture architecture, string? name)
    {
        Assert.Equal(name, UpdateAssets.ArchitectureName(architecture));
    }

    [Fact]
    public void TakesTheInstallerAndTheSignedChecksumsByExactName()
    {
        var x64 = Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(
            FullRelease() with { Assets = [.. FullRelease().Assets.Select(asset => asset.Name == "Lumerist-0.9.2-windows-x64-setup.exe"
                ? asset with { Digest = "sha256:" + Hex.ToUpperInvariant() } : asset)] }, "0.9.1", "x64")).Package;
        Assert.Equal("0.9.2", x64.Version);
        Assert.Equal("Lumerist-0.9.2-windows-x64-setup.exe", x64.Installer.Name);
        Assert.Equal(Download + "Lumerist-0.9.2-windows-x64-setup.exe", x64.Installer.Url.AbsoluteUri);
        Assert.Equal(1000, x64.Installer.Size);
        Assert.Equal(Hex, x64.Installer.Sha256);
        Assert.Equal("SHA256SUMS", x64.ChecksumList.Name);
        Assert.Equal("SHA256SUMS.sig", x64.Signature.Name);
        Assert.Equal("Lumerist-0.9.2-windows-x64-setup.exe.sha256", x64.Checksum?.Name);

        // No .sha256 for arm64 in this release: the signed list is enough.
        var arm = Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(FullRelease(), "0.9.1", "arm64")).Package;
        Assert.Equal("Lumerist-0.9.2-windows-arm64-setup.exe", arm.Installer.Name);
        Assert.Null(arm.Installer.Sha256);
        Assert.Null(arm.Checksum);
    }

    [Fact]
    public void RefusesWhatItCannotProveOrDoesNotNeed()
    {
        UpdateRefusal? Refusal(UpdateRelease release, string running = "0.9.1", string? arch = "x64") =>
            (UpdateAssets.Choose(release, running, arch) as UpdateChoice.Refused)?.Reason;
        UpdateRelease Changed(string name, Func<ReleaseAsset, ReleaseAsset> change) =>
            FullRelease() with { Assets = [.. FullRelease().Assets.Select(asset => asset.Name == name ? change(asset) : asset)] };
        UpdateRelease Without(string name) => FullRelease() with { Assets = [.. FullRelease().Assets.Where(asset => asset.Name != name)] };
        const string installer = "Lumerist-0.9.2-windows-x64-setup.exe";

        Assert.Equal(UpdateRefusal.NotNewer, Refusal(FullRelease(), running: "0.9.2"));
        Assert.Equal(UpdateRefusal.NotNewer, Refusal(FullRelease(), running: "0.10.0"));
        // A development build doesn't know what it would replace; nor does a "v" version.
        Assert.Equal(UpdateRefusal.NotNewer, Refusal(FullRelease(), running: "?"));
        Assert.Equal(UpdateRefusal.NotNewer, Refusal(FullRelease(), running: "v0.9.1"));
        Assert.Equal(UpdateRefusal.BadTag, Refusal(FullRelease() with { TagName = "0.9.2" }));
        Assert.Equal(UpdateRefusal.UnsupportedArchitecture, Refusal(FullRelease(), arch: null));
        Assert.Equal(UpdateRefusal.UnsupportedArchitecture, Refusal(FullRelease(), arch: "x86"));

        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Without(installer)));
        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Without("SHA256SUMS")));
        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Without("SHA256SUMS.sig")));
        // The installer's name carries the tag's version, in this case.
        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Changed(installer, asset => asset with { Name = "Lumerist-0.9.1-windows-x64-setup.exe" })));
        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Changed(installer, asset => asset with { Name = "lumerist-0.9.2-windows-x64-setup.exe" })));
        // An unfinished upload doesn't count.
        Assert.Equal(UpdateRefusal.MissingFile, Refusal(Changed(installer, asset => asset with { State = "open" })));
        Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(Changed(installer, asset => asset with { State = null }), "0.9.1", "x64"));

        Assert.Equal(UpdateRefusal.BadFile, Refusal(FullRelease(Asset("SHA256SUMS", size: 500))));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { DownloadUrl = "https://example.com/" + installer })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { DownloadUrl = "https://GitHub.com/ziago/Lumerist/releases/download/v0.9.2/" + installer })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { DownloadUrl = Download.Replace("v0.9.2", "v0.9.1") + installer })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { DownloadUrl = Download + "Lumerist-0.9.2-windows-arm64-setup.exe" })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed("SHA256SUMS", asset => asset with { DownloadUrl = "http://github.com/ziago/Lumerist/releases/download/v0.9.2/SHA256SUMS" })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { Size = 0 })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { Size = UpdateAssets.Limit.Installer + 1 })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed("SHA256SUMS", asset => asset with { Size = UpdateAssets.Limit.ChecksumList + 1 })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed("SHA256SUMS.sig", asset => asset with { Size = UpdateAssets.Limit.Signature + 1 })));
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer, asset => asset with { Digest = "sha256:xyz" })));
        // A bad .sha256, when the release has one, refuses the whole release.
        Assert.Equal(UpdateRefusal.BadFile, Refusal(Changed(installer + ".sha256", asset => asset with { Size = 5000 })));
        Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(Changed(installer, asset => asset with { Digest = "sha512:abc" }), "0.9.1", "x64"));

        // A file on disk only with the debug builds' test feed.
        var local = Changed("SHA256SUMS", asset => asset with { DownloadUrl = "file:///C:/feed/SHA256SUMS" });
        Assert.Equal(UpdateRefusal.BadFile, Refusal(local));
        Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(local, "0.9.1", "x64", allowLocalFiles: true));
        Assert.IsType<UpdateChoice.Refused>(UpdateAssets.Choose(Changed("SHA256SUMS", asset => asset with { DownloadUrl = "file://server/share/SHA256SUMS" }),
            "0.9.1", "x64", allowLocalFiles: true));
    }

    // Checksums -------------------------------------------------------------------

    private const string Hex = "3f786850e387550fdab836ed7e6dc881de23001b8a3a3bf5c2f3d8c6c4a1f0e2";

    [Fact]
    public void GitHubsDigestIsReadWhenItIsASha256()
    {
        Assert.True(UpdateChecksums.TryReadDigest(null, out var none));
        Assert.Null(none);
        Assert.True(UpdateChecksums.TryReadDigest("sha256:" + Hex.ToUpperInvariant(), out var upper));
        Assert.Equal(Hex, upper);
        Assert.True(UpdateChecksums.TryReadDigest("sha512:abc", out var other));
        Assert.Null(other);
        Assert.True(UpdateChecksums.TryReadDigest("SHA256:" + Hex, out var shouted));
        Assert.Null(shouted);
        Assert.False(UpdateChecksums.TryReadDigest("sha256:xyz", out _));
        Assert.False(UpdateChecksums.TryReadDigest("sha256:", out _));
    }

    // The signature ---------------------------------------------------------------

    private static (ECDsa Key, byte[] Public) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, key.ExportSubjectPublicKeyInfo());
    }

    private static byte[] Sign(ECDsa key, byte[] data) =>
        Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) + "\n");

    [Fact]
    public void AcceptsOnlyASignatureFromAListedKey()
    {
        var (key, publicKey) = NewKey();
        using var _ = key;
        var (other, otherPublic) = NewKey();
        using var __ = other;
        var list = Encoding.UTF8.GetBytes($"{Hex}  a\n");
        var signature = Sign(key, list);

        Assert.True(UpdateSignature.Verify(list, signature, [publicKey]));
        // Any listed key will do, so a later key can join the list.
        Assert.True(UpdateSignature.Verify(list, signature, [otherPublic, publicKey]));
        Assert.False(UpdateSignature.Verify(list, signature, [otherPublic]));
        Assert.False(UpdateSignature.Verify(list, signature, []));
        // One changed byte in the list.
        var tampered = (byte[])list.Clone();
        tampered[0] = (byte)'4';
        Assert.False(UpdateSignature.Verify(tampered, signature, [publicKey]));
        // The exact bytes: a line break more is another list.
        Assert.False(UpdateSignature.Verify([.. list, (byte)'\n'], signature, [publicKey]));
        // Spaces, tabs and line breaks around the base64 are fine, nothing inside it.
        Assert.True(UpdateSignature.Verify(list, Encoding.ASCII.GetBytes(" \t" + Encoding.ASCII.GetString(signature).Trim() + "\r\n"), [publicKey]));
        var text = Encoding.ASCII.GetString(signature).Trim();
        Assert.False(UpdateSignature.Verify(list, Encoding.ASCII.GetBytes(text[..10] + "\n" + text[10..]), [publicKey]));
        Assert.False(UpdateSignature.Verify(list, "not base64!"u8, [publicKey]));
        // Base64 that isn't a multiple of 4 long (a DER signature of 72 bytes has no "=" to drop, so cut a character).
        Assert.False(UpdateSignature.Verify(list, Encoding.ASCII.GetBytes(text[..^1]), [publicKey]));
        // A key that can't be read is skipped, not fatal.
        Assert.True(UpdateSignature.Verify(list, signature, [[1, 2, 3], publicKey]));
        Assert.False(UpdateSignature.Verify(list, ""u8, [publicKey]));
        Assert.False(UpdateSignature.Verify(list, Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[70])), [publicKey]));
        // The raw (IEEE P1363) form is not the format releases use.
        var raw = Encoding.ASCII.GetBytes(Convert.ToBase64String(key.SignData(list, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        Assert.False(UpdateSignature.Verify(list, raw, [publicKey]));
    }

    [Fact]
    public void KeysMustBeP256()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var rsa = RSA.Create(2048);
        var (key, publicKey) = NewKey();
        using var _ = key;
        Assert.True(UpdateSignature.IsP256PublicKey(publicKey));
        Assert.False(UpdateSignature.IsP256PublicKey(p384.ExportSubjectPublicKeyInfo()));
        Assert.False(UpdateSignature.IsP256PublicKey(rsa.ExportSubjectPublicKeyInfo()));
        Assert.False(UpdateSignature.IsP256PublicKey([.. publicKey, 0]));
        Assert.False(UpdateSignature.IsP256PublicKey([1, 2, 3]));

        var list = UpdateSignature.ParseKeyList($$"""{"updatePublicKeys": ["{{Convert.ToBase64String(publicKey)}}"]}""");
        Assert.NotNull(list);
        Assert.Equal(publicKey, Assert.Single(list));
        Assert.Null(UpdateSignature.ParseKeyList($$"""{"updatePublicKeys": ["{{Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())}}"]}"""));
        Assert.Null(UpdateSignature.ParseKeyList("""{"updatePublicKeys": ["%%%"]}"""));
        Assert.Null(UpdateSignature.ParseKeyList("""{"updatePublicKeys": [1]}"""));
        Assert.Null(UpdateSignature.ParseKeyList("""{"keys": []}"""));
        Assert.Null(UpdateSignature.ParseKeyList("[]"));
    }

    // The installed copy ------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs\Lumerist", null, true)]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs\Lumerist\", null, true)]
    [InlineData(@"c:\users\ann\appdata\local\programs\lumerist", null, true)]
    [InlineData(@"C:/Users/Ann/AppData/Local/Programs/Lumerist", null, true)]
    [InlineData(@"C:\Users\Ann\Downloads\Lumerist", null, false)]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs\Lumerist\..\Lumerist", null, false)]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs\Lumerist-old", null, false)]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs", null, false)]
    [InlineData(@"\\server\share\Lumerist", null, false)]
    [InlineData(@"Programs\Lumerist", null, false)]
    [InlineData(@"D:\Apps\Lumerist", @"D:\Apps\Lumerist\", true)]
    [InlineData(@"C:\Users\Ann\AppData\Local\Programs\Lumerist", @"D:\Apps\Lumerist\", false)]
    [InlineData(null, null, false)]
    public void OnlyTheInstalledCopyUpdatesItself(string? appFolder, string? registered, bool installed)
    {
        Assert.Equal(installed, UpdateInstallLocation.IsInstalledCopy(appFolder, @"C:\Users\Ann\AppData\Local", registered));
    }

    /// <summary>The installer script and the app agree on the switches, the mutexes, the folder and the uninstall entry.</summary>
    [Fact]
    public void TheInstallerScriptMatchesTheApp()
    {
        var script = File.ReadAllText(Path.Combine(Fixtures.Root, "Windows", "Installer", "Lumerist.iss"));
        Assert.Contains("AppMutex=" + WindowsInstaller.InstanceMutexName + "\n", script.Replace("\r\n", "\n"));
        Assert.Contains($"UpdatingMutex = '{WindowsInstaller.UpdatingMutexName}';", script);
        Assert.Contains($"CompareText(ParamStr(I), '{WindowsInstaller.RelaunchSwitch}') = 0", script);
        Assert.Contains($"Parameters: \"{WindowsInstaller.AfterUpdateArgument}\"; Flags: nowait; Check: StartedByLumerist", script);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\Lumerist", script);
        Assert.Contains("PrivilegesRequired=lowest", script);
        Assert.Contains("CloseApplications=yes", script);
        // The uninstall entry Lumerist reads is the AppId's.
        var appId = System.Text.RegularExpressions.Regex.Match(script, @"AppId=\{(\{[0-9A-F-]+\})").Groups[1].Value;
        Assert.Equal(UpdateInstallLocation.UninstallKey, $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{appId}_is1");
        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LUMERISTRELAUNCH"], WindowsInstaller.Arguments);
    }

    // Downloads ---------------------------------------------------------------------

    [Fact]
    public async Task FollowsGitHubsRedirectAfterCheckingIt()
    {
        var web = new FakeWeb();
        web.Redirect(Download + "a.exe", "https://release-assets.githubusercontent.com/asset/1?sig=abc");
        web.Serve("https://release-assets.githubusercontent.com/asset/1?sig=abc", "MZ1234"u8.ToArray());
        using var downloader = new UpdateDownloader(web);
        var data = await downloader.FetchAsync(new Uri(Download + "a.exe"), 100, default);
        Assert.Equal("MZ1234"u8.ToArray(), data);
        Assert.Equal(2, web.Requests.Count);
        foreach (var request in web.Requests)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Lumerist", string.Join(" ", request.Headers.GetValues("User-Agent")));
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Content);
        }
    }

    [Fact]
    public async Task ARedirectAwayFromGitHubIsNeverFollowed()
    {
        var web = new FakeWeb();
        web.Redirect(Download + "a.exe", "https://evil.example.com/a.exe");
        web.Serve("https://evil.example.com/a.exe", "MZ"u8.ToArray());
        using var downloader = new UpdateDownloader(web);
        var failure = await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "a.exe"), 100, default));
        Assert.Equal("The download was sent to https://evil.example.com, which isn't GitHub.", failure.Message);
        Assert.Single(web.Requests);

        // Nor to plain http on GitHub, nor through a relative Location to another port.
        web = new FakeWeb();
        web.Redirect(Download + "b.exe", "http://objects.githubusercontent.com/b.exe");
        using var second = new UpdateDownloader(web);
        await Assert.ThrowsAsync<UpdateFailure>(() => second.FetchAsync(new Uri(Download + "b.exe"), 100, default));
        Assert.Single(web.Requests);

        web = new FakeWeb();
        web.Redirect(Download + "c.exe", "/ziago/Lumerist/releases/download/v0.9.2/d.exe");
        web.Serve(Download + "d.exe", "MZ"u8.ToArray());
        using var third = new UpdateDownloader(web);
        Assert.Equal("MZ"u8.ToArray(), await third.FetchAsync(new Uri(Download + "c.exe"), 100, default));

        // The first address is checked too.
        web = new FakeWeb();
        using var fourth = new UpdateDownloader(web);
        await Assert.ThrowsAsync<UpdateFailure>(() => fourth.FetchAsync(new Uri("https://example.com/a.exe"), 100, default));
        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task StopsAfterFiveRedirects()
    {
        var web = new FakeWeb();
        for (var hop = 0; hop < 7; hop++) web.Redirect($"{Download}{hop}", $"{Download}{hop + 1}");
        using var downloader = new UpdateDownloader(web);
        var failure = await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "0"), 100, default));
        Assert.Equal("GitHub redirected the download too many times.", failure.Message);
        Assert.Equal(UpdateDownloadRules.MaxRedirects + 1, web.Requests.Count);
    }

    [Fact]
    public async Task KeepsToTheSizeLimitAndTheListedSize()
    {
        var web = new FakeWeb();
        web.Serve(Download + "big", new byte[200]);
        web.Serve(Download + "unannounced", new byte[200], announceLength: false);
        using var downloader = new UpdateDownloader(web);
        await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "big"), 100, default));
        await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "unannounced"), 100, default));

        using var sink = new MemoryStream();
        var wrong = await Assert.ThrowsAsync<UpdateFailure>(() =>
            downloader.DownloadAsync(new Uri(Download + "big"), sink, 1000, expectedSize: 150, null, default));
        Assert.Equal("The download isn't the size GitHub lists.", wrong.Message);
        await Assert.ThrowsAsync<UpdateFailure>(() =>
            downloader.DownloadAsync(new Uri(Download + "unannounced"), new MemoryStream(), 1000, expectedSize: 150, null, default));
        await Assert.ThrowsAsync<UpdateFailure>(() =>
            downloader.DownloadAsync(new Uri(Download + "unannounced"), new MemoryStream(), 1000, expectedSize: 250, null, default));
        var (sha256, length) = await downloader.DownloadAsync(new Uri(Download + "unannounced"), new MemoryStream(), 1000, expectedSize: 200, null, default);
        Assert.Equal(200, length);
        Assert.Equal(UpdateChecksums.Hex(SHA256.HashData(new byte[200])), sha256);
    }

    [Fact]
    public async Task FailuresAreMessages()
    {
        var web = new FakeWeb();
        web.Fail(Download + "gone", HttpStatusCode.NotFound);
        web.Throw(Download + "offline", new HttpRequestException("No such host is known."));
        using var downloader = new UpdateDownloader(web);
        Assert.Equal("GitHub answered with status 404.",
            (await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "gone"), 100, default))).Message);
        Assert.Equal("Couldn't reach GitHub: No such host is known.",
            (await Assert.ThrowsAsync<UpdateFailure>(() => downloader.FetchAsync(new Uri(Download + "offline"), 100, default))).Message);

        // The user's cancel stays a cancel.
        web.Serve(Download + "slow", new byte[10]);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.FetchAsync(new Uri(Download + "slow"), 100, cancel.Token));
    }

    // The whole preparation -----------------------------------------------------------

    private sealed class SignedRelease : IDisposable
    {
        public const string Installer = "Lumerist-0.9.2-windows-x64-setup.exe";
        public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public readonly FakeWeb Web = new();
        public readonly string Folder = Directory.CreateTempSubdirectory("lumerist-test-").FullName;
        public byte[] Program = [(byte)'M', (byte)'Z', .. Enumerable.Range(0, 5000).Select(n => (byte)n)];

        public byte[] PublicKey => Key.ExportSubjectPublicKeyInfo();

        public string ProgramSha256 => UpdateChecksums.Hex(SHA256.HashData(Program));

        /// <summary>Serves the release: the list signed with <paramref name="signer"/> (this key by default).</summary>
        public UpdatePackage Publish(string? listed = null, ECDsa? signer = null, string? shaFile = null, string? digest = null, bool serveChecksum = true)
        {
            var list = Encoding.UTF8.GetBytes($"{new string('1', 64)}  Lumerist-0.9.2.dmg\n{listed ?? ProgramSha256}  {Installer}\n");
            Web.Serve(Download + "SHA256SUMS", list);
            Web.Redirect(Download + Installer, "https://release-assets.githubusercontent.com/x?sig=1");
            Web.Serve("https://release-assets.githubusercontent.com/x?sig=1", Program);
            var sha = Encoding.ASCII.GetBytes(shaFile ?? $"{ProgramSha256}  {Installer}");
            Web.Serve(Download + Installer + ".sha256", sha);
            var signature = Sign(signer ?? Key, list);
            Web.Serve(Download + "SHA256SUMS.sig", signature);
            var release = Release("v0.9.2", Asset(Installer, size: Program.Length, digest: digest), Asset(Installer + ".sha256", size: sha.Length),
                Asset("SHA256SUMS", size: list.Length), Asset("SHA256SUMS.sig", size: signature.Length));
            var package = Assert.IsType<UpdateChoice.Ready>(UpdateAssets.Choose(release, "0.9.1", "x64")).Package;
            return serveChecksum ? package : package with { Checksum = null };
        }

        public async Task<PreparedInstaller> PrepareAsync(UpdatePackage package, IReadOnlyList<byte[]>? keys = null)
        {
            using var downloader = new UpdateDownloader(Web);
            var preparation = new UpdatePreparation(downloader, new SignedChecksumSource(downloader, package, keys ?? [PublicKey]));
            return await preparation.PrepareAsync(package, Folder, null, default);
        }

        public void Dispose()
        {
            Key.Dispose();
            Directory.Delete(Folder, recursive: true);
        }
    }

    [Fact]
    public async Task ASignedInstallerIsReadyToRun()
    {
        using var release = new SignedRelease();
        var package = release.Publish(digest: "sha256:" + release.ProgramSha256);
        var steps = new List<UpdateStep>();
        using var downloader = new UpdateDownloader(release.Web);
        var preparation = new UpdatePreparation(downloader, new SignedChecksumSource(downloader, package, [release.PublicKey]));
        var installer = await preparation.PrepareAsync(package, release.Folder,
            new InlineProgress<(UpdateStep Step, DownloadProgress Progress)>(step => steps.Add(step.Step)), default);

        Assert.Equal(Path.Combine(release.Folder, SignedRelease.Installer), installer.Path);
        Assert.Equal(release.Program, File.ReadAllBytes(installer.Path));
        Assert.Equal(UpdateStep.Checking, steps[0]);
        Assert.Contains(UpdateStep.Downloading, steps);
        Assert.Equal(UpdateStep.Verifying, steps[^1]);
        // The order that matters: the signed list first, the installer last.
        var paths = release.Web.Requests.Select(request => request.RequestUri!.AbsolutePath).ToList();
        Assert.True(paths.IndexOf("/ziago/Lumerist/releases/download/v0.9.2/SHA256SUMS.sig") < paths.IndexOf("/ziago/Lumerist/releases/download/v0.9.2/" + SignedRelease.Installer));

        using (var locked = installer.OpenVerified())
        {
            // Nobody changes it while it is open for running (Windows enforces the sharing mode; Unix only advises it).
            if (OperatingSystem.IsWindows()) Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(installer.Path, [1]));
        }
        // A change after the check is caught before running.
        File.WriteAllBytes(installer.Path, [.. release.Program[..^1], 0]);
        Assert.Equal("The installer changed on disk after it was checked.", Assert.Throws<UpdateFailure>(installer.OpenVerified).Message);
    }

    [Fact]
    public async Task NothingIsDownloadedWithoutAValidSignature()
    {
        using var release = new SignedRelease();
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var package = release.Publish(signer: stranger);
        var failure = await Assert.ThrowsAsync<UpdateFailure>(() => release.PrepareAsync(package));
        Assert.Equal("The release's checksums aren't signed with Lumerist's key.", failure.Message);
        Assert.DoesNotContain(release.Web.Requests, request => request.RequestUri!.AbsolutePath.EndsWith(".exe", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(release.Folder));

        var noKey = await Assert.ThrowsAsync<UpdateFailure>(() => release.PrepareAsync(release.Publish(), keys: []));
        Assert.Equal("This copy of Lumerist has no key to check updates with.", noKey.Message);
    }

    [Fact]
    public async Task EveryChecksumMustAgree()
    {
        async Task<string> Failure(Func<SignedRelease, UpdatePackage> publish, Action<SignedRelease>? change = null)
        {
            using var release = new SignedRelease();
            var package = publish(release);
            change?.Invoke(release);
            return (await Assert.ThrowsAsync<UpdateFailure>(() => release.PrepareAsync(package))).Message;
        }

        Assert.Equal("The download doesn't match the release's signed checksum.",
            await Failure(release => release.Publish(listed: new string('2', 64), shaFile: $"{new string('2', 64)}  {SignedRelease.Installer}",
                digest: "sha256:" + new string('2', 64))));
        Assert.Equal("The download doesn't match GitHub's checksum.",
            await Failure(release => release.Publish(digest: "sha256:" + new string('3', 64))));
        Assert.Equal("The download doesn't match the installer's .sha256 file.",
            await Failure(release => release.Publish(shaFile: $"{new string('4', 64)}  {SignedRelease.Installer}")));
        Assert.Equal("The installer's .sha256 file can't be read.",
            await Failure(release => release.Publish(shaFile: $"{release.ProgramSha256}  Lumerist-0.9.2-windows-arm64-setup.exe")));
        // The served installer differs from the signed list.
        Assert.Equal("The download doesn't match the release's signed checksum.",
            await Failure(release => release.Publish(), release => release.Web.Serve("https://release-assets.githubusercontent.com/x?sig=1",
                [.. release.Program[..^1], 0])));
        // Or from the size GitHub lists.
        Assert.Equal("The download isn't the size GitHub lists.",
            await Failure(release => release.Publish(), release => release.Web.Serve("https://release-assets.githubusercontent.com/x?sig=1",
                [.. release.Program, 0])));
    }

    [Fact]
    public async Task OnlyAWindowsProgramIsReady()
    {
        using var release = new SignedRelease { Program = "PK not a program"u8.ToArray() };
        var failure = await Assert.ThrowsAsync<UpdateFailure>(() => release.PrepareAsync(release.Publish(serveChecksum: false)));
        Assert.Equal("The download isn't a Windows program.", failure.Message);
    }

    [Fact]
    public async Task AnInstallerMissingFromTheSignedListIsRefused()
    {
        using var release = new SignedRelease();
        var package = release.Publish();
        // Same sizes as the published files, another list.
        var list = Encoding.UTF8.GetBytes($"{new string('1', 64)}  Lumerist-0.9.2.dmg\n{new string('5', 64)}  Lumerist-0.9.2-windows-x64.zip\n");
        var signature = Sign(release.Key, list);
        release.Web.Serve(Download + "SHA256SUMS", list);
        release.Web.Serve(Download + "SHA256SUMS.sig", signature);
        package = package with
        {
            ChecksumList = package.ChecksumList with { Size = list.Length },
            Signature = package.Signature with { Size = signature.Length },
        };
        var failure = await Assert.ThrowsAsync<UpdateFailure>(() => release.PrepareAsync(package));
        Assert.Equal("The installer isn't in the release's signed checksums.", failure.Message);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>A pretend web: answers by exact URL, records every request, never opens a socket.</summary>
public sealed class FakeWeb : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> routes = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Serve(string url, byte[] body, bool announceLength = true) => routes[url] = () =>
    {
        HttpContent content = announceLength ? new ByteArrayContent(body) : new StreamContent(new UnannouncedStream(body));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    };

    public void Redirect(string url, string location) => routes[url] = () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    };

    public void Fail(string url, HttpStatusCode status) => routes[url] = () => new HttpResponseMessage(status);

    public void Throw(string url, Exception error) => routes[url] = () => throw error;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var answer)
            ? answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>A body without Content-Length, as a chunked answer arrives.</summary>
    private sealed class UnannouncedStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
