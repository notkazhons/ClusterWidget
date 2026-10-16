# Performance harness for ClusterWidget. Runs isolated copies (own settings folder, own mutex) so the real widget is untouched.
# Usage: .\tools\perf.ps1 [-Exe path] [-Scenario name,...]
param(
    [string]$Exe = "$PSScriptRoot\..\publish\ClusterWidget.exe",
    [string[]]$Scenario = @('baseline', 'many-icons', 'huge-icons', 'blur-max', 'six-clusters', 'many-clusters-heavy', 'image-bg', 'drag', 'cycle-leak')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Pf {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, int fl, int ex);
  [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr h, uint flags);
}
"@
[Pf]::SetProcessDPIAware() | Out-Null
$cores = [Environment]::ProcessorCount
$results = New-Object System.Collections.Generic.List[object]

function Click($x, $y) { [Pf]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 250; [Pf]::mouse_event(2,0,0,0,0); Start-Sleep -Milliseconds 40; [Pf]::mouse_event(4,0,0,0,0) }
function Esc { [Pf]::keybd_event(0x1B,0,0,0); [Pf]::keybd_event(0x1B,0,2,0) }
function WinD { [Pf]::keybd_event(0x5B,0,0,0); [Pf]::keybd_event(0x44,0,0,0); [Pf]::keybd_event(0x44,0,2,0); [Pf]::keybd_event(0x5B,0,2,0) }

function Cpu($p, [double]$seconds, [scriptblock]$during) {
    $p.Refresh(); $c0 = $p.TotalProcessorTime; $t0 = [Diagnostics.Stopwatch]::StartNew()
    if ($during) { $null = & $during } else { Start-Sleep -Seconds $seconds }
    $p.Refresh(); $c1 = $p.TotalProcessorTime
    [math]::Round(($c1 - $c0).TotalMilliseconds / $t0.Elapsed.TotalMilliseconds * 100 / 1, 2)   # percent of ONE core
}

function Mem($p) {
    $p.Refresh()
    [pscustomobject]@{ WS = [math]::Round($p.WorkingSet64/1MB,1); Private = [math]::Round($p.PrivateMemorySize64/1MB,1); Handles = $p.HandleCount;
        GDI = [Pf]::GetGuiResources($p.Handle, 0); USER = [Pf]::GetGuiResources($p.Handle, 1); Threads = $p.Threads.Count }
}

function New-Items($n) {
    $files = Get-ChildItem C:\Windows\System32 -Filter *.exe -ErrorAction SilentlyContinue | Select-Object -First ([Math]::Max($n, 1)) -ExpandProperty FullName
    $dirs = Get-ChildItem C:\Windows -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
    $all = @($files) + @($dirs)
    1..$n | ForEach-Object { @{ path = $all[($_ - 1) % $all.Count] } }
}

function New-Cluster($name, $x, $y, $items, $over) {
    $c = [ordered]@{ name = $name; snap = $false; freeX = $x; freeY = $y; items = @(New-Items $items) }
    foreach ($k in $over.Keys) { $c[$k] = $over[$k] }
    $c
}

function Run-Scenario($name, $clusters, [scriptblock]$steps) {
    $root = Join-Path $env:TEMP "cwperf_$name"; Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$root\clusters" | Out-Null
    $i = 1; foreach ($c in $clusters) { $c | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 "$root\clusters\cluster-$i.json"; $i++ }
    $log = Join-Path $root 'perf.log'
    $env:CLUSTERWIDGET_DIR = $root; $env:CLUSTERWIDGET_PERF = $log
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $Exe -PassThru
    try {
        while ((Test-Path $log) -eq $false -and $sw.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 100 }
        Start-Sleep -Seconds 2
        $startupMs = [math]::Round($sw.Elapsed.TotalMilliseconds - 2000)
        $row = [ordered]@{ Scenario = $name; Clusters = @($clusters).Count; Items = ($clusters | ForEach-Object { $_.items.Count } | Measure-Object -Sum).Sum; StartupMs = $startupMs }
        & $steps $p $row $clusters
        $results.Add([pscustomobject]$row)
        Get-Content $log -ErrorAction SilentlyContinue | ForEach-Object { "  [$name] $_" }
    }
    finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Remove-Item Env:\CLUSTERWIDGET_DIR, Env:\CLUSTERWIDGET_PERF -ErrorAction SilentlyContinue
    }
}

