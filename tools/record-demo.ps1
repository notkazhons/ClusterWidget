# Records the open / close animation of an isolated cluster as PNG frames (+ timestamps), for tools\make_gif.py.
#   .\tools\record-demo.ps1 -Source <cluster.json> -Out <folder>
# It starts a throw-away copy of the app (own settings folder) using the look and icons of <cluster.json>,
# clicks it open, waits, clicks away so it closes, and screenshots a region the whole time.
param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Out,
    [string]$Exe = "$env:TEMP\cwbuild\ClusterWidget.exe",
    [int]$X = 820, [int]$Y = 300,                     # where the demo cluster sits (screen pixels)
    [int[]]$Region = @(620, 150, 700, 480),           # left, top, width, height of the captured area
    [int]$AnimationMs = 420                           # slower than normal so the GIF shows the motion
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Rec {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
[Rec]::SetProcessDPIAware() | Out-Null

$root = Join-Path $env:TEMP 'cwrec'
Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$root\clusters" | Out-Null
$cfg = Get-Content $Source -Raw | ConvertFrom-Json
$cfg | Add-Member snap $false -Force; $cfg | Add-Member freeX $X -Force; $cfg | Add-Member freeY $Y -Force
$cfg | Add-Member animationMs $AnimationMs -Force; $cfg | Add-Member name 'Demo' -Force
($cfg | ConvertTo-Json -Depth 10) | Set-Content -Encoding utf8 "$root\clusters\cluster-1.json"

New-Item -ItemType Directory -Force $Out | Out-Null
Get-ChildItem $Out -Filter *.png | Remove-Item
$env:CLUSTERWIDGET_DIR = $root
$proc = Start-Process $Exe -PassThru
$sh = New-Object -ComObject Shell.Application
try {
    Start-Sleep -Seconds 2
    $sh.MinimizeAll(); Start-Sleep -Milliseconds 1500
    [Rec]::SetCursorPos(($X + 700), ($Y + 500)) | Out-Null; Start-Sleep -Milliseconds 600

    $clickOpen = @(($X + 90), ($Y + 8))          # top padding of the closed box: opens it without launching an icon
    $clickAway = @(($X + 700), ($Y + 480))       # empty desktop: closes it
    $bmps = New-Object System.Collections.Generic.List[object]
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $step = 0
    while ($clock.ElapsedMilliseconds -lt 6600) {
        $t = $clock.ElapsedMilliseconds
        if ($step -eq 0 -and $t -gt 500)  { [Rec]::SetCursorPos($clickOpen[0], $clickOpen[1]) | Out-Null; $step = 1 }
        if ($step -eq 1 -and $t -gt 1500)  { [Rec]::mouse_event(2,0,0,0,0); [Rec]::mouse_event(4,0,0,0,0); $step = 2 }
        if ($step -eq 2 -and $t -gt 4200) { [Rec]::SetCursorPos($clickAway[0], $clickAway[1]) | Out-Null; $step = 3 }
        if ($step -eq 3 -and $t -gt 4600) { [Rec]::mouse_event(2,0,0,0,0); [Rec]::mouse_event(4,0,0,0,0); $step = 4 }
        $bm = New-Object System.Drawing.Bitmap $Region[2], $Region[3]
        $g = [System.Drawing.Graphics]::FromImage($bm)
        $g.CopyFromScreen($Region[0], $Region[1], 0, 0, $bm.Size)
        $p = New-Object Rec+POINT; [Rec]::GetCursorPos([ref]$p) | Out-Null
        $cur = New-Object System.Drawing.Rectangle -ArgumentList ($p.X - $Region[0]), ($p.Y - $Region[1]), 32, 32
        [System.Windows.Forms.Cursors]::Arrow.Draw($g, $cur)
        $g.Dispose()
        $bmps.Add(@($t, $bm))
    }
    $i = 0; $times = @()
    foreach ($e in $bmps) { $e[1].Save((Join-Path $Out ("f{0:D3}.png" -f $i))); $times += $e[0]; $e[1].Dispose(); $i++ }
    $times -join "," | Set-Content -Encoding ascii (Join-Path $Out 'times.txt')
    "captured $i frames in $($clock.ElapsedMilliseconds) ms"
}
finally {
    $sh.UndoMinimizeALL()
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    Remove-Item Env:\CLUSTERWIDGET_DIR -ErrorAction SilentlyContinue
    Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
