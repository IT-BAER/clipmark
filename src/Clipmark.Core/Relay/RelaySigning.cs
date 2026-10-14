using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clipmark.Core.Relay;

/// <summary>The four signed headers of one relay request (releash spec 5.3).</summary>
public sealed record RelayEnvelope(string InstallId, long Timestamp, string Nonce, string Signature)
{
    public IEnumerable<(string Name, string Value)> Headers =>
    [
        ("X-releash-Install", InstallId),
        ("X-releash-Timestamp", Timestamp.ToString(CultureInfo.InvariantCulture)),
        ("X-releash-Nonce", Nonce),
        ("X-releash-Signature", Signature),
    ];
}

/// <summary>Canonical string and ES256 envelope of releash spec 5.1; the key stays with the caller (CNG in the app, a test key here).</summary>
public static class RelaySigning
{
    public const string Prefix = "releash-v1";

    /// <summary>Signs with <paramref name="sign"/>, which must return the 64-byte IEEE P1363 r||s of a P-256/SHA-256 signature.</summary>
    public static RelayEnvelope Sign(string method, string path, string installId, byte[] body, Func<byte[], byte[]> sign, long timestamp, string? nonce = null)
    {
        nonce ??= NewNonce();
        byte[] signature = sign(Encoding.UTF8.GetBytes(Canonical(method, path, installId, timestamp, nonce, body)));
        if (signature.Length != 64)
            throw new CryptographicException($"Expected a 64-byte P1363 signature, got {signature.Length} bytes.");
        return new RelayEnvelope(installId, timestamp, nonce, Base64Url(signature));
    }

    public static string Canonical(string method, string path, string installId, long timestamp, string nonce, byte[] body) =>
        $"{Prefix}\n{method.ToUpperInvariant()}\n{path}\n{installId}\n{timestamp.ToString(CultureInfo.InvariantCulture)}\n{nonce}\n{Sha256Hex(body)}";

    public static string Sha256Hex(byte[] body) => Convert.ToHexStringLower(SHA256.HashData(body));

    /// <summary>16 random bytes, base64url without padding (22 characters).</summary>
    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
