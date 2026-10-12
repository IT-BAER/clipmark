using System.Security.Cryptography;
using Clipmark.Core.Licensing;
using Microsoft.Win32;

namespace Clipmark.App;

/// <summary>
/// License key and trial: %LOCALAPPDATA%\Clipmark\license.json, trial mirrored in HKCU\Software\Clipmark.
/// Debug test mode keeps the file in CLIPMARK_TEST_DIR and the mirror under CLIPMARK_TEST_REGROOT.
/// </summary>
internal static class AppLicense
{
    public const string BuyUrl = "https://it-baer.net/clipmark";

    /// <summary>Base64 SubjectPublicKeyInfo from tools/Clipmark.KeyTool; the private key is in the owner's password manager.</summary>
    private const string PublicKeyText =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEsqGfAZVMYk5fRNlYfZ5rGvxs06wvkeR2AwrgaeTv1TPZBOtoICqQ/hG5F+x/+ak11ULNQb3rtNO9iduxS1d77w==";

    private const string MirrorKey = @"Software\Clipmark", MirrorValue = "Trial";

    private static readonly ECDsa PublicKey = LoadPublicKey();

    private static readonly string FilePath = Path.Combine(
        App.TestDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipmark"), "license.json");

    public static Entitlement Current { get; private set; } = new(LicenseStatus.Free);

    public static event Action<Entitlement>? Changed;

    private static ECDsa LoadPublicKey()
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyText), out _);
        return key;
    }

    /// <summary>Counts the trial forward and re-checks the key. Called at startup and when a capture opens.</summary>
    public static void Refresh()
    {
        var file = LicenseStore.Load(FilePath);
        var mirror = ReadMirror();
        var (daysLeft, trial) = TrialClock.Check(TrialClock.Merge(file.Trial, mirror), DateTime.UtcNow);
        if (file.Trial != trial)
            TrySave(file with { Trial = trial });
        if (mirror != trial)
            WriteMirror(trial);
        var next = Entitlement.Evaluate(file.Key, PublicKey, daysLeft);
        if (next == Current)
            return;
        Current = next;
        Changed?.Invoke(next);
    }

    /// <summary>Stores the key when it is valid; false (nothing stored) otherwise.</summary>
    public static bool Activate(string key)
    {
        key = key.Trim();
        if (!LicenseKey.TryVerify(key, PublicKey, out _))
            return false;
        TrySave(LicenseStore.Load(FilePath) with { Key = key });
        Refresh();
        return true;
    }

    /// <summary>A failed write only loses trial bookkeeping, never user data, so it must not stop the app.</summary>
    private static void TrySave(LicenseFile file)
    {
        try { LicenseStore.Save(FilePath, file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string MirrorPath()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 } root)
            return root + "\\" + MirrorKey;
#endif
        return MirrorKey;
    }

    private static TrialState? ReadMirror()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MirrorPath());
            return key?.GetValue(MirrorValue) is string json ? LicenseStore.Parse(json).Trial : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static void WriteMirror(TrialState trial)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MirrorPath());
            key.SetValue(MirrorValue, LicenseStore.Serialize(new LicenseFile(null, trial)));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }
}