# ---- common measurement steps: closed idle, then click-expand, expanded idle, hover movement, collapse
$standard = {
    param($p, $row, $clusters)
    $m = Mem $p; $row.IdleCpuPct = Cpu $p 8 $null
    $m = Mem $p; $row.ClosedWS_MB = $m.WS; $row.ClosedPrivate_MB = $m.Private; $row.GDI = $m.GDI; $row.USER = $m.USER; $row.Handles = $m.Handles
    $c = $clusters[0]; $x = [int]$c.freeX + 90; $y = [int]$c.freeY + 8   # top edge, middle: inside the rounded shape (clicks pass through transparent corners) and in the padding (hits no icon)
    $sh = New-Object -ComObject Shell.Application
    try {
        $sh.MinimizeAll(); Start-Sleep -Milliseconds 1200       # make the desktop visible so the widget can be clicked
        Click $x $y; Start-Sleep -Milliseconds 1500             # expand
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen; $bm = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        [System.Drawing.Graphics]::FromImage($bm).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
        $bm.Clone([System.Drawing.Rectangle]::new(0, 0, 1000, 800), $bm.PixelFormat).Save("$env:TEMP\cwperf_last_expanded.png"); $bm.Dispose()
        $row.ExpandedIdleCpuPct = Cpu $p 6 $null
        $m = Mem $p; $row.ExpandedWS_MB = $m.WS; $row.ExpandedPrivate_MB = $m.Private
        $row.HoverCpuPct = Cpu $p 0 { for ($k = 0; $k -lt 300; $k++) { [Pf]::SetCursorPos($x + 40 + ($k * 7) % 500, $y + 40 + ($k * 5) % 400); Start-Sleep -Milliseconds 15 } }
        Esc; Start-Sleep -Milliseconds 1200                     # collapse (Esc: a click-away could land on an icon of a screen-sized cluster and launch it)
        Start-Sleep -Seconds 2
        $m = Mem $p; $row.AfterCollapseWS_MB = $m.WS; $row.AfterCollapsePrivate_MB = $m.Private
        # desktop shown with Win+D (the widget stays visible) and back
        $row.WinDCpuPct = Cpu $p 0 { WinD; Start-Sleep -Seconds 4; WinD; Start-Sleep -Seconds 1 }
    }
    finally { $sh.UndoMinimizeALL() }
}

$base = @{ closedCount = 4; closedPadding = 18; closedGap = 12 }

foreach ($s in $Scenario) {
    switch ($s) {
        'baseline' {
            Run-Scenario $s @((New-Cluster 'Baseline' 300 200 9 $base)) $standard
        }
        'many-icons' {
            Run-Scenario $s @((New-Cluster 'Many' 100 100 400 $base)) $standard
        }
        'huge-icons' {
            # 3x icon size, 120 items: the expanded window grows to the whole work area
            $o = @{ iconScale = 3.0; closedCount = 4; closedPadding = 18; closedGap = 12; openGap = 8 }
            Run-Scenario $s @((New-Cluster 'Huge' 100 100 120 $o)) $standard
        }
        'blur-max' {
            $o = @{ blur = 100; opacity = 80; closedCount = 4 }
            Run-Scenario $s @((New-Cluster 'Blur' 300 200 30 $o)) $standard
        }
        'six-clusters' {
            $cl = 1..6 | ForEach-Object { New-Cluster "C$_" (100 + ($_ - 1) * 270) 120 12 $base }
            Run-Scenario $s $cl $standard
        }
        'many-clusters-heavy' {
            $o = @{ closedCount = 9; blur = 60 }
            $cl = 1..8 | ForEach-Object { New-Cluster "H$_" (60 + (($_ - 1) % 4) * 330) (60 + [math]::Floor(($_ - 1) / 4) * 330) 150 $o }
            Run-Scenario $s $cl $standard
        }
    }
}


