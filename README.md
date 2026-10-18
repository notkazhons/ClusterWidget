<p align="center">
  <img src="assets/icon.png" width="96" alt="ClusterWidget icon">
</p>

<h1 align="center">ClusterWidget</h1>

<p align="center">
  Group your desktop shortcuts into small blurred, fully customizable boxes that open on click.<br>
  A tiny native Windows 11 app: 0% CPU while idle, about 2 MB, no installer.
</p>

<p align="center">
  <a href="../../releases/latest"><b>Download</b></a> &middot;
  <a href="https://notkazhons.github.io/ClusterWidget/"><b>Website</b></a> &middot;
  <a href="docs/CONFIG.md"><b>Config reference</b></a> &middot;
  <a href="CHANGELOG.md"><b>Changelog</b></a>
</p>

<p align="center">
  <img src="docs/screenshots/mine-games.png" height="210" alt="A closed cluster">
  <img src="docs/screenshots/mine-games-open.png" height="210" alt="The same cluster expanded">
  <img src="docs/screenshots/mine-art.png" height="210" alt="Another cluster with the same look">
</p>

<p align="center"><img src="docs/demo.gif" width="560" alt="Clicking a cluster opens it, clicking away closes it"></p>

## Why

Desktops fill up with shortcuts. A **cluster** is a small box that shows a few of them (like a phone folder) and opens
to show the rest. It sits on your desktop like part of the wallpaper, stays visible when you press Win+D, and costs
nothing while you are not using it.

- **Looks like it belongs:** blurred wallpaper, tint, outline and corners you control, or your own picture.
- **Light:** a Native AOT executable of about 2 MB. No timers run while idle (measured 0% CPU), roughly 11-14 MB of
  memory per cluster.
- **As many clusters as you like**, each with its own icons, position and look.
- **Saved designs:** keep a look under a name and apply it to any cluster.

## Install

1. Download `ClusterWidget-<version>-win-x64.zip` from the [Releases](../../releases) page.
2. Unzip it anywhere (for example `C:\Tools\ClusterWidget`) and run `ClusterWidget.exe`.
3. Drop shortcuts, folders or programs onto the box that appears. Right-click it for settings.

Windows 11 (x64). The app is not code-signed, so Windows SmartScreen may warn the first time: choose *More info*, then *Run anyway*.
To start it with Windows, turn on *Start with Windows* (Settings, Behavior tab). To remove it, delete the folder and
`%APPDATA%\ClusterWidget`.

### If Windows won't let you run it

The app is unsigned, and a freshly downloaded unsigned program is often blocked. Windows says things like *"Windows cannot
access the specified device, path, or file. You may not have the appropriate permissions"*, or SmartScreen / Defender
removes the file. Try these in order:

1. **Unblock the zip before unzipping.** Right-click the downloaded `.zip`, choose *Properties*, tick **Unblock** at the
   bottom, press OK, and only then extract it. (Or in PowerShell: `Unblock-File .\ClusterWidget-1.0.0-win-x64.zip`.)
2. **Extract it first.** Do not run it from inside the zip. Unzip to a normal folder such as `C:\Tools\ClusterWidget` or
   your Documents (not Program Files, not a network drive).
3. **Windows Security may have removed it.** Open *Windows Security, Virus & threat protection, Protection history*. If
   `ClusterWidget.exe` is listed, choose *Restore* and *Allow on device*, or add its folder under *Exclusions*.
4. **SmartScreen:** if you see "Windows protected your PC", choose *More info*, then *Run anyway*.
5. **Smart App Control** (Windows 11, Settings, Privacy & security, Windows Security, App & browser control) blocks
   unsigned programs while it is **On**. Switch it to Off, or build the app yourself (see below).

You can verify the download with the `.sha256` file on the release page: `Get-FileHash .\ClusterWidget-1.0.0-win-x64.zip`
should print the same value. The source is all here, and building it yourself gives you an exe your own machine trusts.

## Using it

