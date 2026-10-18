using System.Text.Json;
using System.Text.RegularExpressions;
using Clipmark.Core.Settings;

namespace Clipmark.Core.Update;

/// <summary>
/// The newest GitHub release as the update check reads it. Only the tag is trusted: the page link is built from it here,
/// so a response can never send the user to another site.
/// </summary>
public sealed partial record UpdateRelease(Version Version, string Tag)
{
    public string VersionText => Version.ToString(3);

    public string PageUrl => "https://github.com/IT-BAER/clipmark/releases/tag/" + Tag;

    [GeneratedRegex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^\d{1,5}\.\d{1,5}\.\d{1,5}$")]
    private static partial Regex VersionPattern();

    /// <summary>True for a version text as <see cref="VersionText"/> writes it.</summary>
    public static bool IsVersionText(string? text) => text is not null && VersionPattern().IsMatch(text);

    /// <summary>The release in a <c>releases/latest</c> body; null for a draft, a prerelease, a tag that is not x.y.z, or anything unreadable.</summary>
    public static UpdateRelease? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Flag(root, "draft") || Flag(root, "prerelease")
                || !root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
                || tagElement.GetString() is not { } tag || TagPattern().Match(tag) is not { Success: true } m)
                return null;
            return new UpdateRelease(new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), tag);
        }
    }

    /// <summary>False when <paramref name="current"/> is not a version, so a development build is never nagged.</summary>
    public bool IsNewerThan(string current) => Version.TryParse(current, out var own) && Version > own;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>When the update check runs and when its popup shows. Never turns both off; Later is quiet for three days for that version.</summary>
public static class UpdatePolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    public static readonly TimeSpan PostponeFor = TimeSpan.FromDays(3);

    public static bool ShouldCheck(ClipmarkSettings s, DateTime nowUtc) =>
        s.UpdateCheck && (s.UpdateLastCheckUtc is not { } last || last > nowUtc || nowUtc - last >= CheckInterval);

    public static bool ShouldPrompt(ClipmarkSettings s, UpdateRelease release, string current, DateTime nowUtc)
    {
        if (!s.UpdateCheck || !release.IsNewerThan(current))
            return false;
        // A reminder time beyond the postpone span is a clock set back; it must not silence the popup.
        bool quiet = s.UpdateLaterVersion == release.VersionText && s.UpdateRemindAfterUtc is { } at && at > nowUtc && at <= nowUtc + PostponeFor;
        return !quiet;
    }

    public static ClipmarkSettings Postpone(ClipmarkSettings s, UpdateRelease release, DateTime nowUtc) =>
        s with { UpdateLaterVersion = release.VersionText, UpdateRemindAfterUtc = nowUtc + PostponeFor };
}
