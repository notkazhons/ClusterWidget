# Configuration reference

Each cluster is one JSON file in `%APPDATA%\ClusterWidget\clusters`. The app writes them when you press **Apply changes**
and reloads them when they change on disk, so you can edit them by hand while it runs. Unknown or invalid values fall
back to safe ones; a file that is not valid JSON is ignored and left untouched.

Colors are `#AARRGGBB` or `#RRGGBB`. Pixel values are at 100% display scaling and are scaled for your DPI.

## Position and behavior
| key | default | meaning |
|---|---|---|
| `name` | `Cluster N` | shown in the settings window and tray menu |
| `snap` | `true` | snap the box's top-left corner to the desktop icon grid |
| `x`, `y` | | top-left desktop cell when snapping |
| `freeX`, `freeY` | | top-left screen pixel when not snapping |
| `expandFrom` | `center` | `center` or `topLeft` |
| `clickLaunches` | `false` | with more icons than fit closed: a click on a normal icon opens the app (only the mini preview opens the cluster) |
| `clusterPreview` | `true` | the last closed slot is a mini 2x2 preview of the remaining icons |

## Layout
| key | default | meaning |
|---|---|---|
| `closedCount` | `4` | icons shown closed (1-16), used when the grid is automatic |
| `boxCols`, `boxRows` | `0` | icons per row / column when closed; `0` = automatic |
| `iconScale` | `1.0` | multiplier on your Windows desktop icon size |
| `closedPadding`, `closedGap` | `18`, `12` | box edge to icons / between icons, closed |
| `openPadding`, `openGap` | `14`, `8` | the same for the expanded view |
| `gap` | `4` | space kept between the box and its desktop grid cell when snapping |
| `labelSize` | `12` | size of names in the expanded view, pixels |
| `showLabels` | `true` | show names in the expanded view |
| `animationMs` | `160` | expand / collapse time, `0` = instant |

## Look
| key | default | meaning |
|---|---|---|
| `blur` | `24` | blur of the wallpaper behind the box, 0-100 px |
| `opacity` | `100` | opacity of the blur and tint, 0-100 |
| `tint` | `#8C14181C` | color over the blur (its alpha is the tint strength) |
| `outlineColor`, `outlineOpacity`, `outlineWidth` | `#FF4CC2FF`, `100`, `2` | outline |
| `cornerRadius` | `40` | corner radius in pixels |
| `backgroundImage` | empty | path of a picture drawn over the tint |
| `imageOpacity` | `100` | picture opacity |
| `imageOverlayColor`, `imageOverlayOpacity` | `#FFFFFFFF`, `0` | a color painted over the picture |
| `imageZoom` | `100` | 100-400 |
| `imagePanX`, `imagePanY` | `50` | which part of a zoomed picture shows, 0-100 |
| `labelColor`, `hoverColor`, `fontFamily` | | text color, hover highlight, font |

## Settings icon
| key | default | meaning |
|---|---|---|
| `gearCorner` | `auto` | `auto`, `topRight`, `topLeft`, `bottomRight`, `bottomLeft` |
| `gearSize` | `0` | pixels, `0` = automatic |
| `gearOffsetX`, `gearOffsetY` | `-1` | distance from the box edge, `-1` = automatic |
| `gearAlways` | `false` | show the settings icon all the time instead of on hover |

## Items
```json
"items": [
  { "path": "C:\\Games\\Game.exe" },
  { "path": "C:\\Users\\me\\Documents", "name": "My documents" },
  { "path": "shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }
]
```
A path can be a program, file, folder, `.lnk` / `.url` shortcut or a `shell:` address. `name` is optional.

## Designs and the default look
`designs\*.json` use the same keys (without items). `default-design.json` in the app folder is the look new clusters
start with and *Reset* returns to; delete it to go back to the built-in look.