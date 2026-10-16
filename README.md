# ClusterWidget

A tiny Windows 11 desktop widget: a rounded, blurred box that shows its first 4 shortcuts (2x2) and expands on
click to reveal the rest. Think phone-style folders. Built as a .NET Native AOT Win32 app: event-driven, no timers
while idle, ~2 MB exe.

## Behavior
- **Icons are the same size as your desktop icons** (read from Explorer's icon-size setting, DPI scaled); the
  *Icon size* slider multiplies that.
- Snaps to the **desktop icon grid** (cell size read from the shell). Drag it to move; it snaps on every cell.
- With more than 4 items, the 4th slot becomes a mini 2x2 preview of the rest. Click: it expands by only as many
  whole cells as needed, split evenly on each axis and shifted back inside the work area (right edge grows left,
  top-left corner grows right and down). Esc or clicking elsewhere collapses it.
- With 4 or fewer items, clicking an icon launches it directly.
- It survives **Win+D**: the window is owned by the desktop, so it is raised together with it.
- Drop files, folders or shortcuts on it to add them. Right-click: settings / reload / open config / remove item / exit.
- The small **gear** in the corner opens the settings window (live preview): blur, opacity, tint strength and color,
  outline color and thickness, corner roundness, icon size, names on/off.

## How the look works
The wallpaper never changes while the widget sits on the desktop, so the blur is computed once (reduced
resolution, box blur) and reused every frame, instead of DWM re-blurring live. That is why blur, opacity and corner
radius are all free sliders, and why nothing runs while idle. Only the wallpaper is blurred; with *Opacity* below
100% you will see the real desktop icons through the box.

## Config
`%APPDATA%\ClusterWidget\config.json` (hot reloaded; the settings window writes it too). Colors are `#AARRGGBB` or `#RRGGBB`.

| key | meaning |
|---|---|
| `x`, `y` | top-left grid cell |
| `boxRows` | cell rows of the collapsed box (width follows the cell shape) |
| `blur`, `opacity`, `tint`, `outlineColor`, `outlineWidth`, `cornerRadius`, `gap` | look |
| `iconScale`, `showLabels`, `labelColor`, `labelSize`, `fontFamily`, `hoverColor` | tiles |
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
