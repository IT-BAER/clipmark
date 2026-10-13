using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class SseParserTests
{
    [Fact]
    public void Event_and_data_dispatch_on_the_blank_line()
    {
        var events = new SseParser().Feed("event: ping\ndata: {\"a\":1}\n\n").ToList();
        Assert.Equal([new SseEvent("ping", "{\"a\":1}")], events);
    }

    [Fact]
    public void Lines_split_across_chunks_and_inside_CRLF_are_reassembled()
    {
        var p = new SseParser();
        var events = new List<SseEvent>();
        foreach (var chunk in new[] { "event: mess", "age_start\r", "\ndata: hel", "lo\r\n\r", "\n" })
            events.AddRange(p.Feed(chunk));

        Assert.Equal([new SseEvent("message_start", "hello")], events);
    }

    [Fact]
    public void Multiple_data_lines_join_with_a_newline_and_one_leading_space_is_stripped()
    {
        var events = new SseParser().Feed("data:  a\ndata:b\n\n").ToList();
        Assert.Equal([new SseEvent(null, " a\nb")], events);
    }

    [Fact]
    public void Comments_and_events_without_data_are_ignored()
    {
        var events = new SseParser().Feed(": keepalive\n\nevent: x\n\n:c\ndata: d\n\n").ToList();
        Assert.Equal([new SseEvent(null, "d")], events);
    }

    [Fact]
    public void Several_events_in_one_chunk_come_out_in_order_and_an_unterminated_one_waits()
    {
        var events = new SseParser().Feed("data: 1\n\ndata: 2\n\ndata: 3").ToList();
        Assert.Equal(["1", "2"], events.Select(e => e.Data));
    }

    [Fact]
    public void Event_name_does_not_leak_into_the_next_event()
    {
        var events = new SseParser().Feed("event: a\ndata: 1\n\ndata: 2\n\n").ToList();
        Assert.Equal([new SseEvent("a", "1"), new SseEvent(null, "2")], events);
    }
}
