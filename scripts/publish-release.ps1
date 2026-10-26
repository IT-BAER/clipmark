# Builds the GitHub release zip: Native AOT, self-contained .NET and Windows App SDK (no prerequisites), x64.
# Output: artifacts\release\Clipmark-<version>-win-x64.zip and a .sha256 file next to it. The pdb stays out of the zip.
# -Dev: the owner's dev build named "Clipmark (dev)" (ClipmarkDev), in artifacts\release-dev; never published.
param([switch]$Dev)
$ErrorActionPreference = 'Stop'
# ILCompiler's link step calls vswhere.exe by bare name; the VS Installer folder is not on PATH by default.
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$name = if ($Dev) { "Clipmark-$version-dev-win-x64" } else { "Clipmark-$version-win-x64" }
$out = Join-Path $root $(if ($Dev) { 'artifacts\release-dev' } else { 'artifacts\release' })
$stage = Join-Path $out $name
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& dotnet publish (Join-Path $root 'src\Clipmark.App\Clipmark.App.csproj') -c Release -r win-x64 -p:Platform=x64 -nologo -v q `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishAot=true -p:SelfContained=true -p:ClipmarkDev=$($Dev.IsPresent) -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
Get-ChildItem $stage -Filter *.pdb -Recurse | Remove-Item
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md'), (Join-Path $root 'CHANGELOG.md') $stage
$zip = Join-Path $out "$name.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content "$zip.sha256" "$hash  $name.zip" -NoNewline
'{0}  {1:N1} MB  sha256 {2}' -f $zip, ((Get-Item $zip).Length / 1MB), $hash
