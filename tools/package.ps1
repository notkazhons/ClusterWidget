# Builds the Native AOT executable and packs the release zip into .\dist
#   .\tools\package.ps1            -> dist\ClusterWidget-<version>-win-x64.zip (+ .sha256)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # ILCompiler needs vswhere on PATH

$csproj = Join-Path $root 'src\ClusterWidget\ClusterWidget.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "no <Version> in $csproj" }

$stage = Join-Path $root "dist\stage"
$dist = Join-Path $root 'dist'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish $csproj -c Release -o $stage -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# the zip holds just what a user needs
$pkg = Join-Path $dist "pkg"
Remove-Item $pkg -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $stage 'ClusterWidget.exe') $pkg
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md') $pkg

$zip = Join-Path $dist "ClusterWidget-$version-win-x64.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Set-Content -Encoding ascii "$zip.sha256"
Remove-Item $stage, $pkg -Recurse -Force

"{0}  {1:N2} MB" -f $zip, ((Get-Item $zip).Length / 1MB)
"sha256: $hash"