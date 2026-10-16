# ClusterWidget

A tiny Windows 11 desktop widget: a 4x4-cell blurred acrylic square that shows its first 4 shortcuts
(2x2) and expands on click to reveal the rest. Built as a .NET Native AOT Win32 app, event-driven,
no timers while idle (~2 MB exe, ~0.3 MB working set at rest).

## Behavior
- Snaps to the **desktop icon grid** (cell size read from the shell). Drag it to move; it snaps on every cell.
- Click: expands by only as many whole cells as the items need, split evenly on each axis and
  shifted back inside the work area (right edge grows left, top-left corner grows right/down).
- With 4 or fewer items, clicking an icon launches it directly. Esc, clicking elsewhere, or launching collapses it.
- Drop files, folders or shortcuts on it to add them. Right-click: reload / open config / remove item / exit.

## Config
`%APPDATA%\ClusterWidget\config.json` (hot reloaded). Colors are `#AARRGGBB` or `#RRGGBB`.

| key | meaning |
|---|---|
| `x`, `y` | top-left grid cell |
| `tileCells` | cells per tile side (2 gives the 4x4 footprint) |
| `backdrop` | `acrylic` (true acrylic, system 8px corners) or `blur` (lighter blur, honors `cornerRadius`) |
| `tint`, `outlineColor`, `outlineWidth`, `cornerRadius`, `gap` | look |
| `iconRatio`, `showLabels`, `labelColor`, `labelSize`, `fontFamily`, `hoverColor` | tiles |
| `animationMs` | expand/collapse time (0 = instant) |
| `items` | `[{ "path": "...", "name": "optional" }]` — files, folders, `.lnk`, `shell:AppsFolder\...` |

## Build
Needs the .NET 10 SDK and the Visual Studio C++ build tools (Native AOT links with MSVC).

```powershell
.\tools\dev.ps1            # publish to .\publish, relaunch
.\tools\dev.ps1 -Shot a.png  # also screenshot the desktop (briefly minimizes windows)
dotnet test tests\ClusterWidget.Tests
```

## Known limits
- Windows 11 has no desktop-widget API; this is a bottom-of-z-order window, so Win+D hides it.
- `acrylic` mode has fixed 8px corners (DWM clips the blur); use `backdrop: "blur"` for a custom radius.
- Items must fit on screen; extra items beyond the work area's tile capacity are not shown.
