using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Lumerist.Core;

/// <summary>
/// Why "Update now" stopped, in a short sentence for the update window. Nothing
/// was run, and the installed Lumerist is as it was.
/// </summary>
public sealed class UpdateFailure(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>How far a download got; <c>Total</c> is null when the size is unknown.</summary>
public readonly record struct DownloadProgress(long Received, long? Total);

/// <summary>
/// Downloads the files of an update over https from GitHub only. Redirects
/// are never followed automatically: each one is checked with
/// <see cref="UpdateDownloadRules.IsTrustedHop"/> first, so no hop leaves
/// GitHub, and no cookie, credential or header goes along. Every download has
/// a size limit and timeouts. The handler is injectable so tests never touch
/// the network.
/// </summary>
public sealed class UpdateDownloader : IDisposable
{
    /// <summary>From sending a request to its first answer.</summary>
    public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait for more data during a download.</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>A whole download, redirects included.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(20);

    private readonly HttpClient http;
    private readonly bool allowLocalFiles;

    /// <param name="handler">For tests; null uses a handler that follows no redirect and keeps no cookies.</param>
    /// <param name="allowLocalFiles">Debug builds' test feed: file:// URLs are read from disk (https keeps every rule).</param>
    public UpdateDownloader(HttpMessageHandler? handler = null, bool allowLocalFiles = false)
    {
        this.allowLocalFiles = allowLocalFiles;
        var owned = handler is null;
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = ResponseTimeout,
        };
        http = new HttpClient(handler, disposeHandler: owned) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>A small file (SHA256SUMS, its signature, a .sha256), in memory.</summary>
    public async Task<byte[]> FetchAsync(Uri url, long limit, CancellationToken cancellationToken, long? expectedSize = null)
    {
        using var memory = new MemoryStream();
        await DownloadAsync(url, memory, limit, expectedSize, progress: null, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    /// <summary>One file of a release: at most its role's limit, and exactly the size the feed declares.</summary>
    public Task<byte[]> FetchAsync(UpdateFile file, long limit, CancellationToken cancellationToken) =>
        FetchAsync(file.Url, limit, cancellationToken, file.Size);

    /// <summary>
    /// Copies <paramref name="url"/> into <paramref name="destination"/> and
    /// returns its SHA-256 (lower-case hex) and length. Throws
    /// <see cref="UpdateFailure"/>, or <see cref="OperationCanceledException"/>
    /// when <paramref name="cancellationToken"/> was cancelled.
    /// </summary>
    /// <param name="limit">The most bytes accepted.</param>
    /// <param name="expectedSize">The size the feed gave, when it gave one: the file must have exactly that many bytes.</param>
    public async Task<(string Sha256, long Length)> DownloadAsync(Uri url, Stream destination, long limit, long? expectedSize,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        whole.CancelAfter(TotalTimeout);
        try
        {
            if (allowLocalFiles && UpdateDownloadRules.IsLocalFile(url))
            {
                await using var file = new FileStream(url.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, useAsync: true);
                if (file.Length > limit) throw TooLarge();
                return await CopyAsync(file, destination, limit, expectedSize, progress, whole.Token).ConfigureAwait(false);
            }
            var current = url;
            for (var redirects = 0; ; redirects++)
            {
                if (!UpdateDownloadRules.IsTrustedHop(current))
                    throw new UpdateFailure($"The download was sent to {Describe(current)}, which isn't GitHub.");
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("User-Agent", "Lumerist");
                request.Headers.TryAddWithoutValidation("Accept", "application/octet-stream");
                HttpResponseMessage response;
                using (var answer = CancellationTokenSource.CreateLinkedTokenSource(whole.Token))
                {
                    answer.CancelAfter(ResponseTimeout);
                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, answer.Token).ConfigureAwait(false);
                }
                using (response)
                {
                    if (IsRedirect(response.StatusCode))
                    {
                        if (redirects >= UpdateDownloadRules.MaxRedirects) throw new UpdateFailure("GitHub redirected the download too many times.");
                        var location = response.Headers.Location ?? throw new UpdateFailure("GitHub redirected the download nowhere.");
                        current = location.IsAbsoluteUri ? location : new Uri(current, location);
                        continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new UpdateFailure($"GitHub answered with status {(int)response.StatusCode}.");
                    var announced = response.Content.Headers.ContentLength;
                    if (announced > limit) throw TooLarge();
                    if (expectedSize is { } size && announced is { } length && length != size)
                        throw new UpdateFailure("The download isn't the size GitHub lists.");
                    await using var body = await response.Content.ReadAsStreamAsync(whole.Token).ConfigureAwait(false);
                    return await CopyAsync(body, destination, limit, expectedSize ?? announced, progress, whole.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateFailure("The download timed out.");
        }
        catch (HttpRequestException error)
        {
            throw new UpdateFailure($"Couldn't reach GitHub: {error.Message}", error);
        }
        catch (IOException error)
        {
            throw new UpdateFailure($"The download stopped: {error.Message}", error);
        }
    }

    /// <summary>Copies, hashing as it goes; at most <paramref name="limit"/> bytes, and no wait longer than <see cref="StallTimeout"/>.</summary>
    private static async Task<(string Sha256, long Length)> CopyAsync(Stream source, Stream destination, long limit, long? expected,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = new byte[81920];
        long received = 0, reported = 0;
        progress?.Report(new DownloadProgress(0, expected));
        while (true)
        {
            stall.CancelAfter(StallTimeout);
            var read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            if (read == 0) break;
            received += read;
            if (received > limit || received > expected) throw TooLarge();
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (received - reported >= 256 * 1024)
            {
                reported = received;
                progress?.Report(new DownloadProgress(received, expected));
            }
        }
        if (expected is { } size && received != size) throw new UpdateFailure("The download ended too early.");
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DownloadProgress(received, expected ?? received));
        return (UpdateChecksums.Hex(hash.GetHashAndReset()), received);
    }

    private static UpdateFailure TooLarge() => new("The download is larger than an update can be.");

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>The host only, never a full address with its query (signed GitHub links).</summary>
    private static string Describe(Uri url) => url.IsAbsoluteUri && url.Host.Length > 0 ? $"{url.Scheme}://{url.IdnHost}" : "an unknown address";

    public void Dispose() => http.Dispose();
}

/// <summary>
/// Where the expected SHA-256 of a release file comes from. It must be a
/// source that proves the release is Lumerist's (today: the signed
/// SHA256SUMS, <see cref="SignedChecksumSource"/>), never the release's own
/// unsigned files alone.
/// </summary>
public interface IUpdateHashSource
{
    /// <summary>The SHA-256 (lower-case hex) of <paramref name="fileName"/>; throws <see cref="UpdateFailure"/> when it can't vouch for it.</summary>
    Task<string> ExpectedSha256Async(string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// The release's SHA256SUMS, trusted only once SHA256SUMS.sig verifies under
/// one of the built-in keys (<see cref="UpdateSignature.TrustedKeys"/>).
/// Both files are fetched once, in memory.
/// </summary>
public sealed class SignedChecksumSource(UpdateDownloader downloader, UpdatePackage package, IReadOnlyList<byte[]> keys) : IUpdateHashSource
{
    private ChecksumList? list;

    public async Task<string> ExpectedSha256Async(string fileName, CancellationToken cancellationToken)
    {
        list ??= await LoadAsync(cancellationToken).ConfigureAwait(false);
        return list.Sha256For(fileName) ?? throw new UpdateFailure("The installer isn't in the release's signed checksums.");
    }

    private async Task<ChecksumList> LoadAsync(CancellationToken cancellationToken)
    {
        if (keys.Count == 0) throw new UpdateFailure("This copy of Lumerist has no key to check updates with.");
        var sums = await downloader.FetchAsync(package.ChecksumList, UpdateAssets.Limit.ChecksumList, cancellationToken).ConfigureAwait(false);
        var signature = await downloader.FetchAsync(package.Signature, UpdateAssets.Limit.Signature, cancellationToken).ConfigureAwait(false);
        if (!UpdateSignature.Verify(sums, signature, keys)) throw new UpdateFailure("The release's checksums aren't signed with Lumerist's key.");
        return ChecksumList.Parse(sums) ?? throw new UpdateFailure("The release's signed checksums can't be read.");
    }
}

/// <summary>What "Update now" is doing.</summary>
public enum UpdateStep
{
    /// <summary>Fetching and checking the signed checksums.</summary>
    Checking,
    Downloading,
    /// <summary>Checking the downloaded installer.</summary>
    Verifying,
}

/// <summary>
/// An installer on disk whose SHA-256 matched every source when it was
/// written. <see cref="OpenVerified"/> checks it again, under a lock, just
/// before it runs.
/// </summary>
public sealed record PreparedInstaller(string Path, string Sha256, long Length)
{
    /// <summary>
    /// Opens the file so that no one can change or delete it while it stays
    /// open, then checks its SHA-256, its size and the "MZ" of a Windows
    /// program once more. Keep the stream open until the installer started.
    /// </summary>
    public FileStream OpenVerified()
    {
        var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var (sha256, length, isProgram) = UpdateChecksums.Measure(stream);
            if (length != Length || !string.Equals(sha256, Sha256, StringComparison.Ordinal))
                throw new UpdateFailure("The installer changed on disk after it was checked.");
            if (!isProgram) throw new UpdateFailure("The download isn't a Windows program.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}

/// <summary>
/// "Update now" up to the point of running the installer, in the order both
/// apps keep (Shared/fixtures/updates), so an unproven file never runs:
/// <list type="number">
/// <item>the expected SHA-256 from a source that proves the release (the signed SHA256SUMS, which must list the installer);</item>
/// <item>the installer's own .sha256, when the release has one, must be readable;</item>
/// <item>the installer is downloaded into <c>folder</c>; its SHA-256 must equal the listed one, and the .sha256 and GitHub's digest when present;</item>
/// <item>it must be a Windows program ("MZ").</item>
/// </list>
/// Anything else throws <see cref="UpdateFailure"/> and nothing is run.
/// </summary>
public sealed class UpdatePreparation(UpdateDownloader downloader, IUpdateHashSource hashes)
{
    /// <param name="folder">A new, empty folder only Lumerist uses (the app makes a random one under %TEMP%).</param>
    public async Task<PreparedInstaller> PrepareAsync(UpdatePackage package, string folder, IProgress<(UpdateStep Step, DownloadProgress Progress)>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report((UpdateStep.Checking, default));
        var listed = await hashes.ExpectedSha256Async(package.Installer.Name, cancellationToken).ConfigureAwait(false);
        if (!UpdateChecksums.IsSha256Hex(listed)) throw new UpdateFailure("The release's signed checksums can't be read.");
        string? checksumFile = null;
        if (package.Checksum is { } file)
        {
            var text = await downloader.FetchAsync(file, UpdateAssets.Limit.Checksum, cancellationToken).ConfigureAwait(false);
            checksumFile = UpdateChecksums.ParseSha256File(text, package.Installer.Name)
                ?? throw new UpdateFailure("The installer's .sha256 file can't be read.");
        }

        var path = System.IO.Path.Combine(folder, package.Installer.Name);
        var downloading = progress is null ? null : new Relay(step => progress.Report((UpdateStep.Downloading, step)));
        (string Sha256, long Length) result;
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            result = await downloader.DownloadAsync(package.Installer.Url, output, UpdateAssets.Limit.Installer,
                package.Installer.Size, downloading, cancellationToken).ConfigureAwait(false);
        }
        progress?.Report((UpdateStep.Verifying, new DownloadProgress(result.Length, result.Length)));
        switch (UpdateChecksums.Disagreement(result.Sha256, listed, checksumFile, package.Installer.Sha256))
        {
            case UpdateHashSource.List: throw new UpdateFailure("The download doesn't match the release's signed checksum.");
            case UpdateHashSource.ChecksumFile: throw new UpdateFailure("The download doesn't match the installer's .sha256 file.");
            case UpdateHashSource.Digest: throw new UpdateFailure("The download doesn't match GitHub's checksum.");
        }
        var installer = new PreparedInstaller(path, result.Sha256, result.Length);
        // The header too, before anything says it is ready.
        installer.OpenVerified().Dispose();
        return installer;
    }

    /// <summary>Passes each step on at once, in order (the caller's progress decides where it lands).</summary>
    private sealed class Relay(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
