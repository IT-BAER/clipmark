# Publishes the M0 measurement variants into artifacts\m0\<variant>.
$ErrorActionPreference = 'Stop'
# ILCompiler's link step calls vswhere.exe by bare name; the VS Installer folder is not on PATH by default.
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Clipmark.App\Clipmark.App.csproj'
$out = Join-Path $root 'artifacts\m0'
$common = @('-c', 'Release', '-r', 'win-x64', '-p:Platform=x64', '-nologo', '-v', 'q')
$only = $args
$variants = [ordered]@{
    # GitHub zip candidate: no runtime prerequisites.
    'A-aot-selfcontained'  = @('-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true', '-p:PublishAot=true', '-p:SelfContained=true')
    # Store-equivalent runtime model, unpackaged so it can be started for timing.
    'D-aot-framework'      = @('-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=false', '-p:PublishAot=true', '-p:SelfContained=true')
    # Template default: self-contained .NET + Windows App SDK, ReadyToRun + trimmed.
    'C-r2r-selfcontained'  = @('-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true', '-p:PublishAot=false', '-p:SelfContained=true')
}
foreach ($name in $variants.Keys) {
    if ($only -and $name -notin $only) { continue }
    $dir = Join-Path $out $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet publish $proj @common @($variants[$name]) -o $dir 2>&1 | Select-String -Pattern 'error|warning IL' | Select-Object -First 20 | ForEach-Object { "  $_" }
    "$name exit=$LASTEXITCODE seconds=$([int]$sw.Elapsed.TotalSeconds)"
}
# Store candidate: MSIX package, AOT, framework-dependent Windows App SDK. Unsigned, for size only.
$msix = Join-Path $out 'B-aot-msix'
if (Test-Path $msix) { Remove-Item $msix -Recurse -Force }
& dotnet publish $proj @common '-p:PublishAot=true' '-p:SelfContained=true' '-p:WindowsAppSDKSelfContained=false' `
    '-p:GenerateAppxPackageOnBuild=true' '-p:AppxPackageSigningEnabled=false' "-p:AppxPackageDir=$msix\" 2>&1 |
    Select-String -Pattern 'error' | Select-Object -First 20 | ForEach-Object { "  $_" }
"B-aot-msix exit=$LASTEXITCODE"
