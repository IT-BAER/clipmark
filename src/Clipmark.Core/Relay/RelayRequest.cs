using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Relay;

/// <summary>Body of the Ask AI task call (releash spec 5.4; task file clipmark/ask).</summary>
public static class RelayRequest
{
    public const string App = "clipmark", Task = "ask";
    public const string AskPath = "/v1/tasks/" + App + "/" + Task;

    /// <summary>Turn limits of the clipmark/ask task file (max_items, max_chars_per_item); the relay refuses more with input_invalid.</summary>
    public const int MaxTurns = 20, MaxTurnChars = 8000;

    /// <summary>The typed task input: image, alternating turns ending with the user, model and effort from the server allowlist. A pending answer is left out.</summary>
    public static byte[] BuildAsk(ChatConversation c, string model, string effort)
    {
        var turns = c.Turns;
        int count = c.IsBusy && turns.Count > 0 && turns[^1].Role == ChatRole.Assistant ? turns.Count - 1 : turns.Count;

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteStartObject("input");
            w.WriteStartObject("image");
            w.WriteString("media_type", c.MediaType);
            w.WriteString("data", c.ImageBase64);
            w.WriteEndObject();
            w.WriteStartArray("turns");
            // The turns alternate and end with the user, so an odd window starts with a user turn.
            for (int i = Math.Max(0, count - (MaxTurns - 1)); i < count; i++)
            {
                w.WriteStartObject();
                w.WriteString("role", turns[i].Role == ChatRole.User ? "user" : "assistant");
                w.WriteString("text", Cut(turns[i].Text));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("model", model);
            w.WriteString("effort", effort);
            w.WriteEndObject();
            w.WriteBoolean("stream", true);
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>The first MaxTurnChars UTF-16 units, one less when that would split a surrogate pair; never more code points than the relay counts.</summary>
    private static string Cut(string text)
    {
        if (text.Length <= MaxTurnChars)
            return text;
        return text[..(char.IsHighSurrogate(text[MaxTurnChars - 1]) ? MaxTurnChars - 1 : MaxTurnChars)];
    }
}
