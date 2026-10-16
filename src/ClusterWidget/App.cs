using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>
/// The application: owns every cluster widget, the tray icon, and the one message loop. Each cluster is a Widget with
/// its own window and its own file in the clusters folder.
/// </summary>
internal static unsafe class App
{
    private const uint WM_TRAY = Native.WM_APP + 3, WM_FILES_CHANGED = Native.WM_APP + 1;
    private const nuint TimerSync = 1;

    public static readonly List<Widget> Widgets = new();

    private static nint _hwnd, _trayIcon;
    private static uint _taskbarCreated;
    private static nuint _gdipToken;
    private static FileSystemWatcher? _watcher;

    public static void Run()
    {
        Gdip.GdiplusStartup(out _gdipToken, new GdiplusStartupInput { GdiplusVersion = 1 }, 0);
        Native.CoInitializeEx(0, 2 /* apartment-threaded */);
        Native.InitCommonControlsEx(new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = 0xC /* BAR | TAB */ });

        CreateAppWindow();
        MigrateAndLoad();
        AddTrayIcon();
        StartWatcher();
        Native.EmptyWorkingSet(Native.GetCurrentProcess());

        while (Native.GetMessage(out MSG m, 0, 0, 0) > 0)
        {
            if (SettingsWindow.HandleMessage(m)) continue; // Tab / Enter / Esc inside the settings window
            Native.TranslateMessage(m); Native.DispatchMessage(m);
        }

