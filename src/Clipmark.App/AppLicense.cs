using System.Security.Cryptography;
using System.Text;
using Clipmark.App.Interop;
using Clipmark.App.Relay;
using Clipmark.Core.Licensing;
using Clipmark.Core.Relay;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Security.Cryptography;
using Windows.System.Profile;

namespace Clipmark.App;

/// <summary>
/// Premium state in %LOCALAPPDATA%\Clipmark\license.json: the releash entitlement (signed, bound to <see cref="DeviceKey"/>)
/// is checked offline; the server is called per <see cref="RefreshPolicy"/> (trial start, half-TTL refresh, v1 key
/// activation). Migration window: a v1 key counts offline until it is activated; local trial copies (file, HKCU\Software\Clipmark,
/// Credential Manager "Clipmark/Trial") count until the first server contact and are deleted at the first stored entitlement.
/// Debug test mode keeps the file in CLIPMARK_TEST_DIR and the mirror, credential and device key under CLIPMARK_TEST_REGROOT.
/// </summary>
internal static class AppLicense
{
    public const string BuyUrl = "https://it-baer.net/clipmark";

    /// <summary>Base64 SubjectPublicKeyInfo from tools/Clipmark.KeyTool; the private key is in the owner's password manager.</summary>
    private const string PublicKeyText =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEsqGfAZVMYk5fRNlYfZ5rGvxs06wvkeR2AwrgaeTv1TPZBOtoICqQ/hG5F+x/+ak11ULNQb3rtNO9iduxS1d77w==";

    /// <summary>
    /// Production entitlement keys, kid to base64 SPKI (`releash key show clipmark`). Empty until the key is created at the
    /// host run, so a Release build verifies no token (fail closed).
    /// </summary>
    private static readonly Dictionary<string, string> ReleaseKeys = new(StringComparer.Ordinal);

    private const string MirrorKey = @"Software\Clipmark", MirrorValue = "Trial", VaultTarget = "Clipmark/Trial";

    private static readonly ECDsa PublicKey = LoadPublicKey();

    private static readonly IReadOnlyDictionary<string, ECDsa> EntitlementKeys = EntitlementToken.LoadKeys(KeyTexts());

    private static readonly Lazy<byte[]> DeviceSecret = new(ReadDeviceSecret);

    private static readonly string FilePath = Path.Combine(App.DataDir, "license.json");

    private static DispatcherQueue? _ui;
    private static int _syncing;
    private static TokenVerdict _verdict = TokenVerdict.Malformed;
    private static EntitlementClaims? _claims;

    public static Entitlement Current { get; private set; } = new(LicenseStatus.Free);

    public static event Action<Entitlement>? Changed;

    /// <summary>license.json as last read.</summary>
    public static LicenseFile State { get; private set; } = new(null, null);

    /// <summary>The last rejection by the licence server in this session (Settings shows it); null after a success.</summary>
    public static string? LastServerMessage { get; private set; }

    /// <summary>Premium from a verified, non-trial entitlement (Remove from this PC applies).</summary>
    public static bool LicensedByToken => Current.Status == LicenseStatus.Licensed && _verdict == TokenVerdict.Valid && _claims is { Plan: not "trial" };

    /// <summary>Ask AI may use IT-BAER's relay: Premium by entitlement and an install on the server.</summary>
    public static bool CanUseRelay => LicensedByToken && State.InstallId is not null;

    /// <summary>No server contact yet: no install id and never activated.</summary>
    public static bool NeverContacted => State.InstallId is null && State.ActivatedUtc is null && State.Key is null;

    /// <summary>A licence was activated here and is no longer (removed, revoked, expired).</summary>
    public static bool WasActivated => State.ActivatedUtc is not null;

