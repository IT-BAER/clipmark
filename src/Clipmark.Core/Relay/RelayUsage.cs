using System.Globalization;
using System.Text.Json;

namespace Clipmark.Core.Relay;

/// <summary>The trailing <c>releash.usage</c> event of a streamed answer.</summary>
public sealed record RelayUsage(double? CostUsd, double? RemainingMonthlyUsd)
{
    public static RelayUsage? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                ? new RelayUsage(RelayJson.Num(root, "cost_usd"), RelayJson.Num(root, "remaining_monthly_usd"))
                : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Lenient readers for relay response fields: a missing or mistyped field reads as 0 or null.</summary>
internal static class RelayJson
{
    internal static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

    /// <summary>A number, or a numeric string (the relay sends USD amounts as strings in usage events).</summary>
    internal static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out double d) => d,
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) => d,
            _ => null,
        };
    }
}
