# Drives an isolated ClusterWidget copy (own settings folder, own mutex) for manual-style UI checks.
#   .\tools\ui.ps1 start [-Exe path] [-Items 12] [-Json '{...extra cluster fields...}']
#   .\tools\ui.ps1 hide                 minimise every window (so the desktop is visible)
#   .\tools\ui.ps1 shot out.png [x y w h]
#   .\tools\ui.ps1 click x y | rclick x y | move x y | key 0x1B | text "abc"
#   .\tools\ui.ps1 restore              bring the windows back
#   .\tools\ui.ps1 stop                 close the test copy and delete its folder
[CmdletBinding(PositionalBinding = $false)]
param([Parameter(Mandatory, Position = 0)][string]$Do, [Parameter(Position = 1, ValueFromRemainingArguments)][string[]]$Rest, [string]$Exe, [int]$Items = 12, [string]$Json = '{}')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Ui {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, int fl, int ex);
}
"@
[Ui]::SetProcessDPIAware() | Out-Null
$root = Join-Path $env:TEMP 'cwui'
$pidFile = Join-Path $root 'pid.txt'

function Click($x, $y, $right = $false) {
    [Ui]::SetCursorPos([int]$x, [int]$y); Start-Sleep -Milliseconds 250
    if ($right) { [Ui]::mouse_event(8,0,0,0,0); Start-Sleep -Milliseconds 40; [Ui]::mouse_event(16,0,0,0,0) }
    else { [Ui]::mouse_event(2,0,0,0,0); Start-Sleep -Milliseconds 40; [Ui]::mouse_event(4,0,0,0,0) }
    Start-Sleep -Milliseconds 500
}

switch ($Do) {
    'start' {
        if (-not $Exe) { $Exe = "$env:TEMP\cwbuild\ClusterWidget.exe" }
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force "$root\clusters" | Out-Null
        $files = Get-ChildItem C:\Windows\System32 -Filter *.exe | Select-Object -First $Items -ExpandProperty FullName
        $cfg = [ordered]@{ name = 'Test A'; snap = $false; freeX = 300; freeY = 200; closedCount = 4; items = @($files | ForEach-Object { @{ path = $_ } }) }
        $extra = $Json | ConvertFrom-Json
        foreach ($p in $extra.PSObject.Properties) { $cfg[$p.Name] = $p.Value }
        $cfg | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$root\clusters\cluster-1.json"
        $env:CLUSTERWIDGET_DIR = $root
        $proc = Start-Process $Exe -PassThru
        $proc.Id | Set-Content $pidFile
        Start-Sleep -Seconds 2
        "started pid $($proc.Id)"
    }
    'hide' { (New-Object -ComObject Shell.Application).MinimizeAll(); Start-Sleep -Milliseconds 1200 }
    'restore' { (New-Object -ComObject Shell.Application).UndoMinimizeALL() }
    'shot' {
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bm = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        [System.Drawing.Graphics]::FromImage($bm).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $r = if ($Rest.Count -ge 5) { [System.Drawing.Rectangle]::new([int]$Rest[1], [int]$Rest[2], [int]$Rest[3], [int]$Rest[4]) } else { [System.Drawing.Rectangle]::new(0, 0, $b.Width, $b.Height) }
        $bm.Clone($r, $bm.PixelFormat).Save($Rest[0]); $bm.Dispose()
    }
    'click' { Click $Rest[0] $Rest[1] }
    'rclick' { Click $Rest[0] $Rest[1] $true }
    'drag' {
        $x1 = [int]$Rest[0]; $y1 = [int]$Rest[1]; $x2 = [int]$Rest[2]; $y2 = [int]$Rest[3]
        [Ui]::SetCursorPos($x1, $y1); Start-Sleep -Milliseconds 250; [Ui]::mouse_event(2,0,0,0,0)
        $steps = 12
        for ($i = 1; $i -le $steps; $i++) { [Ui]::SetCursorPos([int]($x1 + ($x2 - $x1) * $i / $steps), [int]($y1 + ($y2 - $y1) * $i / $steps)); Start-Sleep -Milliseconds 30 }
        [Ui]::mouse_event(4,0,0,0,0); Start-Sleep -Milliseconds 500
    }
    'move' { [Ui]::SetCursorPos([int]$Rest[0], [int]$Rest[1]); Start-Sleep -Milliseconds 500 }
    'key' { $vk = [Convert]::ToInt32($Rest[0], 16); [Ui]::keybd_event([byte]$vk,0,0,0); [Ui]::keybd_event([byte]$vk,0,2,0); Start-Sleep -Milliseconds 500 }
    'text' { [System.Windows.Forms.SendKeys]::SendWait($Rest[0]); Start-Sleep -Milliseconds 400 }
    'config' { Get-Content "$root\clusters\*.json" -Raw }
    'stop' {
        if (Test-Path $pidFile) { $id = [int](Get-Content $pidFile); Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        "stopped"
    }
}
