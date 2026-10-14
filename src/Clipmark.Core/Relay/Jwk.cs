using System.Security.Cryptography;
using System.Text;

namespace Clipmark.Core.Relay;

/// <summary>P-256 public key as a JWK and its RFC 7638 thumbprint, which is the relay's install_id.</summary>
public static class Jwk
{
    /// <summary>Base64url x and y of the public point, each padded to 32 bytes.</summary>
    public static (string X, string Y) Coordinates(ECDsa key)
    {
        var q = key.ExportParameters(false).Q;
        return (RelaySigning.Base64Url(Pad32(q.X!)), RelaySigning.Base64Url(Pad32(q.Y!)));
    }

    /// <summary>The members in lexicographic order without whitespace, as RFC 7638 hashes them.</summary>
    public static string ThumbprintInput(string x, string y) => $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{x}\",\"y\":\"{y}\"}}";

    public static string Thumbprint(string x, string y) =>
        RelaySigning.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(ThumbprintInput(x, y))));

    private static byte[] Pad32(byte[] value)
    {
        if (value.Length == 32)
            return value;
        var padded = new byte[32];
        value.AsSpan(Math.Max(0, value.Length - 32)).CopyTo(padded.AsSpan(Math.Max(0, 32 - value.Length)));
        return padded;
    }
}