| You want to | Do this |
|---|---|
| Open a cluster | Click it (Esc or click elsewhere closes it) |
| Add an icon | Drop a file, folder, program or shortcut on it |
| Move it | Drag the box. It snaps to your desktop icon grid unless you turn that off |
| Re-order icons | Open it and drag an icon onto another slot, or use A to Z in settings |
| Open settings | Hover the cluster and click the small gear, or right-click, or use the tray icon |
| Remove an icon | Right-click the icon |
| Use only the keyboard | With the desktop in front press **Tab**: it steps into the clusters (one that can open opens itself). **Tab** / **Shift+Tab** or the arrow keys move between icons, **Enter** opens the focused app, **Esc** goes back to the desktop. Past the last icon it carries on to the next cluster and then the taskbar |
| Add another cluster | Right-click, *New cluster* (it appears in the middle of the screen) |

Hover any icon for its name and location, and hover any setting for an explanation.

## Customizing

The settings window changes things live. **Apply changes** keeps them, **Cancel** (or Esc) undoes everything since your
last Apply.

<p align="center"><img src="docs/screenshots/settings.png" width="460" alt="The settings window"></p>

- **Look:** blur, opacity, tint, outline (color, opacity, thickness) and corner roundness.
- **Picture:** a background picture with opacity, a second *overlay* color over it, zoom and position.
- **Icons:** an automatic grid or an exact *icons per row x column*, mini preview or plain last icon, whether a click
  opens the app or the cluster, sorting.
- **Size and spacing:** icon size, name size, padding (box edge to icons) and spacing (between icons), separately for
  the closed and expanded view, plus presets.
- **Behavior:** snap to grid, expand from the center or the top-left corner, settings icon position and size, start
  with Windows, *Use this look as my default*.
- **About:** how everything works, built into the app.

A **design** is a saved look and layout (not icons or position). Save one with *Save as...* at the top and apply it to
any cluster. Two designs ship with the app, *Black & Pink* and *Frosted White* (a thin white outline over a low-opacity blur); they
appear first in the design list and can't be overwritten or deleted. *Use this look as my default* makes it the starting look of new clusters and what *Reset* returns to.

## Your files

Everything lives in `%APPDATA%\ClusterWidget` and is plain JSON you can edit while the app runs:

```
clusters\*.json      one file per cluster (icons, position, look)
designs\*.json       saved designs
shortcuts\           copies of .lnk / .url files you dropped, so deleting the original is safe
pictures\            copies of background pictures you chose
default-design.json  your default look (optional)
```

Colors are `#AARRGGBB` or `#RRGGBB`. See [docs/CONFIG.md](docs/CONFIG.md) for every key.

## How it works (short version)

- Each cluster is a layered window drawn with GDI+ and owned by the desktop window, which is why it stays put
  when you show the desktop with Win+D.
- The wallpaper does not change while a cluster sits on it, so the blur is computed once and reused. That makes blur
  strength, corner radius and opacity free sliders instead of a fixed system effect, and keeps idle cost at zero.
- Icons come from the same shell image list the desktop uses, at your Windows desktop icon size.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Visual Studio C++ build tools
("Desktop development with C++"), because Native AOT links with the MSVC linker.

```powershell
dotnet test tests\ClusterWidget.Tests     # layout unit tests
.\tools\dev.ps1                           # publish to .\publish and start it
.\tools\package.ps1                       # build the release zip in .\dist
```

`tools\perf.ps1` runs isolated copies through stress scenarios (hundreds of icons, many clusters, huge icons, pictures)
and reports CPU, memory and frame times. Set `CLUSTERWIDGET_DIR=<folder>` to run a copy with its own settings, and
`CLUSTERWIDGET_PERF=<file>` to log timings.

## Known limits

- Windows 11 has no desktop-widget API, so a cluster is a window that lives just above the desktop. It cannot blur
  other windows, only the wallpaper.
- The process is system-DPI-aware like the desktop itself, so a monitor with a different scale than the primary one is
  bitmap-scaled.
- A cluster shows as many icons as fit on the screen when expanded.
- A wallpaper slideshow is picked up the next time Windows reports a display or setting change.
- If a program is moved or deleted, its icon becomes an empty square: remove it or drop it again.

## Contributing

Bug reports and pull requests are welcome, see [CONTRIBUTING.md](CONTRIBUTING.md). Release notes are in
[CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE)
