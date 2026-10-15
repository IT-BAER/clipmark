using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class RelayFailureTests
{
    [Fact]
    public void Parses_the_error_body_fields()
    {
        var f = RelayFailure.Parse(429, "{\"error\":{\"code\":\"quota_rpm\",\"message\":\"Too many requests this minute.\",\"retry_after\":12}}");

        Assert.Equal(429, f.Status);
        Assert.Equal("quota_rpm", f.Code);
        Assert.Equal(12, f.RetryAfter);
        Assert.Null(f.ResetsAt);
        Assert.Equal("Relay limit reached. Try again in 12 s.", f.Message());
    }

    [Fact]
    public void A_malformed_or_missing_body_falls_back_to_the_status()
    {
        Assert.Equal("Request failed (HTTP 500).", RelayFailure.Parse(500, "<html>").Message());
        Assert.Equal("Request failed (HTTP 418).", RelayFailure.Parse(418, null).Message());
        Assert.Null(RelayFailure.Parse(500, "{\"error\":5}").Code);
    }

    [Fact]
    public void Budget_exhaustion_names_the_reset_date()
    {
        var f = RelayFailure.Parse(429, "{\"error\":{\"code\":\"quota_budget\",\"message\":\"x\",\"resets_at\":\"2026-11-01T00:00:00Z\"}}");

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), f.ResetsAt);
        Assert.Contains("Premium AI budget", f.Message());
        Assert.Contains("2026", f.Message());
    }

    [Theory]
    [InlineData(401, "unknown_install", true)]
    [InlineData(403, "install_evicted", true)]
    [InlineData(403, "install_revoked", false)]
    [InlineData(403, "subject_revoked", false)]
    [InlineData(401, "signature_invalid", false)]
    public void Only_a_missing_or_evicted_install_asks_for_a_new_registration(int status, string code, bool expected)
    {
        var f = RelayFailure.Parse(status, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"m\"}}}}");

        Assert.Equal(expected, f.NeedsRegistration);
    }

    [Fact]
    public void Clock_skew_carries_the_server_time()
    {
        var f = RelayFailure.Parse(401, "{\"error\":{\"code\":\"clock_skew\",\"message\":\"m\",\"server_time\":1760400000}}");

        Assert.True(f.IsClockSkew);
        Assert.Equal(1760400000, f.ServerTime);
    }

    [Theory]
    [InlineData("app_budget_exhausted", "relay has reached its monthly budget")]
    [InlineData("upstream_unavailable", "Anthropic is overloaded or unavailable")]
    [InlineData("storage_unavailable", "relay is temporarily unavailable")]
    [InlineData("subject_revoked", "license")]
    [InlineData("install_revoked", "This PC")]
    [InlineData("clock_skew", "clock")]
    [InlineData("input_invalid", "Request rejected: the field")]
    [InlineData("replay", "replay")]
    public void Each_code_has_a_fixed_user_message(string code, string expectedPart)
    {
        var f = RelayFailure.Parse(400, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"the field\"}}}}");

        Assert.Contains(expectedPart, f.Message());
    }

    [Theory]
    [InlineData("code_invalid", "activation code is not valid")]
    [InlineData("licence_invalid", "license key is not valid")]
    [InlineData("seats_exhausted", "Every PC")]
    [InlineData("subject_revoked", "revoked")]
    [InlineData("licence_expired", "expired")]
    [InlineData("trial_expired", "trial has ended")]
    [InlineData("trial_not_offered", "No trial")]
    [InlineData("install_evicted", "another PC")]
    [InlineData("install_deactivated", "removed from this PC")]
    [InlineData("signature_invalid", "key was not accepted")]
    [InlineData("envelope_invalid", "key was not accepted")]
    public void Licence_codes_have_their_own_sentences(string code, string expectedPart)
    {
        var f = RelayFailure.Parse(403, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"m\"}}}}");

        Assert.Contains(expectedPart, f.Message());
    }

    [Fact]
    public void Rate_limited_names_the_retry_time()
    {
        Assert.Contains("30 s", RelayFailure.Parse(429, "{\"error\":{\"code\":\"rate_limited\",\"message\":\"m\",\"retry_after\":30}}").Message());
        Assert.Contains("Too many attempts", RelayFailure.Parse(429, "{\"error\":{\"code\":\"rate_limited\",\"message\":\"m\"}}").Message());
    }

    [Theory]
    [InlineData("trial_expired", true)]
    [InlineData("subject_revoked", true)]
    [InlineData("licence_expired", true)]
    [InlineData("install_evicted", true)]
    [InlineData("install_revoked", true)]
    [InlineData("install_deactivated", true)]
    [InlineData("rate_limited", false)]
    [InlineData("clock_skew", false)]
    [InlineData("storage_unavailable", false)]
    public void Only_a_lost_licence_or_install_ends_the_entitlement(string code, bool expected)
    {
        Assert.Equal(expected, RelayFailure.Parse(403, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"m\"}}}}").EndsEntitlement);
    }

    [Theory]
    [InlineData(403, "install_deactivated", true)]
    [InlineData(403, "install_revoked", true)]
    [InlineData(403, "install_evicted", true)]
    [InlineData(403, "subject_revoked", true)]
    [InlineData(401, "unknown_install", true)]
    [InlineData(401, "signature_invalid", false)]
    [InlineData(401, "clock_skew", false)]
    [InlineData(429, "rate_limited", false)]
    [InlineData(503, "storage_unavailable", false)]
    [InlineData(502, null, false)]
    public void Only_an_install_already_gone_lets_deactivation_clear_the_local_state(int status, string? code, bool expected)
    {
        string? body = code is null ? null : $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"m\"}}}}";

        Assert.Equal(expected, RelayFailure.Parse(status, body).InstallGone);
    }
}
