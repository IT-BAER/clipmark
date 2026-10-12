using System.Security.Cryptography;
using System.Text;
using Clipmark.App.Interop;
using Clipmark.Core.Licensing;
using Microsoft.Win32;
using Windows.Security.Cryptography;
using Windows.System.Profile;

namespace Clipmark.App;

/// <summary>
/// License key and trial: %LOCALAPPDATA%\Clipmark\license.json, trial mirrored in HKCU\Software\Clipmark and in the
/// Credential Manager entry "Clipmark/Trial" (the copy that survives an MSIX uninstall). Every trial copy carries a
/// <see cref="TrialSeal"/> tag bound to this device. Debug test mode keeps the file in CLIPMARK_TEST_DIR and the
/// mirror and credential under CLIPMARK_TEST_REGROOT.
/// </summary>
internal static class AppLicense
{
    public const string BuyUrl = "https://it-baer.net/clipmark";

    /// <summary>Base64 SubjectPublicKeyInfo from tools/Clipmark.KeyTool; the private key is in the owner's password manager.</summary>
    private const string PublicKeyText =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEsqGfAZVMYk5fRNlYfZ5rGvxs06wvkeR2AwrgaeTv1TPZBOtoICqQ/hG5F+x/+ak11ULNQb3rtNO9iduxS1d77w==";

    private const string MirrorKey = @"Software\Clipmark", MirrorValue = "Trial", VaultTarget = "Clipmark/Trial";

    private static readonly ECDsa PublicKey = LoadPublicKey();

    private static readonly Lazy<byte[]> DeviceSecret = new(ReadDeviceSecret);

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
        var loaded = TryLoad();
        var file = loaded ?? new LicenseFile(null, null);
        var mirror = ReadMirror();
        var vault = ReadVault();
        var (daysLeft, trial) = TrialSeal.Check([file, mirror, vault], DateTime.UtcNow);
        var copy = new LicenseFile(null, trial, TrialSeal.Sign(trial, DeviceSecret.Value));
        if (loaded is not null && file with { Key = null } != copy)
            TrySave(copy with { Key = file.Key });
        if (mirror != copy)
            WriteMirror(copy);
        if (vault != copy)
            WriteVault(copy);
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
        TrySave((TryLoad() ?? new LicenseFile(null, null)) with { Key = key });
        Refresh();
        return true;
    }

    /// <summary>Null when the file cannot be read; Refresh then leaves it alone so the stored key is not overwritten.</summary>
    private static LicenseFile? TryLoad()
    {
        try { return LicenseStore.Load(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
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

    private static LicenseFile ReadMirror()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MirrorPath());
            return key?.GetValue(MirrorValue) is string json ? LicenseStore.Parse(json) with { Key = null } : new LicenseFile(null, null);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return new LicenseFile(null, null);
        }
    }

    private static void WriteMirror(LicenseFile copy)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MirrorPath());
            key.SetValue(MirrorValue, LicenseStore.Serialize(copy));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static string VaultTargetName()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLIPMARK_TEST_REGROOT") is { Length: > 0 } root)
            return VaultTarget + "/" + root;
#endif
        return VaultTarget;
    }

    private static unsafe LicenseFile ReadVault()
    {
        if (!Native.CredRead(VaultTargetName(), Native.CRED_TYPE_GENERIC, 0, out var cred))
            return new LicenseFile(null, null);
        try
        {
            if (cred->CredentialBlob is null || cred->CredentialBlobSize == 0)
                return new LicenseFile(null, null);
            return LicenseStore.Parse(Encoding.UTF8.GetString(cred->CredentialBlob, (int)cred->CredentialBlobSize)) with { Key = null };
        }
        finally
        {
            Native.CredFree(cred);
        }
    }

    /// <summary>A failed write is ignored like the other copies: the trial only loses one backup.</summary>
    private static unsafe void WriteVault(LicenseFile copy)
    {
        byte[] blob = Encoding.UTF8.GetBytes(LicenseStore.Serialize(copy));
        fixed (char* target = VaultTargetName())
        fixed (char* user = "Clipmark")
        fixed (byte* data = blob)
        {
            var cred = new Native.CREDENTIALW
            {
                Type = Native.CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlob = data,
                CredentialBlobSize = (uint)blob.Length,
                Persist = Native.CRED_PERSIST_LOCAL_MACHINE,
            };
            Native.CredWrite(&cred, 0);
        }
    }

    /// <summary>
    /// HMAC key for the trial tags: the publisher-scoped system ID, else the Windows MachineGuid, else a constant
    /// (no device binding, but edited dates still fail).
    /// </summary>
    private static byte[] ReadDeviceSecret() => ReadDeviceId(out _) ?? Encoding.UTF8.GetBytes("clipmark-no-device-id");

    private static byte[]? ReadDeviceId(out string source)
    {
        try
        {
            var info = SystemIdentification.GetSystemIdForPublisher();
            if (info?.Id is { Length: > 0 } id && info.Source != SystemIdentificationSource.None)
            {
                CryptographicBuffer.CopyToByteArray(id, out byte[] bytes);
                source = "SystemIdForPublisher/" + info.Source;
                return bytes;
            }
        }
        catch (Exception) { } // WinRT surfaces any HRESULT; the next source still works
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string { Length: > 0 } guid)
            {
                source = "MachineGuid";
                return Encoding.UTF8.GetBytes(guid);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        source = "none";
        return null;
    }

#if DEBUG
    /// <summary>`--trial-status FILE` (Debug): entitlement, device-ID source and the state of each stored copy.</summary>
    public static string Describe()
    {
        var id = ReadDeviceId(out string source);
        string Copy(LicenseFile c) => c.Trial is null && c.TrialTag is null ? "missing"
            : $"{(TrialSeal.Verify(c.Trial, c.TrialTag, DeviceSecret.Value) ? "valid" : "INVALID")} {c.Trial?.StartedUtc:O}..{c.Trial?.LastSeenUtc:O}";
        return $"""
            status={Current.Status} daysLeft={Current.TrialDaysLeft} packaged={Output.CaptureOutput.IsPackaged()}
            deviceId={source} sha256={(id is null ? "-" : Convert.ToHexString(SHA256.HashData(id))[..16])}
            file={Copy(TryLoad() ?? new LicenseFile(null, null))} ({FilePath})
            registry={Copy(ReadMirror())}
            credential={Copy(ReadVault())} ({VaultTargetName()})

            """;
    }
#endif
}
