using System.Globalization;
using System.Text.Json;
using Clipmark.Core.Ai;

namespace Clipmark.Core.Relay;

public sealed record RelayLimits(int Rpm, int Rpd, double MonthlyUsd, int MaxInstalls);

/// <summary>What the relay answered at registration, kept in relay.json next to the settings; <see cref="RemainingMonthlyUsd"/> follows the last usage event.</summary>
public sealed record RelayRegistration(string InstallId, string KeyStore, string AppVersion, DateTime RegisteredUtc, RelayLimits Limits, double? RemainingMonthlyUsd = null)
{
    /// <summary>Null when the body has no install_id (never trust a 2xx without one).</summary>
    public static RelayRegistration? FromResponse(string json, string keyStore, string appVersion, DateTime nowUtc)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ChatStreamEvent.Str(root, "install_id") is not { Length: > 0 } installId)
                return null;
            var limits = root.TryGetProperty("limits", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
            return new RelayRegistration(installId, keyStore, appVersion, nowUtc,
                new RelayLimits(Int(limits, "rpm"), Int(limits, "rpd"), Num(limits, "monthly_usd") ?? 0, Int(limits, "max_installs")));
        }
    }

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
                ? new RelayUsage(RelayRegistration.Num(root, "cost_usd"), RelayRegistration.Num(root, "remaining_monthly_usd"))
                : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>relay.json without reflection (Native AOT). Anything incomplete reads as not registered.</summary>
public static class RelayStateStore
{
    public static string Serialize(RelayRegistration s)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("installId", s.InstallId);
            w.WriteString("keyStore", s.KeyStore);
            w.WriteString("appVersion", s.AppVersion);
            w.WriteString("registered", s.RegisteredUtc.ToString("O", CultureInfo.InvariantCulture));
            w.WriteNumber("rpm", s.Limits.Rpm);
            w.WriteNumber("rpd", s.Limits.Rpd);
            w.WriteNumber("monthlyUsd", s.Limits.MonthlyUsd);
            w.WriteNumber("maxInstalls", s.Limits.MaxInstalls);
            if (s.RemainingMonthlyUsd is { } remaining)
                w.WriteNumber("remainingMonthlyUsd", remaining);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static RelayRegistration? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ChatStreamEvent.Str(root, "installId") is not { Length: > 0 } installId
                || ChatStreamEvent.Str(root, "keyStore") is not { Length: > 0 } keyStore
                || ChatStreamEvent.Str(root, "appVersion") is not { } appVersion
                || !DateTimeOffset.TryParse(ChatStreamEvent.Str(root, "registered"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var registered))
                return null;
            return new RelayRegistration(installId, keyStore, appVersion, registered.UtcDateTime,
                new RelayLimits(RelayRegistration.Int(root, "rpm"), RelayRegistration.Int(root, "rpd"), RelayRegistration.Num(root, "monthlyUsd") ?? 0, RelayRegistration.Int(root, "maxInstalls")),
                RelayRegistration.Num(root, "remainingMonthlyUsd"));
        }
    }

    public static RelayRegistration? Load(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Temp file first, so a crash never leaves a half-written state file.</summary>
    public static void Save(string path, RelayRegistration s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(s));
        File.Move(temp, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
