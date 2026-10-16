using System.Text.Json;
using Clipmark.Core.Ai;
using Clipmark.Core.Relay;

namespace Clipmark.Core.Tests;

public sealed class RelayRequestTests
{
    private static JsonElement Root(byte[] json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Ask_body_carries_the_typed_input_and_streams()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        c.Ask("what?");

        var r = Root(RelayRequest.BuildAsk(c, "claude-sonnet-5-5", "high"));

        Assert.True(r.GetProperty("stream").GetBoolean());
        var input = r.GetProperty("input");
        Assert.Equal(4, input.EnumerateObject().Count());
        Assert.Equal("image/png", input.GetProperty("image").GetProperty("media_type").GetString());
        Assert.Equal("QUJD", input.GetProperty("image").GetProperty("data").GetString());
        Assert.Equal("claude-sonnet-5-5", input.GetProperty("model").GetString());
        Assert.Equal("high", input.GetProperty("effort").GetString());
        var turns = input.GetProperty("turns");
        Assert.Equal(1, turns.GetArrayLength());
        Assert.Equal("user", turns[0].GetProperty("role").GetString());
        Assert.Equal("what?", turns[0].GetProperty("text").GetString());
    }

    [Fact]
    public void Ask_body_skips_the_pending_answer_so_the_turns_alternate_and_end_with_the_user()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/jpeg");
        c.Ask("q1"); c.AppendDelta("a1"); c.Complete("end_turn");
        c.Ask("q2");
        c.AppendDelta("streaming");

        var turns = Root(RelayRequest.BuildAsk(c, "claude-opus-5-5", "medium")).GetProperty("input").GetProperty("turns");

        Assert.Equal(["user", "assistant", "user"], turns.EnumerateArray().Select(t => t.GetProperty("role").GetString()));
        Assert.Equal(["q1", "a1", "q2"], turns.EnumerateArray().Select(t => t.GetProperty("text").GetString()));
    }

    [Fact]
    public void Path_names_the_clipmark_app_and_the_ask_task()
    {
        Assert.Equal("/v1/tasks/clipmark/ask", RelayRequest.AskPath);
    }
}
