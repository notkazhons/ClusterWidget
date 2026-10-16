# Dev loop: stop the widget, publish (Native AOT), relaunch, and optionally screenshot the desktop.
# Usage: .\tools\dev.ps1 [-Shot out.png] [-NoRun]
param([string]$Shot, [switch]$NoRun)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # ILCompiler needs vswhere on PATH

Get-Process ClusterWidget -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish "$root\src\ClusterWidget" -c Release -o "$root\publish" -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
if ($NoRun) { return }
Start-Process "$root\publish\ClusterWidget.exe"
Start-Sleep -Seconds 2

if ($Shot) {
    Add-Type -AssemblyName System.Drawing, System.Windows.Forms
    Add-Type 'using System.Runtime.InteropServices; public class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
    [Dpi]::SetProcessDPIAware() | Out-Null
    $sh = New-Object -ComObject Shell.Application
    try {
        $sh.MinimizeAll(); Start-Sleep -Milliseconds 1500   # the widget sits at the bottom of the z-order
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $bmp.Save($Shot)
    } finally { $sh.UndoMinimizeALL() }
}
