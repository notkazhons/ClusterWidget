# Builds the Native AOT executable and packs the release zip into .\dist
#   .\tools\package.ps1                      build + zip            -> dist\ClusterWidget-<version>-win-x64.zip (+ .sha256)
#   .\tools\package.ps1 -BuildOnly           build only             -> dist\exe\ClusterWidget.exe (to be code-signed)
#   .\tools\package.ps1 -ExePath <file>      zip a ready-made exe   (for example the signed one) without building
param(
    [switch]$BuildOnly,
    [string]$ExePath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # ILCompiler needs vswhere on PATH

$csproj = Join-Path $root 'src\ClusterWidget\ClusterWidget.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "no <Version> in $csproj" }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

if (-not $ExePath) {
    $stage = Join-Path $dist 'stage'
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $stage | Out-Null
    dotnet publish $csproj -c Release -o $stage -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
    $exeOut = Join-Path $dist 'exe'
    Remove-Item $exeOut -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $exeOut | Out-Null
    Copy-Item (Join-Path $stage 'ClusterWidget.exe') $exeOut
    Remove-Item $stage -Recurse -Force
    $ExePath = Join-Path $exeOut 'ClusterWidget.exe'
    if ($BuildOnly) { "built: $ExePath"; return }
}
if (-not (Test-Path $ExePath)) { throw "exe not found: $ExePath" }

# the zip holds just what a user needs
$pkg = Join-Path $dist 'pkg'
Remove-Item $pkg -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item $ExePath (Join-Path $pkg 'ClusterWidget.exe')
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md') $pkg

$zip = Join-Path $dist "ClusterWidget-$version-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -Encoding ascii "$zip.sha256"
$sig = Get-AuthenticodeSignature $ExePath
Remove-Item $pkg -Recurse -Force

"{0}  {1:N2} MB" -f $zip, ((Get-Item $zip).Length / 1MB)
"sha256: $hash"
"exe signature: $($sig.Status)"
