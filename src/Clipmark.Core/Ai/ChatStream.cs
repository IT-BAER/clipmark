using System.Text;

namespace Clipmark.Core.Ai;

/// <summary>A failed request or stream; the message is shown to the user as is.</summary>
public sealed class AiException(string message) : Exception(message);

public static class ChatStream
{
    /// <summary>
    /// Reads a streaming Messages API body: text deltas go to <paramref name="onText"/> in order, the stop reason is returned.
    /// Throws <see cref="AiException"/> for an error event, a body that ends without a stop reason, or no data for <paramref name="idle"/>.
    /// </summary>
    public static async Task<string> ReadAsync(Stream body, Action<string> onText, TimeSpan idle, CancellationToken ct)
    {
        var parser = new SseParser();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        string? stopReason = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            timeout.CancelAfter(idle);
            int read;
            try
            {
                read = await body.ReadAsync(bytes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AiException($"No data from Anthropic for {idle.TotalSeconds:0} s. Try again.");
            }
            if (read == 0)
                return stopReason ?? throw new AiException("The connection closed before the answer was complete. Try again.");
            int count = decoder.GetChars(bytes, 0, read, chars, 0);
            foreach (var sse in parser.Feed(new string(chars, 0, count)))
            {
                var e = ChatStreamEvent.From(sse.Data);
                switch (e.Kind)
                {
                    case ChatStreamKind.TextDelta:
                        onText(e.Text!);
                        break;
                    case ChatStreamKind.Stop:
                        stopReason = e.StopReason;
                        break;
                    case ChatStreamKind.Error:
                        throw new AiException(ApiError.FromStreamError(e.ErrorType, e.ErrorMessage));
                }
            }
        }
    }
}
