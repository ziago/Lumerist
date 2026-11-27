using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumerist.Core.Tests;

/// <summary>
/// Runs Shared/fixtures/updates (format: Shared/fixtures/README.md, "Update
/// cases"): the choice of release and files, the URL rules, the checksums and
/// their signature, the highlights and the update window's timing. The Mac
/// runs the same files in Tests/LumeristCoreTests/UpdateFixtureTests.swift.
/// </summary>
public class UpdateFixtureTests
{
    [Fact]
    public void SharedUpdateCases() => Fixtures.Run("updates", Fixtures.Cases("updates"), (testCase, id) => id.Split('/') switch
    {
        [_, "releases", ..] => Releases(testCase),
        [_, "urls", ..] => Urls(testCase),
        [_, "checksums", .., var kind] => Checksums(testCase, kind),
        [_, "highlights", ..] => Highlights(testCase),
        [_, "prompt", ..] => Prompt(testCase),
        _ => throw new FixtureException("unknown kind of update case"),
    });

    /// <summary>The C# list of keys is exactly Shared/update-public-keys.json (check-parity checks the same).</summary>
    [Fact]
    public void TrustedKeysMatchTheSharedFile()
    {
        var file = Fixtures.Object(Path.Combine("Shared", "update-public-keys.json"));
        var shared = file["updatePublicKeys"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
        Assert.Equal(shared, UpdateSignature.UpdatePublicKeys);
        Assert.NotEmpty(UpdateSignature.TrustedKeys);
        Assert.All(UpdateSignature.TrustedKeys, key => Assert.True(UpdateSignature.IsP256PublicKey(key)));
        Assert.Equal(shared, UpdateSignature.ParseKeyList(file.ToJsonString())!.Select(Convert.ToBase64String));
    }

    // Releases ------------------------------------------------------------------------

    private static readonly string[] Platforms = ["mac", "windows-x64", "windows-arm64"];

    private static IEnumerable<string> Releases(JsonObject testCase)
    {
        var current = FixtureValues.Text(testCase["current"], "current");
        var json = testCase["releases"]?.ToJsonString() ?? throw new FixtureException("a releases case needs releases");
        var releases = UpdateCheck.DecodeReleases(Encoding.UTF8.GetBytes(json)) ?? throw new FixtureException("the releases don't decode");
        var allowFiles = testCase["allowFileURLs"] is { } flag && FixtureValues.Flag(flag, "allowFileURLs");
        var update = UpdateCheck.Newer(releases, current);
        if (testCase["expect"] is not JsonObject expected)
        {
            if (update is not null) yield return $"offered {update.Version} instead of nothing";
            yield break;
        }
        if (update is null)
        {
            yield return "offered nothing";
            yield break;
        }
        if (update.Version.ToString() != FixtureValues.OptionalText(expected["version"])) yield return $"version {update.Version}";
        if (update.Tag != FixtureValues.OptionalText(expected["tag"])) yield return $"tag {update.Tag}";
        if (update.Page.OriginalString != FixtureValues.OptionalText(expected["page"])) yield return $"page {update.Page.OriginalString}";
        var platforms = expected["platforms"] as JsonObject ?? [];
        foreach (var platform in Platforms)
        {
            var name = UpdateAssets.PlatformInstallerName(platform, update.Version.ToString())!;
            var files = (UpdateAssets.Files(update.Release, update.Version, name, allowFiles) as UpdateChoice.Ready)?.Package;
            if (platforms[platform] is not JsonObject wanted)
            {
                if (files is not null) yield return $"{platform}: {files.Installer.Name} instead of none";
                continue;
            }
            if (files is null)
            {
                yield return $"{platform}: none";
                continue;
            }
            foreach (var (role, file) in new[] { ("installer", files.Installer), ("checksum", files.Checksum), ("sums", files.ChecksumList), ("signature", files.Signature) })
            {
                var want = Describe(wanted[role] as JsonObject);
                if (Describe(file) != want) yield return $"{platform} {role}: {Describe(file)} ≠ {want}";
            }
        }
    }

    private static string Describe(UpdateFile? file) =>
        file is null ? "none" : $"{file.Name} {file.Url.OriginalString} {file.Size} {file.Sha256 ?? "-"}";

    private static string Describe(JsonObject? asset) => asset is null
        ? "none"
        : $"{FixtureValues.OptionalText(asset["name"]) ?? "?"} {FixtureValues.OptionalText(asset["url"]) ?? "?"} "
          + $"{(asset["size"] is { } size ? size.GetValue<long>() : -1)} {FixtureValues.OptionalText(asset["sha256"]) ?? "-"}";

    // URLs ------------------------------------------------------------------------------

    private static IEnumerable<string> Urls(JsonObject testCase)
    {
        var text = FixtureValues.Text(testCase["url"], "url");
        var expected = testCase["expect"] as JsonObject ?? throw new FixtureException("a urls case needs expect");
        var allowFiles = testCase["allowFileURLs"] is { } flag && FixtureValues.Flag(flag, "allowFileURLs");
        var parsed = Uri.TryCreate(text, UriKind.Absolute, out var url) ? url : null;
        if (expected["hop"] is { } hop)
        {
            var got = UpdateDownloadRules.IsTrustedHop(text, allowFiles);
            if (got != FixtureValues.Flag(hop, "hop")) yield return $"hop {got}";
        }
        if (expected["asset"] is { } asset)
        {
            var got = UpdateDownloadRules.IsTrustedAssetUrl(text, FixtureValues.Text(testCase["tag"], "tag"), FixtureValues.Text(testCase["name"], "name"), allowFiles);
            if (got != FixtureValues.Flag(asset, "asset")) yield return $"asset {got}";
        }
        if (expected["releasePage"] is { } page)
        {
            var got = parsed is not null && UpdateCheck.IsTrustedReleasePage(parsed);
            if (got != FixtureValues.Flag(page, "releasePage")) yield return $"releasePage {got}";
        }
    }

    // Checksums ---------------------------------------------------------------------------

    private static IEnumerable<string> Checksums(JsonObject testCase, string kind)
    {
        if (kind.StartsWith("sha256-file-", StringComparison.Ordinal))
        {
            var name = FixtureValues.Text(testCase["name"], "name");
            var data = Encoding.UTF8.GetBytes(FixtureValues.Text(testCase["checksumFile"], "checksumFile"));
            var got = UpdateChecksums.ReadSha256File(data, name);
            return Compare(got.Sha256, got.Error, testCase["expect"]);
        }
        if (kind.StartsWith("list-", StringComparison.Ordinal))
        {
            var sums = Encoding.UTF8.GetBytes(FixtureValues.Text(testCase["sums"], "sums"));
            var signature = Encoding.UTF8.GetBytes(FixtureValues.Text(testCase["signature"], "signature"));
            var keys = (testCase["keys"] as JsonArray ?? throw new FixtureException("a list case needs keys"))
                .Select(node => FixtureValues.Text(node, "key")).Select(Key).ToList();
            var name = FixtureValues.Text(testCase["name"], "name");
            string? sha256 = null, error = null;
            if (!UpdateSignature.Verify(sums, signature, keys)) error = "badSignature";
            else if (ChecksumList.Parse(sums) is not { } list) error = "malformedList";
            else if (list.Sha256For(name) is not { } listed) error = "notListed";
            else sha256 = listed;
            return Compare(sha256, error, testCase["expect"]);
        }
        if (kind.StartsWith("agree-", StringComparison.Ordinal))
        {
            var source = UpdateChecksums.Disagreement(FixtureValues.Text(testCase["actual"], "actual"), FixtureValues.Text(testCase["listed"], "listed"),
                FixtureValues.OptionalText(testCase["checksumFile"]), FixtureValues.OptionalText(testCase["digest"]));
            var outcome = source switch
            {
                null => "ok",
                UpdateHashSource.List => "mismatch list",
                UpdateHashSource.ChecksumFile => "mismatch checksumFile",
                _ => "mismatch digest",
            };
            var expected = testCase["expect"] is JsonObject error
                ? $"{FixtureValues.OptionalText(error["error"]) ?? "?"} {FixtureValues.OptionalText(error["source"]) ?? "?"}"
                : FixtureValues.OptionalText(testCase["expect"]) ?? "?";
            return outcome == expected ? [] : [outcome];
        }
        throw new FixtureException("unknown kind of checksums case");
    }

    /// <summary>A key as the case gives it: base64 that may not decode (then an unreadable key, skipped).</summary>
    private static byte[] Key(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static IEnumerable<string> Compare(string? sha256, string? error, JsonNode? expect)
    {
        var expected = expect as JsonObject ?? [];
        if (error is not null) return error == FixtureValues.OptionalText(expected["error"]) ? [] : [$"error {error}"];
        return sha256 == FixtureValues.OptionalText(expected["sha256"]) ? [] : [$"sha256 {sha256}"];
    }

    // Highlights --------------------------------------------------------------------------

    private static IEnumerable<string> Highlights(JsonObject testCase)
    {
        var body = FixtureValues.Text(testCase["body"], "body");
        var expected = (testCase["expect"] as JsonArray ?? throw new FixtureException("a highlights case needs expect"))
            .Select(node => FixtureValues.Text(node, "highlight")).ToList();
        var got = ReleaseHighlights.Parse(body);
        if (!got.SequenceEqual(expected)) yield return "got " + JsonSerializer.Serialize(got);
    }

    // Prompt -----------------------------------------------------------------------------

    private static DateTimeOffset? Date(JsonNode? node) =>
        FixtureValues.OptionalText(node) is { } text ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) : null;

    private static IEnumerable<string> Prompt(JsonObject testCase)
    {
        switch (FixtureValues.OptionalText(testCase["action"]))
        {
            case "later":
            {
                var now = Date(testCase["now"]) ?? throw new FixtureException("a later case needs now");
                var expected = Date((testCase["expect"] as JsonObject)?["laterUntil"]) ?? throw new FixtureException("a later case needs expect.laterUntil");
                var got = new UpdatePrompt().Later(now).LaterUntil;
                if (got != expected) yield return $"laterUntil {got:O}";
                break;
            }
            case "skip":
            {
                var version = FixtureValues.Text(testCase["version"], "version");
                var expected = FixtureValues.Text((testCase["expect"] as JsonObject)?["skippedVersion"], "expect.skippedVersion");
                var got = new UpdatePrompt().Skip(version).SkippedVersion;
                if (got != expected) yield return $"skippedVersion {got}";
                break;
            }
            default:
            {
                var trigger = FixtureValues.Text(testCase["trigger"], "trigger") switch
                {
                    "launch" => UpdatePrompt.Trigger.Launch,
                    "manual" => UpdatePrompt.Trigger.Manual,
                    var other => throw new FixtureException("unknown trigger " + other),
                };
                var schedule = new UpdatePrompt(FixtureValues.OptionalText(testCase["skippedVersion"]), Date(testCase["laterUntil"]));
                var got = schedule.ShouldShow(FixtureValues.Text(testCase["available"], "available"), FixtureValues.Text(testCase["current"], "current"),
                    trigger, Date(testCase["now"]) ?? throw new FixtureException("a prompt case needs now"));
                if (got != FixtureValues.Flag(testCase["expect"], "expect")) yield return $"show {got}";
                break;
            }
        }
    }
}
