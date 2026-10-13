using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ChatStreamEventTests
{
    [Fact]
    public void Real_stream_maps_to_one_text_delta_and_one_stop()
    {
        string[] wire =
        [
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[],"model":"claude-sonnet-5-5","stop_reason":null,"usage":{"input_tokens":58,"cache_creation_input_tokens":4817,"cache_read_input_tokens":0,"output_tokens":6}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"**Nummer:** 4711"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":537}}""",
            """{"type":"message_stop"}""",
        ];

        Assert.Equal(
        [
            ChatStreamKind.Ignore, ChatStreamKind.Ignore, ChatStreamKind.TextDelta,
            ChatStreamKind.Ignore, ChatStreamKind.Stop, ChatStreamKind.Ignore,
        ], wire.Select(w => ChatStreamEvent.From(w).Kind));
        Assert.Equal("**Nummer:** 4711", ChatStreamEvent.From(wire[2]).Text);
        Assert.Equal("end_turn", ChatStreamEvent.From(wire[4]).StopReason);
    }

    [Theory]
    [InlineData("""{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"x"}}""")]
    [InlineData("""{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{"}}""")]
    [InlineData("""{"type":"message_delta","delta":{"stop_reason":null},"usage":{"output_tokens":1}}""")]
    [InlineData("""{"type":"ping"}""")]
    [InlineData("""{"type":"something_new"}""")]
    [InlineData("""[1,2]""")]
    public void Other_events_are_ignored(string json)
    {
        Assert.Equal(ChatStreamKind.Ignore, ChatStreamEvent.From(json).Kind);
    }

    [Fact]
    public void Thinking_delta_maps_to_ThinkingDelta()
    {
        var e = ChatStreamEvent.From("""{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"hmm"}}""");
        Assert.Equal(new ChatStreamEvent(ChatStreamKind.ThinkingDelta, Text: "hmm"), e);
    }

    [Fact]
    public void Error_event_carries_type_and_message()
    {
        var e = ChatStreamEvent.From("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""");
        Assert.Equal(new ChatStreamEvent(ChatStreamKind.Error, ErrorType: "overloaded_error", ErrorMessage: "Overloaded"), e);
    }

    [Fact]
    public void Invalid_json_is_an_error()
    {
        Assert.Equal(new ChatStreamEvent(ChatStreamKind.Error, ErrorType: "invalid_json"), ChatStreamEvent.From("{nope"));
    }
}
