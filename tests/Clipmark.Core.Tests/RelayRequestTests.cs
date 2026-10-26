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
    public void Ask_body_keeps_the_newest_turns_within_the_task_limit_starting_with_a_user_turn()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        for (int i = 1; i <= 12; i++)
        {
            c.Ask($"q{i}"); c.AppendDelta($"a{i}"); c.Complete("end_turn");
        }
        c.Ask("q13");

        var turns = Root(RelayRequest.BuildAsk(c, "claude-opus-5-5", "medium")).GetProperty("input").GetProperty("turns");

        Assert.Equal(19, turns.GetArrayLength());
        Assert.Equal("q4", turns[0].GetProperty("text").GetString());
        Assert.Equal("q13", turns[18].GetProperty("text").GetString());
        Assert.All(turns.EnumerateArray().Select((t, i) => (t, i)),
            x => Assert.Equal(x.i % 2 == 0 ? "user" : "assistant", x.t.GetProperty("role").GetString()));
    }

    [Fact]
    public void Ask_body_cuts_each_turn_to_8000_characters_without_splitting_a_surrogate_pair()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        c.Ask(new string('q', 9000)); c.AppendDelta(new string('a', 7999) + "\U0001F600b"); c.Complete("end_turn");
        c.Ask(new string('r', 8000));

        var turns = Root(RelayRequest.BuildAsk(c, "claude-opus-5-5", "medium")).GetProperty("input").GetProperty("turns");

        Assert.Equal(new string('q', 8000), turns[0].GetProperty("text").GetString());
        Assert.Equal(new string('a', 7999), turns[1].GetProperty("text").GetString());
        Assert.Equal(new string('r', 8000), turns[2].GetProperty("text").GetString());
    }

    [Fact]
    public void Path_names_the_clipmark_app_and_the_ask_task()
    {
        Assert.Equal("/v1/tasks/clipmark/ask", RelayRequest.AskPath);
    }
}
