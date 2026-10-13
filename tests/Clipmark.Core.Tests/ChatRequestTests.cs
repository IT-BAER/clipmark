using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ChatRequestTests
{
    private static JsonElement Root(byte[] json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void One_turn_request_carries_the_image_with_a_cache_marker_and_the_question()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        c.Ask("what?");

        var r = Root(ChatRequest.Build(c, "claude-sonnet-5-5"));

        Assert.Equal("claude-sonnet-5-5", r.GetProperty("model").GetString());
        Assert.Equal(16000, r.GetProperty("max_tokens").GetInt32());
        Assert.True(r.GetProperty("stream").GetBoolean());
        Assert.Equal(ChatRequest.SystemPrompt, r.GetProperty("system").GetString());
        Assert.Equal("medium", r.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal("adaptive", r.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("summarized", r.GetProperty("thinking").GetProperty("display").GetString());

        var msgs = r.GetProperty("messages");
        Assert.Equal(1, msgs.GetArrayLength());
        Assert.Equal("user", msgs[0].GetProperty("role").GetString());
        var content = msgs[0].GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        var source = content[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal("QUJD", source.GetProperty("data").GetString());
        Assert.Equal("ephemeral", content[0].GetProperty("cache_control").GetProperty("type").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal("what?", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void Three_turn_request_has_the_image_only_in_the_first_message_and_skips_the_pending_answer()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        c.Ask("q1"); c.AppendDelta("a1"); c.Complete("end_turn");
        c.Ask("q2 \"quoted\""); c.AppendDelta("a2"); c.Complete("end_turn");
        c.Ask("q3");
        c.AppendDelta("streaming");

        var msgs = Root(ChatRequest.Build(c, "claude-opus-5-5")).GetProperty("messages");

        Assert.Equal(5, msgs.GetArrayLength());
        Assert.Equal(["user", "assistant", "user", "assistant", "user"], msgs.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(JsonValueKind.Array, msgs[0].GetProperty("content").ValueKind);
        Assert.Equal(["a1", "q2 \"quoted\"", "a2", "q3"], Enumerable.Range(1, 4).Select(i => msgs[i].GetProperty("content").GetString()));
    }

    [Fact]
    public void Effort_and_max_tokens_are_passed_through()
    {
        var c = new ChatConversation();
        c.Start("QUJD", "image/png");
        c.Ask("q");
        var r = Root(ChatRequest.Build(c, "m", effort: "high", maxTokens: 1000));
        Assert.Equal("high", r.GetProperty("output_config").GetProperty("effort").GetString());
        Assert.Equal(1000, r.GetProperty("max_tokens").GetInt32());
    }
}