# ---- extra step blocks
$cycleSteps = {
    param($p, $row, $clusters)
    $m0 = Mem $p; $row.Before_Private_MB = $m0.Private; $row.Before_GDI = $m0.GDI; $row.Before_Handles = $m0.Handles
    $c = $clusters[0]; $x = [int]$c.freeX + 90; $y = [int]$c.freeY + 8
    $sh = New-Object -ComObject Shell.Application
    try {
        $sh.MinimizeAll(); Start-Sleep -Milliseconds 1200
        $sw = [Diagnostics.Stopwatch]::StartNew()
        foreach ($round in 1..3) {
            1..40 | ForEach-Object { Click $x $y; Start-Sleep -Milliseconds 350; Esc; Start-Sleep -Milliseconds 350 }
            Start-Sleep -Seconds 1; $m = Mem $p
            $row["Round${round}_Private_MB"] = $m.Private; $row["Round${round}_GDI"] = $m.GDI; $row["Round${round}_USER"] = $m.USER; $row["Round${round}_Handles"] = $m.Handles
        }
        $row.CycleSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    } finally { $sh.UndoMinimizeALL() }
    Start-Sleep -Seconds 2
    $m1 = Mem $p; $row.After40_Private_MB = $m1.Private; $row.After40_GDI = $m1.GDI; $row.After40_Handles = $m1.Handles; $row.After40_USER = $m1.USER
}

$dragSteps = {
    param($p, $row, $clusters)
    $c = $clusters[0]; $x = [int]$c.freeX + 90; $y = [int]$c.freeY + 8
    $sh = New-Object -ComObject Shell.Application
    try {
        $sh.MinimizeAll(); Start-Sleep -Milliseconds 1200
        [Pf]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 300; [Pf]::mouse_event(2,0,0,0,0)
        $row.DragCpuPct = Cpu $p 0 { for ($k = 1; $k -le 240; $k++) { [Pf]::SetCursorPos($x + $k * 4, $y + [int]($k * 2.5)); Start-Sleep -Milliseconds 8 } }
        [Pf]::mouse_event(4,0,0,0,0); Start-Sleep -Milliseconds 800
    } finally { $sh.UndoMinimizeALL() }
    $m = Mem $p; $row.Private_MB = $m.Private; $row.GDI = $m.GDI
}

foreach ($s in $Scenario) {
    switch ($s) {
        'image-bg' {
            $img = Join-Path $env:TEMP 'cwperf_bg.png'
            if (-not (Test-Path $img)) {
                $bmp = New-Object System.Drawing.Bitmap 4000, 3000; $g = [System.Drawing.Graphics]::FromImage($bmp)
                $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)), ([System.Drawing.Point]::new(4000,3000)), ([System.Drawing.Color]::Orange), ([System.Drawing.Color]::RoyalBlue)
                $g.FillRectangle($br, 0, 0, 4000, 3000); $bmp.Save($img); $g.Dispose(); $bmp.Dispose()
            }
            $o = @{ backgroundImage = $img; imageZoom = 150; imageOverlayOpacity = 30; closedCount = 4 }
            Run-Scenario $s @((New-Cluster 'Image' 300 200 120 $o)) $standard
        }
        'drag' {
            Run-Scenario $s @((New-Cluster 'Drag' 300 200 12 $base)) $dragSteps
        }
        'cycle-leak' {
            Run-Scenario $s @((New-Cluster 'Cycle' 300 200 40 $base)) $cycleSteps
        }
    }
}
$results | Format-Table -AutoSize -Wrap | Out-String -Width 260
foreach ($r in $results) { $r | ConvertTo-Json | Set-Content -Encoding utf8 "$env:TEMP\cwperf_$($r.Scenario).json" }
