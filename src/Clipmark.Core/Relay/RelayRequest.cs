using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Relay;

/// <summary>Bodies for the two relay calls Clipmark makes (releash spec 5.2 and 5.4; task file clipmark/ask).</summary>
public static class RelayRequest
{
    public const string App = "clipmark", Task = "ask";
    public const string RegisterPath = "/v1/apps/" + App + "/installs";
    public const string AskPath = "/v1/tasks/" + App + "/" + Task;

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
            for (int i = 0; i < count; i++)
            {
                w.WriteStartObject();
                w.WriteString("role", turns[i].Role == ChatRole.User ? "user" : "assistant");
                w.WriteString("text", turns[i].Text);
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

    /// <summary>Windows registration: the licence is the anchor, the JWK is the install key; no device label in v1.</summary>
    public static byte[] BuildRegistration(string licenceKey, string x, string y, string keyStore, string appVersion)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("platform", "windows");
            w.WriteStartObject("anchor");
            w.WriteString("type", "licence");
            w.WriteString("licence_key", licenceKey);
            w.WriteEndObject();
            w.WriteStartObject("public_key");
            w.WriteString("kty", "EC");
            w.WriteString("crv", "P-256");
            w.WriteString("x", x);
            w.WriteString("y", y);
            w.WriteEndObject();
            w.WriteString("key_store", keyStore);
            w.WriteString("app_version", appVersion);
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
