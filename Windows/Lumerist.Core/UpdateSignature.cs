using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumerist.Core;

/// <summary>
/// A release's <c>SHA256SUMS</c> (the Mac's SignedChecksumList): UTF-8 with
/// LF line breaks, one <c>&lt;64 lower-case hex&gt;  &lt;file name&gt;</c> line
/// (two spaces) per file; the last line break may be missing. A name is ASCII
/// letters, digits, ".", "_", "+" and "-", and not "." or "..". The order is
/// the signer's (not checked again). It is read only after its signature
/// checked out (<see cref="UpdateSignature"/>), from the exact bytes that were signed.
/// </summary>
public sealed partial class ChecksumList
{
    private readonly Dictionary<string, string> digests;

    private ChecksumList(Dictionary<string, string> digests) => this.digests = digests;

    public IReadOnlyDictionary<string, string> Entries => digests;

    /// <summary>The listed SHA-256 of <paramref name="fileName"/> (exact name), or null when it is not listed.</summary>
    public string? Sha256For(string fileName) => digests.GetValueOrDefault(fileName);

    /// <summary>Null for a line that isn't that format, a blank line, a CR, a name listed twice, invalid UTF-8, or no line.</summary>
    public static ChecksumList? Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Contains((byte)'\r')) return null;
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(utf8);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        var rows = text.Split('\n').ToList();
        if (rows.Count > 0 && rows[^1].Length == 0) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return null;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var match = Line().Match(row);
            if (!match.Success) return null;
            var name = match.Groups[2].Value;
            if (name is "." or ".." || !digests.TryAdd(name, match.Groups[1].Value)) return null;
        }
        return new ChecksumList(digests);
    }

    [GeneratedRegex(@"\A([0-9a-f]{64})  ([A-Za-z0-9._+-]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Line();
}

/// <summary>
/// Checks <c>SHA256SUMS.sig</c>: an ECDSA P-256 / SHA-256 signature (DER,
/// base64 on one line) over the exact bytes of <c>SHA256SUMS</c>, made with
/// a key kept off GitHub. The Windows packages are not code-signed and their
/// .sha256 files come from the same release, so this signature is what
/// tells an installer made by Lumerist's maker from any other.
/// </summary>
public static partial class UpdateSignature
{
    /// <summary>The OID of the P-256 curve (secp256r1).</summary>
    private const string P256 = "1.2.840.10045.3.1.7";

    /// <summary>
    /// The keys a release may be signed with (SPKI DER, base64; ECDSA P-256).
    /// A signature valid under any of them is accepted, so a new key can join
    /// before the old one goes. The one source is Shared/update-public-keys.json:
    /// Scripts/check-parity.py and a test check that this list equals it.
    /// </summary>
    public static readonly string[] UpdatePublicKeys =
    [
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEt37ZfOYd5bo4/ZKP73l2KTyzgZI9Ih0nlBof4WjmWuaoFd3JYfLtZah8HT6R2DP62EYmSxeW2N25UIN1DIhzVA==",
    ];

    /// <summary>The list above, decoded.</summary>
    public static IReadOnlyList<byte[]> TrustedKeys { get; } = [.. UpdatePublicKeys.Select(Convert.FromBase64String)];

    /// <summary>
    /// <c>{"updatePublicKeys": ["&lt;SPKI DER, base64&gt;", …]}</c>: each key
    /// must be a P-256 public key. Null when the file is not exactly that.
    /// </summary>
    public static IReadOnlyList<byte[]>? ParseKeyList(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("updatePublicKeys", out var list)
                || list.ValueKind != JsonValueKind.Array) return null;
            var keys = new List<byte[]>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return null;
                var key = Convert.FromBase64String(item.GetString()!);
                if (!IsP256PublicKey(key)) return null;
                keys.Add(key);
            }
            return keys;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>A SubjectPublicKeyInfo that holds a P-256 public key.</summary>
    public static bool IsP256PublicKey(byte[] subjectPublicKeyInfo)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var read);
            return read == subjectPublicKeyInfo.Length && ecdsa.KeySize == 256 && ecdsa.ExportParameters(false).Curve.Oid.Value == P256;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// <paramref name="signatureFile"/> signs the exact bytes of
    /// <paramref name="checksumList"/> under at least one of
    /// <paramref name="keys"/>: base64 (standard alphabet, padded, nothing
    /// inside but base64; spaces, tabs and line breaks around it ignored) of a
    /// DER ECDSA P-256 / SHA-256 signature. Keys that can't be read are
    /// skipped; anything unreadable is false.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> checksumList, ReadOnlySpan<byte> signatureFile, IEnumerable<byte[]> keys)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(signatureFile).Trim(' ', '\t', '\r', '\n');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        if (text.Length == 0 || text.Length % 4 != 0 || !Base64().IsMatch(text)) return false;
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return false;
        }
        var data = checksumList.ToArray();
        foreach (var key in keys)
        {
            if (!IsP256PublicKey(key)) continue;
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(key, out _);
                if (ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) return true;
            }
            catch (CryptographicException)
            {
                // Not a signature this key can read.
            }
        }
        return false;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9+/]+={0,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Base64();
}
