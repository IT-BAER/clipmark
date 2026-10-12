using System.Globalization;
using System.Security.Cryptography;
using Clipmark.Core.Licensing;

// Issues Clipmark license keys. The private key file must stay outside any Git repository.
return args switch
{
    ["keygen", var path] => KeyGen(path),
    ["public", var path] => PrintPublicKey(path),
    ["issue", var path, var id] => Issue(path, id, DateOnly.FromDateTime(DateTime.UtcNow)),
    ["issue", var path, var id, var date] => Issue(path, id, DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
    ["verify", var publicKey, var licenseKey] => Verify(publicKey, licenseKey),
    _ => Usage(),
};

static int KeyGen(string path)
{
    path = Path.GetFullPath(path);
    if (InsideGitRepository(path))
        return Fail($"Refusing to write a private key inside a Git repository: {path}");
    if (File.Exists(path))
        return Fail($"Refusing to overwrite an existing key: {path}");
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows())
        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // Windows: profile ACLs are owner-only
    using (var file = new FileStream(path, options))
    using (var writer = new StreamWriter(file))
        writer.Write(key.ExportPkcs8PrivateKeyPem());
    Console.Error.WriteLine($"Private key written to {path}. Move it into your password manager.");
    Console.WriteLine(PublicKeyText(key));
    return 0;
}

static int PrintPublicKey(string path)
{
    using var key = Load(path);
    Console.WriteLine(PublicKeyText(key));
    return 0;
}

static int Issue(string path, string id, DateOnly issued)
{
    if (!ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong licenseId))
        return Fail($"License id must be a whole number: {id}");
    using var key = Load(path);
    Console.WriteLine(LicenseKey.Sign(new LicensePayload(licenseId, issued), key));
    return 0;
}

static int Verify(string publicKey, string licenseKey)
{
    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
    if (!LicenseKey.TryVerify(licenseKey, key, out var payload))
        return Fail("Invalid license key.");
    Console.WriteLine($"Valid: license {payload.LicenseId}, issued {payload.Issued:yyyy-MM-dd}");
    return 0;
}

static ECDsa Load(string path)
{
    var key = ECDsa.Create();
    key.ImportFromPem(File.ReadAllText(path));
    return key;
}

/// <summary>Base64 SubjectPublicKeyInfo: the value the app embeds.</summary>
static string PublicKeyText(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

static bool InsideGitRepository(string path)
{
    for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null; dir = dir.Parent)
        // A .git folder without HEAD is not a repository; a .git file is a worktree.
        if (File.Exists(Path.Combine(dir.FullName, ".git", "HEAD")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            return true;
    return false;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("""
        Usage:
          Clipmark.KeyTool keygen <private-key.pem>            create a key pair, print the public key
          Clipmark.KeyTool public <private-key.pem>            print the public key again
          Clipmark.KeyTool issue <private-key.pem> <id> [date]  print a license key (date yyyy-MM-dd, default today UTC)
          Clipmark.KeyTool verify <public-key> <license-key>    check a customer's key
        """);
    return 2;
}