        RemoveTrayIcon();
        _watcher?.Dispose();
        foreach (var w in Widgets.ToArray()) w.Destroy();
        Gdip.GdiplusShutdown(_gdipToken);
    }

    public static void Quit() => Native.PostQuitMessage(0);

    // ---------------------------------------------------------------- clusters

    private static void MigrateAndLoad()
    {
        Directory.CreateDirectory(Config.ClustersDir);
        if (!Directory.EnumerateFiles(Config.ClustersDir, "*.json").Any())
        {
            string first = Path.Combine(Config.ClustersDir, "cluster-1.json");
            if (File.Exists(Config.LegacyFile)) File.Copy(Config.LegacyFile, first); // keep the old file untouched
            else { var c0 = Config.NewDefault(); c0.SourcePath = first; c0.Name = "Cluster 1"; c0.Save(); }
        }
        foreach (string file in Directory.GetFiles(Config.ClustersDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            AddWidgetFromFile(file);
        if (Widgets.Count == 0) NewCluster(null);
    }

    private static Widget? AddWidgetFromFile(string file)
    {
        var cfg = Config.Load(file);
        if (cfg == null) return null;
        var w = new Widget(cfg);
        Widgets.Add(w);
        w.Create();
        return w;
    }

    /// <summary>Creates another cluster, next to <paramref name="basis"/> and with the same design but no items.</summary>
    public static Widget NewCluster(Widget? basis)
    {
        Directory.CreateDirectory(Config.ClustersDir);
        int n = 1;
        while (File.Exists(Path.Combine(Config.ClustersDir, $"cluster-{n}.json"))) n++;
        var cfg = Config.NewDefault();
        cfg.SourcePath = Path.Combine(Config.ClustersDir, $"cluster-{n}.json");
        string name; int k = n;
        do { name = $"Cluster {k++}"; } while (Widgets.Any(w => w.Cfg.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        cfg.Name = name;
        if (basis != null)
        {
            cfg.ApplyDesign(basis.Cfg);
            cfg.Snap = basis.Cfg.Snap;
        }
        cfg.Save();
        var w = new Widget(cfg);
        Widgets.Add(w);
        w.Create();
        w.CenterOnScreen(); // a new cluster always appears in the middle of the screen
        return w;
    }

    public static bool DeleteCluster(Widget w)
    {
        if (Widgets.Count <= 1) return false; // the app always keeps one cluster
        Widgets.Remove(w);
        string path = w.Cfg.SourcePath;
        w.Destroy();
        try { File.Delete(path); } catch (Exception) { /* nothing left to clean */ }
        SettingsWindow.TargetRemoved(w);
        return true;
    }

    // ---------------------------------------------------------------- disk changes (hand-edited or externally copied files)

    private static void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(Config.ClustersDir, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            FileSystemEventHandler h = (_, _) => Native.PostMessage(_hwnd, WM_FILES_CHANGED, 0, 0);
            _watcher.Changed += h; _watcher.Created += h; _watcher.Deleted += h;
            _watcher.Renamed += (_, _) => Native.PostMessage(_hwnd, WM_FILES_CHANGED, 0, 0);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { /* hot reload is optional */ }
    }

    private static void SyncFromDisk()
    {
        string[] files = Directory.Exists(Config.ClustersDir) ? Directory.GetFiles(Config.ClustersDir, "*.json") : Array.Empty<string>();
        foreach (var w in Widgets.ToArray())
        {
            string path = w.Cfg.SourcePath;
            if (!files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                if (Widgets.Count > 1) { Widgets.Remove(w); w.Destroy(); SettingsWindow.TargetRemoved(w); }
            }
            else w.ReloadIfChangedOnDisk();
        }
        foreach (string f in files)
            if (!Widgets.Any(w => string.Equals(w.Cfg.SourcePath, f, StringComparison.OrdinalIgnoreCase))) AddWidgetFromFile(f);
        SettingsWindow.Refresh();
    }

    // ---------------------------------------------------------------- app window + tray

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint AppProc(nint h, uint m, nint w, nint l)
    {
        try
        {
            if (m == _taskbarCreated && _taskbarCreated != 0) { AddTrayIcon(); return 0; } // Explorer restarted
            switch (m)
            {
                case WM_TRAY:
                    switch ((uint)(l & 0xFFFF))
                    {
                        case 0x202 /* WM_LBUTTONUP */: if (Widgets.Count > 0) SettingsWindow.Show(SettingsWindow.Target ?? Widgets[0]); break;
                        case 0x205 /* WM_RBUTTONUP */: TrayMenu(); break;
                    }
                    return 0;
                case WM_FILES_CHANGED:
                    Native.SetTimer(h, TimerSync, 300, 0); // debounce: editors write in bursts
                    return 0;
                case Native.WM_TIMER:
                    if ((nuint)w == TimerSync) { Native.KillTimer(h, TimerSync); SyncFromDisk(); }
                    return 0;
            }
        }
        catch (Exception) { /* never let an exception cross into native code */ }
        return Native.DefWindowProc(h, m, w, l);
    }

    private static void CreateAppWindow()
    {
        nint inst = Native.GetModuleHandle(0);
        fixed (char* cls = "ClusterWidgetApp")
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)sizeof(WNDCLASSEX),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&AppProc,
                hInstance = inst, lpszClassName = cls,
            };
            Native.RegisterClassEx(ref wc);
        }
        // a hidden top-level window (not message-only) so it receives the TaskbarCreated broadcast
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, "ClusterWidgetApp", "ClusterWidget", Native.WS_POPUP, 0, 0, 0, 0, 0, 0, inst, 0);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    private static void AddTrayIcon()
    {
        if (_trayIcon == 0) _trayIcon = MakeTrayIcon();
        NOTIFYICONDATA d = default;
        d.cbSize = (uint)sizeof(NOTIFYICONDATA);
        d.hWnd = _hwnd; d.uID = 1; d.uFlags = 0x7 /* MESSAGE | ICON | TIP */; d.uCallbackMessage = WM_TRAY; d.hIcon = _trayIcon;
        const string tip = "Cluster widget";
        for (int i = 0; i < tip.Length; i++) d.szTip[i] = tip[i];
        Native.ShellNotifyIcon(0 /* NIM_ADD */, &d);
    }

    private static void RemoveTrayIcon()
    {
        NOTIFYICONDATA d = default;
        d.cbSize = (uint)sizeof(NOTIFYICONDATA); d.hWnd = _hwnd; d.uID = 1;
        Native.ShellNotifyIcon(2 /* NIM_DELETE */, &d);
        if (_trayIcon != 0) { Native.DestroyIcon(_trayIcon); _trayIcon = 0; }
    }

    private static void TrayMenu()
    {
        Native.GetCursorPos(out POINT pt);
        nint menu = Native.CreatePopupMenu();
        for (int i = 0; i < Widgets.Count; i++)
            Native.AppendMenu(menu, 0, (nuint)(100 + i), $"Settings: {Widgets[i].Cfg.Name}");
        Native.AppendMenu(menu, 0x800, 0, null);
        Native.AppendMenu(menu, 0, 1, "New cluster");
        Native.AppendMenu(menu, 0, 2, "Exit");
        Native.SetForegroundWindow(_hwnd); // required so the menu closes when you click away
        int cmd = Native.TrackPopupMenu(menu, 0x100 | 0x2 /* RETURNCMD | RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, 0);
        Native.DestroyMenu(menu);
        if (cmd == 1) SettingsWindow.Show(NewCluster(SettingsWindow.Target ?? (Widgets.Count > 0 ? Widgets[0] : null)));
        else if (cmd == 2) Quit();
        else if (cmd >= 100 && cmd - 100 < Widgets.Count) SettingsWindow.Show(Widgets[cmd - 100]);
    }

    /// <summary>A small rounded square with four dots, drawn once at startup.</summary>
    private static nint MakeTrayIcon()
    {
        const int N = 32;
        nint pixels = Marshal.AllocHGlobal(N * N * 4);
        new Span<byte>((void*)pixels, N * N * 4).Clear();
        Gdip.GdipCreateBitmapFromScan0(N, N, N * 4, Gdip.PixelFormat32bppPARGB, pixels, out nint bmp);
        Gdip.GdipGetImageGraphicsContext(bmp, out nint g);
        Gdip.GdipSetSmoothingMode(g, 4);
        Gdip.GdipCreateSolidFill(0xFF4CC2FF, out nint blue);
        Gdip.GdipCreateSolidFill(0xFFFFFFFF, out nint white);
        nint body = Widget.RoundRect(2, 2, N - 4, N - 4, 9);
        Gdip.GdipFillPath(g, blue, body);
        Gdip.GdipDeletePath(body);
        for (int k = 0; k < 4; k++)
        {
            nint dot = Widget.RoundRect(7 + k % 2 * 11, 7 + k / 2 * 11, 8, 8, 4);
            Gdip.GdipFillPath(g, white, dot);
            Gdip.GdipDeletePath(dot);
        }
        Gdip.GdipDeleteBrush(blue); Gdip.GdipDeleteBrush(white); Gdip.GdipDeleteGraphics(g);
        Gdip.GdipCreateHICONFromBitmap(bmp, out nint icon);
        Gdip.GdipDisposeImage(bmp);
        Marshal.FreeHGlobal(pixels);
        return icon;
    }
}
