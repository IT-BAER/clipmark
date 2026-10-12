using System.Security.Cryptography;
using Clipmark.Core.Licensing;

namespace Clipmark.Core.Tests;

public class LicenseKeyTests
{
    private static readonly ECDsa Issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly LicensePayload Sale = new(0x0123456789ABCDEF, new DateOnly(2026, 10, 12));

    [Fact]
    public void Signed_key_verifies_and_returns_its_payload()
    {
        string key = LicenseKey.Sign(Sale, Issuer);

        Assert.True(LicenseKey.TryVerify(key, Issuer, out var payload));
        Assert.Equal(Sale, payload);
    }

    [Fact]
    public void Key_is_dash_grouped_base32()
    {
        string key = LicenseKey.Sign(Sale, Issuer);

        Assert.Matches("^[A-Z2-7]{5}(-[A-Z2-7]{1,5})+$", key);
    }

    [Fact]
    public void Pasted_key_with_lower_case_and_spaces_verifies()
    {
        string key = LicenseKey.Sign(Sale, Issuer);

        Assert.True(LicenseKey.TryVerify("  " + key.ToLowerInvariant().Replace("-", " - ") + "\r\n", Issuer, out _));
    }

    [Fact]
    public void Changed_character_is_rejected()
    {
        string key = LicenseKey.Sign(Sale, Issuer);
        char flipped = key[7] == 'A' ? 'B' : 'A';

        Assert.False(LicenseKey.TryVerify(key[..7] + flipped + key[8..], Issuer, out var payload));
        Assert.Null(payload);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("7")]
    public void Key_with_an_extra_character_is_rejected(string extra)
    {
        Assert.False(LicenseKey.TryVerify(LicenseKey.Sign(Sale, Issuer) + extra, Issuer, out _));
    }

    [Fact]
    public void Key_with_a_changed_pad_bit_is_rejected()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string key = LicenseKey.Sign(Sale, Issuer);
        char last = alphabet[alphabet.IndexOf(key[^1]) ^ 1];

        Assert.False(LicenseKey.TryVerify(key[..^1] + last, Issuer, out _));
    }

    [Fact]
    public void Key_from_another_issuer_is_rejected()
    {
        using var forger = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(LicenseKey.TryVerify(LicenseKey.Sign(Sale, forger), Issuer, out _));
    }

    [Fact]
    public void Key_for_another_product_is_rejected()
    {
        string key = LicenseKey.Sign(Sale with { Product = 2 }, Issuer);

        Assert.False(LicenseKey.TryVerify(key, Issuer, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("AAAAA-BBBBB-CCCCC")]
    public void Garbage_is_rejected(string? text)
    {
        Assert.False(LicenseKey.TryVerify(text, Issuer, out _));
    }

    [Fact]
    public void Issue_date_before_2026_cannot_be_signed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LicenseKey.Sign(Sale with { Issued = new DateOnly(2025, 12, 31) }, Issuer));
    }
}
