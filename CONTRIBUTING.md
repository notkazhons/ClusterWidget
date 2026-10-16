# Contributing

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting a bug
Open an issue and include: Windows version, display scaling, what you did, what you expected and what happened. A
screenshot helps. If a cluster looks wrong, attach its file from `%APPDATA%\ClusterWidget\clusters` (remove any paths
you do not want to share).

## Building
See the README. In short: .NET 10 SDK + Visual Studio C++ build tools, then `dotnet test tests\ClusterWidget.Tests` and
`.\tools\dev.ps1`.

## Code layout
| File | What it does |
|---|---|
| `App.cs` | owns all clusters, the tray icon and the message loop |
| `Widget.cs` | one cluster: layout, drawing, input, animation |
| `SettingsWindow.cs` | the settings window |
| `Config.cs` | settings model, designs, default look |
| `Layout.cs` | pure grid math (unit tested) |
| `Backdrop.cs` / `BackgroundImage.cs` | blurred wallpaper and picture backgrounds |
| `Icons.cs` | shell icon extraction |
| `Native*.cs` | Win32 / GDI+ declarations |

## Guidelines
- Keep the idle cost at zero: no timers or threads that run while nobody is using a cluster.
- Anything that changes sizes or positions belongs in `Layout.cs` where it can be unit tested.
- Check UI changes with `tools\ui.ps1` (it runs an isolated copy) and look at a screenshot before opening the pull request.
- Run `tools\perf.ps1` when you touch drawing code and mention any change in the numbers.
- Match the surrounding code style. Prefer explaining *why* in comments.
