# Builds the Microsoft Store package: Native AOT, self-contained .NET, framework-dependent Windows App SDK, x64.
# Unsigned, because the Store signs it. Output: the .msix for Partner Center under artifacts\store\.
# No .msixupload: under the dotnet CLI the StoreUpload mode fails in WinAppSdkCreateAppStoreContainer (System.Security.Permissions).
# Fails when the package version is not <Directory.Build.props Version>.0 or resources.pri lacks a language from Strings\.
$ErrorActionPreference = 'Stop'
# ILCompiler's link step calls vswhere.exe by bare name; the VS Installer folder is not on PATH by default.
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'src\Clipmark.App'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$out = Join-Path $root 'artifacts\store'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& dotnet publish (Join-Path $app 'Clipmark.App.csproj') -c Release -r win-x64 -p:Platform=x64 -nologo -v q `
    -p:PublishAot=true -p:SelfContained=true -p:WindowsAppSDKSelfContained=false `
    -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false `
    -p:AppxBundle=Never "-p:AppxPackageDir=$out\"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$msix = @(Get-ChildItem $out -Recurse -Filter *.msix | Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($msix.Count -ne 1) { throw "expected one app .msix in $out, found $($msix.Count)" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$check = Join-Path $out 'check'
New-Item $check -ItemType Directory | Out-Null
$zip = [IO.Compression.ZipFile]::OpenRead($msix[0].FullName)
try {
    foreach ($entry in 'AppxManifest.xml', 'resources.pri') {
        [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($entry), (Join-Path $check $entry))
    }
} finally { $zip.Dispose() }

$identity = ([xml](Get-Content (Join-Path $check 'AppxManifest.xml'))).Package.Identity
if ($identity.Version -ne "$version.0") { throw "package version $($identity.Version), expected $version.0" }

$makepri = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\makepri.exe" | Sort-Object FullName | Select-Object -Last 1
& $makepri.FullName dump /if (Join-Path $check 'resources.pri') /of (Join-Path $check 'resources.xml') /dt detailed /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makepri dump failed ($LASTEXITCODE)" }
$priLanguages = Select-String -Path (Join-Path $check 'resources.xml') -Pattern 'qualifier name="Language" value="([^"]+)"' -AllMatches |
    ForEach-Object { $_.Matches.Groups[1].Value.ToLowerInvariant() } | Sort-Object -Unique
$missing = Get-ChildItem (Join-Path $app 'Strings') -Directory | ForEach-Object { $_.Name.ToLowerInvariant() } | Where-Object { $_ -notin $priLanguages }
if ($missing) { throw "resources.pri lacks languages: $($missing -join ', ')" }

'{0}  {1:N1} MB  {2} {3}  languages {4}' -f $msix[0].FullName, ($msix[0].Length / 1MB), $identity.Name, $identity.Version, @($priLanguages).Count
