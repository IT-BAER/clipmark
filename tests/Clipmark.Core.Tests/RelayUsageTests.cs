using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class RelayUsageTests
{
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
