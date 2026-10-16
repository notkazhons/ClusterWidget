# Changelog

All notable changes to this project are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/),
and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Keyboard navigation: Tab from the desktop enters the clusters, Tab / Shift+Tab / arrows move between icons, Enter opens the focused app, Esc returns to the desktop. A keyboard hook is only installed while the desktop is in front.

## [1.0.0] - 2026-10-16

First public release.

### Added
- Clusters: blurred, rounded desktop boxes that show a few shortcuts and open on click. Any number of clusters, each with
  its own icons, position, look and JSON file.
- Closed layout: automatic grid or exact icons per row x column, mini preview of the remaining icons (optional), padding
  and spacing that are exact (the box is the icon grid plus padding).
- Expanded view that grows only as much as needed, from the center or the top-left corner, with re-ordering by drag and
  A to Z / Z to A / reverse.
- Look: blur, opacity, tint color and strength, outline color / opacity / thickness, corner roundness, background picture
  with opacity, overlay color, zoom and position.
- Snap to the desktop icon grid or free placement. Stays visible with Win+D.
- Settings window with tabs, live preview, Apply / Cancel, hover help for every control and a built-in About page.
- Saved designs, a user default look, start with Windows, tray icon.
- Dropped shortcuts and chosen pictures are copied into the app folder so deleting the originals is safe.
- Performance tooling: 	ools/perf.ps1, 	ools/ui.ps1, optional timing log.
