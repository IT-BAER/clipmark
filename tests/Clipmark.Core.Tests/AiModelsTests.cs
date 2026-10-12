using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class AiModelsTests
{
    [Fact]
    public void Models_list_in_order_with_Sonnet_as_default()
    {
        Assert.Equal(["claude-sonnet-5-5", "claude-opus-5-5", "claude-haiku-5-5"], AiModels.All.Select(m => m.Id));
        Assert.Equal(["Claude Sonnet 5.5", "Claude Opus 5.5", "Claude Haiku 5.5"], AiModels.All.Select(m => m.Name));
        Assert.Equal(AiModels.All[0], AiModels.Default);
    }

    [Fact]
    public void Find_returns_the_match_or_the_default()
    {
        Assert.Equal("claude-haiku-5-5", AiModels.Find("claude-haiku-5-5").Id);
        Assert.Equal(AiModels.Default, AiModels.Find("gpt-9"));
        Assert.Equal(AiModels.Default, AiModels.Find(null));
    }
}
