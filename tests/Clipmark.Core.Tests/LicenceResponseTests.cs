using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class LicenceResponseTests
{
    [Fact]
    public void Activation_parses_every_field()
    {
        var r = LicenceResponse.ParseActivate("""
            {"entitlement":"rl1.a.b","install_id":"inst","licence":{"id":"42","plan":"premium","seats":3,"expires_at":"2027-10-14T00:00:00Z"},"evicted_install_id":"old"}
            """);

        Assert.Equal(new ActivateResult("rl1.a.b", "inst", "42", "premium", 3, new DateTimeOffset(2027, 10, 14, 0, 0, 0, TimeSpan.Zero), "old"), r);
    }

    [Fact]
    public void Activation_without_expiry_or_eviction_has_nulls()
    {
        var r = LicenceResponse.ParseActivate("""{"entitlement":"rl1.a.b","install_id":"inst","licence":{"id":"42","plan":"premium","seats":3,"expires_at":null},"evicted_install_id":null}""");

        Assert.NotNull(r);
        Assert.Null(r.ExpiresAt);
        Assert.Null(r.EvictedInstallId);
    }

    [Theory]
    [InlineData("""{"install_id":"inst","licence":{"id":"42","plan":"premium","seats":3}}""")]
    [InlineData("""{"entitlement":"","install_id":"inst"}""")]
    [InlineData("""{"entitlement":5}""")]
    [InlineData("[]")]
    [InlineData("<html>")]
    [InlineData("")]
    public void A_body_without_an_entitlement_is_no_result(string json)
    {
        Assert.Null(LicenceResponse.ParseActivate(json));
        Assert.Null(LicenceResponse.ParseTrial(json));
        Assert.Null(LicenceResponse.ParseEntitlement(json));
    }

    [Fact]
    public void Trial_parses_the_end()
    {
        Assert.Equal(new TrialResult("rl1.t.s", new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero)),
            LicenceResponse.ParseTrial("""{"entitlement":"rl1.t.s","ends_at":"2026-11-13T09:00:00Z"}"""));
    }

    [Fact]
    public void Entitlement_refresh_returns_the_token()
    {
        Assert.Equal("rl1.x.y", LicenceResponse.ParseEntitlement("""{"entitlement":"rl1.x.y"}"""));
    }
}