    private static ECDsa LoadPublicKey()
    {
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyText), out _);
        return key;
    }

    private static Dictionary<string, string> KeyTexts()
    {
        var keys = new Dictionary<string, string>(ReleaseKeys, StringComparer.Ordinal);
#if DEBUG
        // The releash vector TEST key (testdata/entitlement/v1.json); never in a Release build.
        keys["dGVzdGtpZDE"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE2XHT79Y4QLGIA1goT5irW334QJ0vGCx8GiitkWCAmDPv9JOsVY+kFT1Tzdezd/Y8nSYSzwNna/uk7Bvr+CrlbA==";
#endif
        return keys;
    }

    /// <summary>Re-checks the stored state offline and, when due, calls the server in the background. Called at startup, hourly and when a capture opens.</summary>
    public static void Refresh()
    {
        _ui ??= DispatcherQueue.GetForCurrentThread();
        var loaded = TryLoad();
        Evaluate(loaded);
        if (loaded is not null && RefreshPolicy.ShouldCall(loaded, DateTime.UtcNow))
        {
            // Low priority: the call starts after the current UI work (at startup: after the window is created).
            if (_ui is null || !_ui.TryEnqueue(DispatcherQueuePriority.Low, () => _ = SyncAsync()))
                _ = SyncAsync();
        }
    }

    /// <summary>One server call if <see cref="RefreshPolicy"/> says so; a network failure only records the attempt.</summary>
    internal static async Task SyncAsync()
    {
        if (Interlocked.Exchange(ref _syncing, 1) == 1)
            return;
        try
        {
            var now = DateTime.UtcNow;
            if (TryLoad() is not { } file || !RefreshPolicy.ShouldCall(file, now))
                return;
            TrySave(file with { LastRefreshUtc = now });
            if (file.Entitlement is { } token && EntitlementToken.ReadClaims(token)?.Plan != "trial")
            {
                var r = await Task.Run(() => LicenceClient.EntitlementAsync(CancellationToken.None));
                if (r.Value is { } fresh)
                    Store(fresh, null, activated: false);
                else if (r.Failure is { Status: 403 } or { EndsEntitlement: true })
                    DropEntitlement(r.Failure);
            }
            else if (file.Entitlement is null && file.Key is { } key)
            {
                var r = await Task.Run(() => LicenceClient.ActivateAsync(key, CancellationToken.None));
                if (r.Value is { } a)
                    Store(a.Entitlement, a.InstallId, activated: true);
                else if (r.Failure is { Status: 403, Code: "licence_invalid" or "subject_revoked" or "licence_expired" } f)
                    Update(s => s with { Key = null }, f.Message());
            }
            else
            {
                var r = await Task.Run(() => LicenceClient.TrialAsync(CancellationToken.None));
                if (r.Value is { } t)
                    Store(t.Entitlement, null, activated: false);
                else if (r.Failure is { EndsEntitlement: true })
                    DropEntitlement(r.Failure);
                else if (r.Failure is { Code: "trial_not_offered" } f)
                    LastServerMessage = f.Message();
            }
        }
        finally
        {
            Volatile.Write(ref _syncing, 0);
        }
    }

    /// <summary>Activates a code or v1 key on the server and stores the entitlement; the result is for the Settings text.</summary>
    public static async Task<LicenceResult<ActivateResult>> ActivateAsync(string text)
    {
        var r = await Task.Run(() => LicenceClient.ActivateAsync(text.Trim(), CancellationToken.None));
        if (r.Value is { } a)
            Store(a.Entitlement, a.InstallId, activated: true);
        return r;
    }

    /// <summary>Frees this PC's seat. True when the entitlement and install id were cleared: on 204, or when the server no longer holds the install; any other failure keeps them.</summary>
    public static async Task<(bool Removed, LicenceResult<string> Result)> DeactivateAsync()
    {
        var r = await Task.Run(() => LicenceClient.DeactivateAsync(CancellationToken.None));
        bool removed = r.Value is not null || r.Failure is { InstallGone: true };
        if (removed)
            Update(s => s with { Entitlement = null, InstallId = null }, null);
        return (removed, r);
    }

    /// <summary>The server ended the licence, trial or install: Premium locks at once. The install id stays for the status text.</summary>
    internal static void DropEntitlement(RelayFailure failure)
    {
        if (_ui is { HasThreadAccess: false } ui)
        {
            ui.TryEnqueue(() => DropEntitlement(failure));
            return;
        }
        Update(s => s with { Entitlement = null, InstallId = s.InstallId ?? DeviceKey.TryOpen()?.Thumbprint }, failure.Message());
    }

    /// <summary>Stores a fresh entitlement; a v1 key is removed once the entitlement verifies here. Deletes the local trial copies.</summary>
    private static void Store(string token, string? installId, bool activated)
    {
        var key = DeviceKey.TryOpen();
        string dev = key?.Thumbprint ?? "";
        bool verifies = EntitlementToken.Verify(token, EntitlementKeys, RelayRequest.App, dev, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out _) == TokenVerdict.Valid;
        Update(s => s with
        {
            Entitlement = token, InstallId = installId ?? dev, KeyStore = key?.KeyStore ?? s.KeyStore,
            ActivatedUtc = activated ? DateTime.UtcNow : s.ActivatedUtc, Key = verifies ? null : s.Key, Trial = null, TrialTag = null,
        }, null);
        DeleteLocalTrialCopies();
    }

    /// <summary>Read, change, write, re-evaluate. An unreadable file is left alone.</summary>
    private static void Update(Func<LicenseFile, LicenseFile> change, string? serverMessage)
    {
        LastServerMessage = serverMessage;
        if (TryLoad() is not { } file)
            return;
        var next = change(file);
        TrySave(next);
        Evaluate(next);
    }

    private static void Evaluate(LicenseFile? loaded)
    {
        var file = loaded ?? new LicenseFile(null, null);
        State = file;
        var now = DateTime.UtcNow;
        (_verdict, _claims) = (TokenVerdict.Malformed, null);
        Entitlement next;
        if (file.Entitlement is { } token)
        {
            _verdict = EntitlementToken.Verify(token, EntitlementKeys, RelayRequest.App, DeviceKey.TryOpen()?.Thumbprint ?? "",
                new DateTimeOffset(now).ToUnixTimeSeconds(), out _claims);
            next = Entitlement.Evaluate(_verdict, _claims, now);
            // A v1 key is only kept next to a token that did not verify at activation.
            if (next.Status == LicenseStatus.Free && file.Key is not null)
                next = Entitlement.EvaluateLegacy(file.Key, PublicKey);
        }
        else if (file.Key is not null)
            next = Entitlement.EvaluateLegacy(file.Key, PublicKey);
        else if (file.InstallId is null && file.ActivatedUtc is null)
            next = LocalTrial(loaded);
        else
            next = new(LicenseStatus.Free);
        if (next == Current)
            return;
        Current = next;
        Changed?.Invoke(next);
    }

    /// <summary>The pre-server local trial, only while copies exist; a fresh install gets none (the trial starts on the server).</summary>
    private static Entitlement LocalTrial(LicenseFile? loaded)
    {
        var file = loaded ?? new LicenseFile(null, null);
        var mirror = ReadMirror();
        var vault = ReadVault();
        if (file.Trial is null && mirror.Trial is null && vault.Trial is null)
            return new(LicenseStatus.Free);
        var (daysLeft, trial) = TrialSeal.Check([file, mirror, vault], DateTime.UtcNow);
        var copy = new LicenseFile(null, trial, TrialSeal.Sign(trial, DeviceSecret.Value));
        if (loaded is not null && (file.Trial != copy.Trial || file.TrialTag != copy.TrialTag))
            TrySave(file with { Trial = copy.Trial, TrialTag = copy.TrialTag });
        if (mirror != copy)
            WriteMirror(copy);
        if (vault != copy)
            WriteVault(copy);
        return daysLeft > 0 ? new(LicenseStatus.Trial, daysLeft) : new(LicenseStatus.Free);
    }

    /// <summary>Null when the file cannot be read; nothing is written then, so the stored state is not overwritten.</summary>
    private static LicenseFile? TryLoad()
    {
        try { return LicenseStore.Load(FilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A failed write loses licence bookkeeping only; the next server call restores it, so it must not stop the app.</summary>
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

    /// <summary>The registry mirror and the credential copy of the local trial; a missing copy is not an error.</summary>
    private static void DeleteLocalTrialCopies()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MirrorPath(), writable: true);
            key?.DeleteValue(MirrorValue, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        Native.CredDelete(VaultTargetName(), Native.CRED_TYPE_GENERIC, 0);
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
    /// <summary>`--trial-status FILE` (Debug): entitlement, server state, device-ID source and each local trial copy. Never prints the token.</summary>
    public static string Describe()
    {
        var id = ReadDeviceId(out string source);
        string Copy(LicenseFile c) => c.Trial is null && c.TrialTag is null ? "missing"
            : $"{(TrialSeal.Verify(c.Trial, c.TrialTag, DeviceSecret.Value) ? "valid" : "INVALID")} {c.Trial?.StartedUtc:O}..{c.Trial?.LastSeenUtc:O}";
        var file = TryLoad() ?? new LicenseFile(null, null);
        var claims = EntitlementToken.ReadClaims(file.Entitlement);
        string token = file.Entitlement is null ? "none"
            : $"{EntitlementToken.Name(_verdict)} plan={claims?.Plan} exp={(claims is null ? "-" : DateTimeOffset.FromUnixTimeSeconds(claims.Exp).ToString("O"))}";
        return $"""
            status={Current.Status} daysLeft={Current.TrialDaysLeft} packaged={Output.CaptureOutput.IsPackaged()}
            token={token}
            installId={file.InstallId ?? "-"} keyStore={file.KeyStore ?? "-"} v1key={(file.Key is null ? "no" : "yes")} activated={file.ActivatedUtc:O} lastRefresh={file.LastRefreshUtc:O}
            server={LastServerMessage ?? "-"}
            deviceId={source} sha256={(id is null ? "-" : Convert.ToHexString(SHA256.HashData(id))[..16])}
            file={Copy(file)} ({FilePath})
            registry={Copy(ReadMirror())}
            credential={Copy(ReadVault())} ({VaultTargetName()})

            """;
    }
#endif
}
