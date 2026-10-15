using System.Security.Cryptography;
using System.Text;
using Clipmark.Core.Licensing;
using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class LicenceRequestTests
{
    private const string Code = "ABCDEFGH-IJKLMNOP-QRSTUVWX-YZ234567";
    private const string Jwk = "\"public_key\":{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"xx\",\"y\":\"yy\"}";

    private static readonly string V1Key = LicenseKey.Sign(new LicensePayload(7, new DateOnly(2026, 10, 1)), ECDsa.Create(ECCurve.NamedCurves.nistP256));

    private static string Text(byte[] body) => Encoding.UTF8.GetString(body);

    [Fact]
    public void Activate_with_a_code_sends_the_code_the_jwk_and_no_device_label()
    {
        Assert.Equal("{\"platform\":\"windows\",\"code\":\"" + Code + "\"," + Jwk + ",\"key_store\":\"tpm\",\"app_version\":\"1.1.0\"}",
            Text(LicenceRequest.BuildActivate("  " + Code + " ", "xx", "yy", "tpm", "1.1.0")));
    }

    [Fact]
    public void Activate_with_a_v1_key_sends_licence_key()
    {
        Assert.Equal("{\"platform\":\"windows\",\"licence_key\":\"" + V1Key + "\"," + Jwk + ",\"key_store\":\"software\",\"app_version\":\"1.1.0\"}",
            Text(LicenceRequest.BuildActivate(V1Key, "xx", "yy", "software", "1.1.0")));
    }

    [Fact]
    public void Trial_sends_the_device_fields_only()
    {
        Assert.Equal("{\"platform\":\"windows\"," + Jwk + ",\"key_store\":\"tpm\",\"app_version\":\"1.1.0\"}",
            Text(LicenceRequest.BuildTrial("xx", "yy", "tpm", "1.1.0")));
    }

    [Fact]
    public void Entitlement_and_deactivate_send_an_empty_object()
    {
        Assert.Equal("{}", Text(LicenceRequest.EmptyObject));
    }

    [Theory]
    [InlineData(Code, true)]
    [InlineData("abcdefghijklmnopqrstuvwxyz234567", true)]
    [InlineData("not base32 !", true)]
    [InlineData("AAAA", true)]
    [InlineData("", true)]
    public void Anything_but_a_76_byte_v1_key_is_sent_as_a_code(string text, bool isCode)
    {
        Assert.Equal(isCode, LicenceRequest.IsActivationCode(text));
        Assert.False(LicenceRequest.IsActivationCode(V1Key));
        Assert.False(LicenceRequest.IsActivationCode(V1Key.ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void Paths_name_the_clipmark_licence_endpoints()
    {
        Assert.Equal("/v1/licences/clipmark/activate", LicenceRequest.ActivatePath);
        Assert.Equal("/v1/licences/clipmark/entitlement", LicenceRequest.EntitlementPath);
        Assert.Equal("/v1/licences/clipmark/deactivate", LicenceRequest.DeactivatePath);
        Assert.Equal("/v1/licences/clipmark/trial", LicenceRequest.TrialPath);
    }
}
