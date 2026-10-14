using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class RelayStateTests
{
    private const string Response = "{\"install_id\":\"wDEq\",\"subject\":{\"type\":\"licence\",\"id\":\"1234\"},"
        + "\"limits\":{\"rpm\":20,\"rpd\":300,\"monthly_usd\":5.0,\"max_installs\":3},\"evicted_install_id\":null}";

    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Registration_response_becomes_the_stored_state()
    {
        var s = RelayRegistration.FromResponse(Response, "tpm", "1.1.0", Now);

        Assert.NotNull(s);
        Assert.Equal("wDEq", s.InstallId);
        Assert.Equal("tpm", s.KeyStore);
        Assert.Equal("1.1.0", s.AppVersion);
        Assert.Equal(Now, s.RegisteredUtc);
        Assert.Equal(20, s.Limits.Rpm);
        Assert.Equal(300, s.Limits.Rpd);
        Assert.Equal(5.0, s.Limits.MonthlyUsd);
        Assert.Equal(3, s.Limits.MaxInstalls);
        Assert.Null(s.RemainingMonthlyUsd);
    }

    [Fact]
    public void A_response_without_an_install_id_is_rejected()
    {
        Assert.Null(RelayRegistration.FromResponse("{\"limits\":{}}", "tpm", "1.1.0", Now));
        Assert.Null(RelayRegistration.FromResponse("nope", "tpm", "1.1.0", Now));
    }

    [Fact]
    public void State_round_trips_through_json_and_a_broken_file_reads_as_none()
    {
        var s = RelayRegistration.FromResponse(Response, "software", "1.1.0", Now)! with { RemainingMonthlyUsd = 4.25 };

        var back = RelayStateStore.Parse(RelayStateStore.Serialize(s));

        Assert.Equal(s, back);
        Assert.Null(RelayStateStore.Parse("{}"));
        Assert.Null(RelayStateStore.Parse("[1"));
    }

    [Fact]
    public void Usage_event_yields_the_remaining_budget()
    {
        var u = RelayUsage.Parse("{\"input_tokens\":1200,\"output_tokens\":80,\"cost_usd\":\"0.0123\",\"remaining_monthly_usd\":\"4.9877\"}");

        Assert.NotNull(u);
        Assert.Equal(0.0123, u.CostUsd);
        Assert.Equal(4.9877, u.RemainingMonthlyUsd);
        Assert.Null(RelayUsage.Parse("{\"cost_usd\":\"x\"}")!.CostUsd);
        Assert.Null(RelayUsage.Parse("garbage"));
    }
}
