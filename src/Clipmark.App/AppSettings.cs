using Clipmark.Core.Settings;

namespace Clipmark.App;

/// <summary>Current settings, persisted to %LOCALAPPDATA%\Clipmark\settings.json on every change.</summary>
internal static class AppSettings
{
    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipmark", "settings.json");

    public static ClipmarkSettings Current { get; private set; } = SettingsStore.Load(FilePath);

    public static event Action<ClipmarkSettings>? Changed;

    public static void Update(Func<ClipmarkSettings, ClipmarkSettings> change)
    {
        var next = change(Current);
        if (next == Current)
            return;
        Current = next;
        SettingsStore.Save(FilePath, next);
        Changed?.Invoke(next);
    }
}
