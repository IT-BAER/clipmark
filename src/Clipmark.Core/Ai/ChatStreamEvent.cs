using System.Text.Json;

namespace Clipmark.Core.Ai;

public enum ChatStreamKind { TextDelta, ThinkingDelta, Stop, Error, Ignore }

public sealed record ChatStreamEvent(ChatStreamKind Kind, string? Text = null, string? StopReason = null, string? ErrorType = null, string? ErrorMessage = null)
{
    private static readonly ChatStreamEvent IgnoreEvent = new(ChatStreamKind.Ignore);

    /// <summary>Maps one Messages API stream payload; only text and thinking deltas, the stop reason and errors matter.</summary>
    public static ChatStreamEvent From(string dataJson)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(dataJson); }
        catch (JsonException) { return new ChatStreamEvent(ChatStreamKind.Error, ErrorType: "invalid_json"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "type") is not { } type)
                return IgnoreEvent;
            switch (type)
            {
                case "content_block_delta" when root.TryGetProperty("delta", out var d) && Str(d, "type") == "text_delta" && Str(d, "text") is { } text:
                    return new ChatStreamEvent(ChatStreamKind.TextDelta, Text: text);
                case "content_block_delta" when root.TryGetProperty("delta", out var d) && Str(d, "type") == "thinking_delta" && Str(d, "thinking") is { } thinking:
                    return new ChatStreamEvent(ChatStreamKind.ThinkingDelta, Text: thinking);
                case "message_delta" when root.TryGetProperty("delta", out var d) && Str(d, "stop_reason") is { } reason:
                    return new ChatStreamEvent(ChatStreamKind.Stop, StopReason: reason);
                case "error":
                    var e = root.TryGetProperty("error", out var err) ? err : default;
                    return new ChatStreamEvent(ChatStreamKind.Error, ErrorType: Str(e, "type"), ErrorMessage: Str(e, "message"));
                default:
                    return IgnoreEvent;
            }
        }
    }

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
