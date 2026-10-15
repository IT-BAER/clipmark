using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clipmark.Core.Licensing;
using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

/// <summary>The releash entitlement vector (releash repo testdata/entitlement/v1.json, test key only), driven case by case.</summary>
public sealed class EntitlementTokenTests
{
    private static readonly JsonElement Vector = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "testdata", "entitlement", "v1.json"))).RootElement;

    public static TheoryData<string> CaseNames()
    {
        var names = new TheoryData<string>();
        foreach (var c in Vector.GetProperty("cases").EnumerateArray())
            names.Add(c.GetProperty("name").GetString()!);
        return names;
    }

    private static JsonElement Case(string name) =>
        Vector.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private static Dictionary<string, ECDsa> Keys(JsonElement keys) =>
        EntitlementToken.LoadKeys(keys.EnumerateObject().ToDictionary(k => k.Name, k => k.Value.GetString()!));

    [Fact]
    public void The_vector_has_twelve_cases()
    {
        Assert.Equal(12, CaseNames().Count);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Each_vector_case_gets_its_verdict(string name)
    {
        var c = Case(name);

        var verdict = EntitlementToken.Verify(c.GetProperty("entitlement").GetString(), Keys(c.GetProperty("keys")),
            c.GetProperty("app").GetString()!, c.GetProperty("dev").GetString()!, c.GetProperty("now").GetInt64(), out var claims);

        Assert.Equal(c.GetProperty("verdict").GetString(), EntitlementToken.Name(verdict));
        Assert.Equal(verdict == TokenVerdict.Valid, claims is not null);
    }

    [Fact]
    public void A_valid_token_yields_every_claim()
    {
        var c = Case("valid");

        EntitlementToken.Verify(c.GetProperty("entitlement").GetString(), Keys(c.GetProperty("keys")), "clipmark",
            c.GetProperty("dev").GetString()!, c.GetProperty("now").GetInt64(), out var claims);

        Assert.NotNull(claims);
        Assert.Equal("dGVzdGtpZDE", claims.Kid);
        Assert.Equal("clipmark", claims.App);
        Assert.Equal("AAECAwQFBgcICQoL", claims.Lic);
        Assert.Equal("premium", claims.Plan);
        Assert.Equal(["ai", "history"], claims.Feat);
        Assert.Equal(3, claims.Seats);
        Assert.Equal("wDEqxDa9R1vf1h96oQnyEQyAhNqmR3A3mr0ui4vCICM", claims.Dev);
        Assert.Equal(1760000000, claims.Iat);
        Assert.Equal(1762592000, claims.Exp);
        Assert.Equal(1209600, claims.Grace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rl1.only-two")]
    [InlineData("rl1.a.b.c")]
    [InlineData("rl1.@@@.AAAA")]
    public void Broken_text_is_malformed(string? text)
    {
        var keys = Keys(Case("valid").GetProperty("keys"));

        Assert.Equal(TokenVerdict.Malformed, EntitlementToken.Verify(text, keys, "clipmark", "dev", 1760001000, out var claims));
        Assert.Null(claims);
    }

    [Fact]
    public void A_signed_payload_without_a_required_field_is_malformed()
    {
        using var key = VectorKey();
        string token = Sign("""{"v":1,"kid":"dGVzdGtpZDE","app":"clipmark","plan":"premium","dev":"d","iat":1,"exp":2,"grace":0}""", key);

        Assert.Equal(TokenVerdict.Malformed, EntitlementToken.Verify(token, KeysFor(key), "clipmark", "d", 1, out _));
    }

    [Fact]
    public void A_trial_token_with_null_features_and_an_empty_licence_verifies()
    {
        using var key = VectorKey();
        string token = Sign("""{"v":1,"kid":"dGVzdGtpZDE","app":"clipmark","lic":"","plan":"trial","feat":null,"seats":1,"dev":"d","iat":100,"exp":200,"grace":0}""", key);

        Assert.Equal(TokenVerdict.Valid, EntitlementToken.Verify(token, KeysFor(key), "clipmark", "d", 150, out var claims));
        Assert.Equal("trial", claims!.Plan);
        Assert.Empty(claims.Feat);
        Assert.Equal(TokenVerdict.Expired, EntitlementToken.Verify(token, KeysFor(key), "clipmark", "d", 201, out _));
    }

    [Fact]
    public void Read_claims_parses_without_checking_the_signature()
    {
        var claims = EntitlementToken.ReadClaims(Case("tampered payload").GetProperty("entitlement").GetString());

        Assert.NotNull(claims);
        Assert.Null(EntitlementToken.ReadClaims("rl2.x.y"));
    }

    internal static ECDsa VectorKey()
    {
        var key = ECDsa.Create();
        key.ImportFromPem(Vector.GetProperty("private_key_pem").GetString());
        return key;
    }

    internal static Dictionary<string, ECDsa> KeysFor(ECDsa key) => new() { ["dGVzdGtpZDE"] = key };

    internal static string Sign(string payload, ECDsa key)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        return "rl1." + RelaySigning.Base64Url(bytes) + "." +
            RelaySigning.Base64Url(key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
