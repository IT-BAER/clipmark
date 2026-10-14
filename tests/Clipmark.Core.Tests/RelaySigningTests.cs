using System.Security.Cryptography;
using System.Text;
using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

/// <summary>The releash signing vector (releash repo testdata/signing/v1.json, test key only): the client must reproduce it byte for byte.</summary>
public sealed class RelaySigningTests
{
    private const string PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nMIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgAd+7c9LK13rmhlbO\nKEnYyCuSLAKbv8DwzoE9M4Pny+yhRANCAAT9GgUBp/DozKw/0ZP8fyyrotkrGut/\n4qbIMcK9iKRxnl/MhvXt/fzLyszA2qbwetv8CcOxRP7xyVV0N4OGMVXW\n-----END PRIVATE KEY-----";
    private const string X = "_RoFAafw6MysP9GT_H8sq6LZKxrrf-KmyDHCvYikcZ4";
    private const string Y = "X8yG9e39_MvKzMDapvB62_wJw7FE_vHJVXQ3g4YxVdY";
    private const string ThumbprintInput = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"_RoFAafw6MysP9GT_H8sq6LZKxrrf-KmyDHCvYikcZ4\",\"y\":\"X8yG9e39_MvKzMDapvB62_wJw7FE_vHJVXQ3g4YxVdY\"}";
    private const string InstallId = "wDEqxDa9R1vf1h96oQnyEQyAhNqmR3A3mr0ui4vCICM";
    private const string Path = "/v1/tasks/clipmark/ask";
    private const long Timestamp = 1760400000;
    private const string Nonce = "AxQlNkdYaXqLnK2-z-DxAg";
    private const string Body = "{\"input\":{\"question\":\"hi\"},\"stream\":false}";
    private const string BodySha256Hex = "84cd647d78e7eb2ee3b9b891365c55cd8d16f0e084a694a431456122d614c229";
    private const string Canonical = "releash-v1\nPOST\n/v1/tasks/clipmark/ask\nwDEqxDa9R1vf1h96oQnyEQyAhNqmR3A3mr0ui4vCICM\n1760400000\nAxQlNkdYaXqLnK2-z-DxAg\n84cd647d78e7eb2ee3b9b891365c55cd8d16f0e084a694a431456122d614c229";
    private const string Signature = "n4kdlzkXdFi06LQWsMYe6tXlP9vTepB1SynAxWOlV-IuAjLitx-xEOGBsWk0GT5oqLl5v3bAOGZSPpXEJb6RBg";

    private static ECDsa VectorKey()
    {
        var key = ECDsa.Create();
        key.ImportFromPem(PrivateKeyPem);
        return key;
    }

    private static byte[] Base64UrlDecode(string s) =>
        Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));

    [Fact]
    public void Public_key_exports_as_the_vector_jwk_and_its_thumbprint_is_the_install_id()
    {
        using var key = VectorKey();

        var (x, y) = Jwk.Coordinates(key);

        Assert.Equal(X, x);
        Assert.Equal(Y, y);
        Assert.Equal(ThumbprintInput, Jwk.ThumbprintInput(x, y));
        Assert.Equal(InstallId, Jwk.Thumbprint(x, y));
    }

    [Fact]
    public void Canonical_string_and_body_hash_match_the_vector()
    {
        byte[] body = Encoding.UTF8.GetBytes(Body);

        Assert.Equal(BodySha256Hex, RelaySigning.Sha256Hex(body));
        Assert.Equal(Canonical, RelaySigning.Canonical("POST", Path, InstallId, Timestamp, Nonce, body));
    }

    [Fact]
    public void Vector_signature_verifies_over_the_canonical_string()
    {
        using var key = VectorKey();

        byte[] signature = Base64UrlDecode(Signature);

        Assert.Equal(64, signature.Length);
        Assert.True(key.VerifyData(Encoding.UTF8.GetBytes(Canonical), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void Signing_produces_the_four_envelope_headers_with_a_verifiable_64_byte_signature()
    {
        using var key = VectorKey();
        byte[] body = Encoding.UTF8.GetBytes(Body);

        var envelope = RelaySigning.Sign("POST", Path, InstallId, body,
            data => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), Timestamp, Nonce);

        Assert.Equal(InstallId, envelope.InstallId);
        Assert.Equal(Timestamp, envelope.Timestamp);
        Assert.Equal(Nonce, envelope.Nonce);
        byte[] signature = Base64UrlDecode(envelope.Signature);
        Assert.Equal(64, signature.Length);
        Assert.DoesNotContain('=', envelope.Signature);
        Assert.True(key.VerifyData(Encoding.UTF8.GetBytes(Canonical), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(
            [("X-releash-Install", InstallId), ("X-releash-Timestamp", "1760400000"), ("X-releash-Nonce", Nonce), ("X-releash-Signature", envelope.Signature)],
            envelope.Headers);
    }

    [Fact]
    public void A_fresh_nonce_is_16_random_bytes_in_unpadded_base64url()
    {
        string a = RelaySigning.NewNonce(), b = RelaySigning.NewNonce();

        Assert.Equal(22, a.Length);
        Assert.Equal(16, Base64UrlDecode(a).Length);
        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
    }
}
