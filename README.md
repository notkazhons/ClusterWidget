# ClusterWidget

A tiny Windows 11 desktop widget: a rounded, blurred box that shows its first 4 shortcuts (2x2) and expands on
click to reveal the rest. Think phone-style folders. Built as a .NET Native AOT Win32 app: event-driven, no timers
while idle, ~2 MB exe.

## Behavior
- **Icons are the same size as your desktop icons** (read from Explorer's icon-size setting, DPI scaled); *Icon size*
  and *Cluster size* scale from there. The box always fits its contents, so icons never overflow or float in empty space.
- **Closed:** shows 1-16 icons (your choice). If there are more items, the last slot becomes a mini 2x2 preview of the rest.
- **Size:** *Width/Height (grid cells)* give the closed box a fixed size in desktop cells (for example 2x6); 0 = auto,
  where the box fits its icons exactly so the padding and spacing sliders are literal. In a fixed box the icons keep
  their size and are arranged to suit its shape; they only shrink if they cannot all fit.
- **Snap to the desktop grid** (on by default): the box's top-left corner sits on a desktop cell. Turn it off to place
  the box anywhere, pixel by pixel.
- **Expanded:** click it. It grows only as much as the items need, evenly on each axis, shifted back inside the work
  area (right edge grows left, top-left corner grows right and down). Esc or clicking elsewhere closes it.
- With no more items than the closed count, clicking an icon launches it directly.
- Padding (box edge to icons) and spacing (between icons) are separate sliders for closed and expanded, plus
  Compact / Cozy / Roomy presets for the closed box (padding never smaller than spacing).
- It survives **Win+D**: the window is owned by the desktop, so it is raised together with it.
- Drop files, folders or shortcuts on it to add them. Right-click: settings / reload / open config / remove item / exit.
- The **gear** appears only while the pointer is over the cluster and opens the settings window (live preview). Its
  position is automatic by default (scales with the icons and picks a corner where it overlaps nothing) or a corner
  of your choice, with its own size. The same window opens from the **tray icon** (hidden-icons flyout, no taskbar button).
- Settings also has: outline opacity, *Stay open when clicking away* (keeps the expanded cluster open while you edit;
  click empty space in it to close), *Start with Windows* (per-user Run entry for the exe's current path), and *Reset
  all* (asks first).

## How the look works
The wallpaper never changes while the widget sits on the desktop, so the blur is computed once (reduced
resolution, box blur) and reused every frame, instead of DWM re-blurring live. That is why blur, opacity and corner
radius are all free sliders, and why nothing runs while idle. Only the wallpaper is blurred; with *Opacity* below
100% you will see the real desktop icons through the box.

## Config
`%APPDATA%\ClusterWidget\config.json` (hot reloaded; the settings window writes it too). Colors are `#AARRGGBB` or `#RRGGBB`.

| key | meaning |
|---|---|
| `snap`, `x`, `y`, `freeX`, `freeY` | grid snapping, top-left cell (snapped) or pixel (free) |
| `closedCount`, `boxCols`, `boxRows`, `iconScale` | icons shown closed, closed size in cells (0 = auto), icon size |
| `keepOpen`, `gearCorner`, `gearSize`, `outlineOpacity` | stay open, settings icon (auto/topRight/topLeft/bottomRight/bottomLeft, px), outline alpha |
| `closedPadding`, `closedGap`, `openPadding`, `openGap` | spacing (px at 100%) |
| `blur`, `opacity`, `tint`, `outlineColor`, `outlineWidth`, `cornerRadius`, `gap` | look |
| `showLabels`, `labelColor`, `labelSize`, `fontFamily`, `hoverColor` | labels |
| `animationMs` | expand/collapse time (0 = instant) |
| `items` | `[{ "path": "...", "name": "optional" }]`: files, folders, `.lnk`, `shell:AppsFolder\...` |

## Build
Needs the .NET 10 SDK and the Visual Studio C++ build tools (Native AOT links with MSVC).

```powershell
.\tools\dev.ps1              # publish to .\publish, relaunch
.\tools\dev.ps1 -Shot a.png  # also screenshot the desktop (briefly minimizes windows)
dotnet test tests\ClusterWidget.Tests
```

## Known limits
- The process is system-DPI-aware (like the desktop itself), so a monitor with a different scale than the primary is bitmap-scaled.
- A window drawn from the wallpaper cannot blur other windows; it is a desktop widget.
- Items must fit on screen; extra items beyond the work area's tile capacity are not shown.
- A wallpaper slideshow/Spotlight change is picked up on the next settings/display change event.
