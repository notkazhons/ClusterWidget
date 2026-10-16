# Keyboard-navigation check on an isolated copy: two clusters, Tab from the desktop into them, Enter to open an app.
param([string]$Exe = "$env:TEMP\cwbuild\ClusterWidget.exe", [string]$Scenario = "basic")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Kb {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int dx, int dy, int d, int e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, int fl, int ex);
}
"@
[Kb]::SetProcessDPIAware() | Out-Null
$root = Join-Path $env:TEMP 'cwkb'; Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$root\clusters" | Out-Null
$many = 'notepad','calc','mspaint','cmd','charmap','mstsc','taskmgr','msinfo32','control','Magnify','osk','SnippingTool' | ForEach-Object { @{ path = "C:\Windows\System32\$_.exe" } }
$few = 'hostname','whoami','ipconfig' | ForEach-Object { @{ path = "C:\Windows\System32\$_.exe" } }
@{ name = 'A'; snap = $false; freeX = 400; freeY = 300; closedCount = 4; items = $many } | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$root\clusters\cluster-1.json"
@{ name = 'B'; snap = $false; freeX = 1000; freeY = 300; closedCount = 6; items = $few } | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 "$root\clusters\cluster-2.json"
$log = "$root\perf.log"
$env:CLUSTERWIDGET_DIR = $root; $env:CLUSTERWIDGET_PERF = $log
$before = @(Get-Process | ForEach-Object { $_.Id })   # to close only what this test launched, never your own programs
$proc = Start-Process $Exe -PassThru
function Key($vk, [bool]$shift = $false) {
    if ($shift) { [Kb]::keybd_event(0x10, 0, 0, 0) }
    [Kb]::keybd_event($vk, 0, 0, 0); [Kb]::keybd_event($vk, 0, 2, 0)
    if ($shift) { [Kb]::keybd_event(0x10, 0, 2, 0) }
    Start-Sleep -Milliseconds 700
}
function Shot($name) {
    $b = [System.Windows.Forms.SystemInformation]::VirtualScreen; $bm = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    [System.Drawing.Graphics]::FromImage($bm).CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bm.Clone([System.Drawing.Rectangle]::new(300, 200, 1100, 560), $bm.PixelFormat).Save("$env:TEMP\kb_$name.png"); $bm.Dispose()
}
$sh = New-Object -ComObject Shell.Application
try {
    Start-Sleep -Seconds 2
    $sh.MinimizeAll(); Start-Sleep -Milliseconds 1200
    [Kb]::SetCursorPos(900, 900) | Out-Null; [Kb]::mouse_event(2,0,0,0,0); [Kb]::mouse_event(4,0,0,0,0); Start-Sleep -Milliseconds 700   # click empty desktop: it is now in front
    if ($Scenario -eq 'basic') {
        Key 0x09;          Shot '1_tab_into_A'        # Tab: cluster A opens, focus on its first icon
        Key 0x09; Key 0x09; Shot '2_third_icon'       # two more Tabs
        Key 0x27;          Shot '3_arrow_right'       # arrow right
        Key 0x0D;          Start-Sleep -Milliseconds 500; Shot '4_after_enter'   # Enter: launches the focused icon, cluster closes
    } else {
        Key 0x09                                       # into A (icon 0)
        1..11 | ForEach-Object { Key 0x09 }            # through A's 12 icons (now on the last)
        Key 0x09;          Shot '5_in_B'               # past the last icon: A closes, B takes the focus
        Key 0x09; Key 0x09; Key 0x09                   # through B's 3 icons and out the end: back to the desktop, on to the taskbar
        Shot '6_after_B'
    }    "--- launches so far:"; Get-Content $log | Where-Object { $_ -match 'launch|keyboard' }
}
finally {
    $sh.UndoMinimizeALL()
    Get-Process notepad,calc,CalculatorApp,mspaint,charmap,mstsc,taskmgr,msinfo32,control,Magnify,osk,SnippingTool -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    Remove-Item Env:\CLUSTERWIDGET_DIR, Env:\CLUSTERWIDGET_PERF -ErrorAction SilentlyContinue
}
