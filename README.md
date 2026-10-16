# ClusterWidget

A tiny Windows 11 desktop widget: a rounded, blurred box that shows its first 4 shortcuts (2x2) and expands on
click to reveal the rest. Think phone-style folders. Built as a .NET Native AOT Win32 app: event-driven, no timers
while idle, ~2 MB exe.

## Behavior
- **Icons are the same size as your desktop icons** (read from Explorer's icon-size setting, DPI scaled); *Icon size*
  scales from there. The box always fits its contents, so icons never overflow or float in empty space.
- **Several clusters:** every cluster is its own widget with its own icons, position, look and file
  (`%APPDATA%\ClusterWidget\clusters\*.json`). Create, rename and delete them from the settings window, the right-click
  menu or the tray menu. A new cluster starts with the current cluster's design.
- **Closed:** shows 1-16 icons (*Automatic grid*), or an exact *Icons per row* x *Icons per column* grid (for example 5x2).
  The box is always exactly the icon grid plus the padding on every side, so padding and spacing sliders are literal
  and icons never shrink. With more icons than slots, the last slot is either a mini 2x2 preview of the rest or just
  another icon (your choice).
- **Snap to the desktop grid** (on by default): the box's top-left corner sits on a desktop cell. Turn it off to place
  the box anywhere, pixel by pixel.
- **Expanded:** click it. It grows only as much as the items need, evenly on each axis, shifted back inside the work
  area. Esc or clicking elsewhere closes it. Tiles are exactly as wide as icons, so spacing 0 means icons touching,
  like the closed box. *Name size* sets the text under the icons.
- **Re-order:** drag an icon onto another slot in the expanded view; it moves there and the rest shift. *A to Z*, *Z to A* and *Reverse* are on the Icons tab.
- **Expand from:** center (default) or the top-left corner, which stays exactly where it is while the cluster grows right and down.
  Use the same padding and spacing for closed and expanded to keep the icons themselves in place too.
- A new cluster always appears in the middle of the screen.
- With no more items than the closed count, clicking an icon launches it directly.
- It survives **Win+D**: the window is owned by the desktop, so it is raised together with it.
- Drop files, folders or shortcuts on it to add them. Right-click: settings / remove item / new or delete cluster / exit.

## Settings window
Opens from the **gear** (shown while the pointer is over the cluster), the right-click menu or the **tray icon** (hidden-icons
flyout, no taskbar button). Everything previews live; **Apply changes** saves, **Cancel** (or the window's X) puts it back.

- **Top:** cluster picker (Rename / New / Delete) and design picker (*Save as...* / Delete). A design is a named look +
  layout + settings-icon setup that can be applied to any cluster; items and position are not part of it.
- **View toggles:** *Expanded view* holds the cluster open (without covering the settings window) so you can see edits to
  it; *Show settings icon* shows the gear while you place it. Both end when the window closes.
- **Tabs:** Appearance (blur, opacity, tint, outline, corner roundness), Image (picture, opacity, a second **overlay
  color** with its own opacity, zoom, position), Icons, Spacing (padding / spacing closed and expanded, presets),
  Settings icon (corner or automatic, size, distance from the edge, always show), General (snap, start with Windows, reset).

## How the look works
The wallpaper never changes while the cluster sits on the desktop, so the blur is computed once (reduced resolution, box
blur) and reused, instead of DWM re-blurring live. That is why blur, opacity and corner radius are all free sliders, and
why nothing runs while idle. Only the wallpaper is blurred; with *Opacity* below 100% you will see the real desktop icons
through the box. The blurred background plus tint is also cached at full size so a frame copies pixels instead of
re-blending them; only the rounded corners are re-drawn.
## Config
`%APPDATA%\ClusterWidget\clusters\*.json` (hot reloaded; the settings window writes it too). Colors are `#AARRGGBB` or `#RRGGBB`.

| key | meaning |
|---|---|
| `expandFrom` | `center` or `topLeft` |
| `snap`, `x`, `y`, `freeX`, `freeY` | grid snapping, top-left cell (snapped) or pixel (free) |
| `closedCount`, `boxCols`, `boxRows`, `iconScale`, `clusterPreview` | icons shown closed, icons per row / column (0 = auto), icon size, last slot = mini preview |
| `backgroundImage`, `imageOpacity`, `imageOverlayColor`, `imageOverlayOpacity`, `imageZoom`, `imagePanX`, `imagePanY` | box picture, its opacity, overlay color and opacity, zoom %, position % |
| `gearAlways`, `gearCorner`, `gearSize`, `gearOffsetX`/`gearOffsetY` (px, -1 = auto), `outlineOpacity` | stay open, settings icon (auto/topRight/topLeft/bottomRight/bottomLeft, px), outline alpha |
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

## Performance

`tools\perf.ps1` runs isolated copies (own settings folder) through stress scenarios and reports CPU, memory, handles and frame times; `tools\ui.ps1` drives one for manual-style checks. Set `CLUSTERWIDGET_PERF=<file>` to make the app log its own timings.

Typical numbers on a 1920x1200 desktop at 125% scaling: idle CPU 0% (closed, expanded, desktop shown), about 11-14 MB private memory per cluster with a few dozen icons, expand animation 4-6 ms per frame for normal clusters, hover redraw 3-5 ms. A 400-icon cluster filling the screen: 21 ms hover redraw, ~55 MB. Eight clusters with 150 icons each: ~120-130 MB total, hover 14 ms. A background picture on a big cluster is the slowest case (36 ms hover) because the picture is re-drawn each time.
