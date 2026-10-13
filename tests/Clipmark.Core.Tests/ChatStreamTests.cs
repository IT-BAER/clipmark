using System.Text;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Tests;

public sealed class ChatStreamTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    private static string Delta(string text) =>
        $"event: content_block_delta\ndata: {{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"text_delta\",\"text\":\"{text}\"}}}}\n\n";

    private const string Stop = "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n"
        + "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";

    /// <summary>Returns the given byte chunks one read at a time, then blocks (or ends).</summary>
    private sealed class ChunkStream(IEnumerable<byte[]> chunks, bool hangAtEnd = false) : Stream
    {
        private readonly Queue<byte[]> _chunks = new(chunks);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_chunks.Count == 0)
            {
                if (hangAtEnd)
                    await Task.Delay(Timeout.Infinite, ct);
                return 0;
            }
            var next = _chunks.Dequeue();
            next.CopyTo(buffer);
            return next.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ChunkStream Body(params string[] chunks) => new(chunks.Select(Encoding.UTF8.GetBytes));

    [Fact]
    public async Task Text_arrives_in_order_and_the_stop_reason_is_returned()
    {
        var text = new List<string>();
        string reason = await ChatStream.ReadAsync(Body("event: ping\ndata: {\"type\":\"ping\"}\n\n" + Delta("Hel"), Delta("lo") + Stop), text.Add, Idle, default);

        Assert.Equal(["Hel", "lo"], text);
        Assert.Equal("end_turn", reason);
    }

    [Fact]
    public async Task A_character_split_across_reads_is_decoded_whole()
    {
        byte[] all = Encoding.UTF8.GetBytes(Delta("Grüße") + Stop);
        int cut = Array.IndexOf(all, (byte)0xC3) + 1; // inside the two bytes of "ü"
        var text = new List<string>();

        await ChatStream.ReadAsync(new ChunkStream([all[..cut], all[cut..]]), text.Add, Idle, default);

        Assert.Equal("Grüße", string.Concat(text));
    }

    [Fact]
    public async Task An_error_event_throws_its_user_message()
    {
        var e = await Assert.ThrowsAsync<AiException>(() => ChatStream.ReadAsync(
            Body(Delta("a") + "event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n\n"),
            _ => { }, Idle, default));

        Assert.Equal(ApiError.FromStreamError("overloaded_error", "Overloaded"), e.Message);
    }

    [Fact]
    public async Task A_body_that_ends_before_the_stop_reason_throws()
    {
        var e = await Assert.ThrowsAsync<AiException>(() => ChatStream.ReadAsync(Body(Delta("a")), _ => { }, Idle, default));

        Assert.Contains("closed", e.Message);
    }

    [Fact]
    public async Task No_data_within_the_idle_timeout_throws()
    {
        var body = new ChunkStream([Encoding.UTF8.GetBytes(Delta("a"))], hangAtEnd: true);

        var e = await Assert.ThrowsAsync<AiException>(() => ChatStream.ReadAsync(body, _ => { }, TimeSpan.FromMilliseconds(100), default));

        Assert.Contains("No data", e.Message);
    }

    [Fact]
    public async Task Cancelling_the_caller_token_is_not_an_error_message()
    {
        using var cts = new CancellationTokenSource();
        var body = new ChunkStream([Encoding.UTF8.GetBytes(Delta("a"))], hangAtEnd: true);

        var read = ChatStream.ReadAsync(body, _ => cts.Cancel(), Idle, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }
}
