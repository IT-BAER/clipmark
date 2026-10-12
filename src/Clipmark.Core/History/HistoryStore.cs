using System.Globalization;
using System.Text.RegularExpressions;

namespace Clipmark.Core.History;

/// <summary>One stored capture: its id (UTC "yyyyMMdd-HHmmssfff", sorts by time) and its file size.</summary>
public sealed record HistoryEntry(string Id, long Size)
{
    public DateTime TakenUtc => DateTime.ParseExact(Id.AsSpan(0, 18), "yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

public static class HistoryPolicy
{
    public const int MaxItems = 25;
    public const long MaxBytes = 250L * 1024 * 1024;

    /// <summary>The entries to delete, oldest first; entries are newest first. The newest entry always stays.</summary>
    public static IReadOnlyList<HistoryEntry> Evict(IReadOnlyList<HistoryEntry> entries, int maxItems, long maxBytes)
    {
        int keep = 0;
        long total = 0;
        while (keep < entries.Count && keep < maxItems && (keep == 0 || total + entries[keep].Size <= maxBytes))
            total += entries[keep++].Size;
        return entries.Skip(keep).Reverse().ToList();
    }
}

/// <summary>
/// Capture history: one file per item in <c>folder</c>, encrypted by <c>protect</c> (DPAPI in the app). Each write goes to a
/// temp file first, so a crash never leaves a half-written item; the folder itself is the index.
/// </summary>
public sealed partial class HistoryStore(string folder, Func<byte[], byte[]> protect, Func<byte[], byte[]?> unprotect,
    int maxItems = HistoryPolicy.MaxItems, long maxBytes = HistoryPolicy.MaxBytes)
{
    private const string Extension = ".cmh";

    [GeneratedRegex(@"^\d{8}-\d{9}(-\d{4})?$")]
    private static partial Regex IdPattern();

    // History ids only: the shape plus a real date, so a crafted name can neither leave the folder nor break TakenUtc.
    private static bool IsId(string id) => IdPattern().IsMatch(id) && DateTime.TryParseExact(id.AsSpan(0, 18), "yyyyMMdd-HHmmssfff",
        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _);

    /// <summary>Stores the item and evicts old ones; returns its id.</summary>
    public string Add(byte[] data, DateTime utcNow)
    {
        Directory.CreateDirectory(folder);
        string stamp = utcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        string id = stamp;
        for (int n = 2; File.Exists(PathOf(id)); n++)
            id = $"{stamp}-{n:D4}";
        Write(id, data);
        foreach (var old in HistoryPolicy.Evict(List(), maxItems, maxBytes))
        {
            try
            {
                Delete(old.Id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // locked (antivirus): evicted on a later add
        }
        return id;
    }

    /// <summary>Newest first; files that are not history items are ignored.</summary>
    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(folder))
            return [];
        return new DirectoryInfo(folder).EnumerateFiles("*" + Extension)
            .Select(f => new HistoryEntry(Path.GetFileNameWithoutExtension(f.Name), f.Length))
            .Where(e => IsId(e.Id))
            .OrderByDescending(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The decrypted item, or null when it is gone, unreadable or cannot be decrypted.</summary>
    public byte[]? Load(string id)
    {
        if (!IsId(id))
            return null;
        try
        {
            return unprotect(File.ReadAllBytes(PathOf(id)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(string id, byte[] data)
    {
        string path = PathOf(id), temp = path + ".tmp";
        File.WriteAllBytes(temp, protect(data));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Replaces an item's content in place; false when the item is gone (evicted, deleted, cleared).</summary>
    public bool Update(string id, byte[] data)
    {
        if (!IsId(id) || !File.Exists(PathOf(id)))
            return false;
        Write(id, data);
        return true;
    }

    public void Delete(string id)
    {
        if (IsId(id))
            File.Delete(PathOf(id));
    }

    /// <summary>Deletes all items and leftover temp files.</summary>
    public void Clear()
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder, "*" + Extension).Concat(Directory.EnumerateFiles(folder, "*" + Extension + ".tmp")))
            File.Delete(file);
    }

    private string PathOf(string id) => Path.Combine(folder, id + Extension);
}
