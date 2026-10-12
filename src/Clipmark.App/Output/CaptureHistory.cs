using System.ComponentModel;
using System.Runtime.InteropServices;
using Clipmark.Core.History;
using Clipmark.Core.Licensing;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Output;

/// <summary>
/// Premium capture history in %LOCALAPPDATA%\Clipmark\History (never Pictures: it is often synced to OneDrive). Each item is
/// sealed with DPAPI for the current Windows user. Failures never block a capture: the item is skipped.
/// </summary>
internal static class CaptureHistory
{
    public static readonly string Folder = Path.Combine(
        App.TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipmark"), "History");

    private static readonly byte[] Entropy = "Clipmark capture history 1"u8.ToArray();
    private static readonly HistoryStore Store = new(Folder, Protect, Unprotect);
    private static readonly Dictionary<string, string> s_idByPath = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsOn => AppSettings.Current.HistoryEnabled && AppLicense.Current.IsEnabled(PremiumFeature.History);

    /// <summary>Adds a new capture; <paramref name="path"/> is its file, so an editor opened on it later writes back here.</summary>
    public static async Task AddAsync(string path, byte[] png)
    {
        if (!IsOn)
            return;
        if (await Run(() => IsOn ? Store.Add(png, DateTime.UtcNow) : null) is { } id) // checked again: the switch may turn off meanwhile
            lock (s_idByPath)
                s_idByPath[path] = id;
    }

    public static string? IdFor(string path)
    {
        lock (s_idByPath)
            return s_idByPath.GetValueOrDefault(path);
    }

    public static async Task<IReadOnlyList<HistoryEntry>> ListAsync() => await Run(Store.List) ?? [];

    public static Task<byte[]?> LoadAsync(string id) => Run(() => Store.Load(id));

    public static Task UpdateAsync(string id, byte[] data) => Run(() => Store.Update(id, data));

    public static Task DeleteAsync(string id) => Run(() => { Store.Delete(id); return true; });

    public static Task ClearAsync() => Run(() => { Store.Clear(); return true; });

    // One store operation at a time, off the UI thread; IO and DPAPI failures give null.
    private static Task<T?> Run<T>(Func<T> operation) => Task.Run(() =>
    {
        try
        {
            lock (Store)
                return operation();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return default;
        }
    });

    private static byte[] Protect(byte[] data) => Crypt(data, protect: true)
        ?? throw new Win32Exception(Marshal.GetLastPInvokeError(), "CryptProtectData failed.");

    private static byte[]? Unprotect(byte[] data) => Crypt(data, protect: false);

    private static unsafe byte[]? Crypt(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        fixed (byte* salt = Entropy)
        {
            var inBlob = new DATA_BLOB { cbData = (uint)data.Length, pbData = input };
            var entropy = new DATA_BLOB { cbData = (uint)Entropy.Length, pbData = salt };
            DATA_BLOB outBlob = default;
            bool ok = protect
                ? CryptProtectData(&inBlob, null, &entropy, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, &outBlob)
                : CryptUnprotectData(&inBlob, null, &entropy, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, &outBlob);
            if (!ok)
                return null;
            try
            {
                return new ReadOnlySpan<byte>(outBlob.pbData, (int)outBlob.cbData).ToArray();
            }
            finally
            {
                LocalFree((nint)outBlob.pbData);
            }
        }
    }
}
