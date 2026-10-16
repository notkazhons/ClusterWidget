using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>
/// The settings window (gear icon / tray icon). A cluster picker and design picker sit on top; the settings are split
/// into tabs: Appearance, Image, Icons, Spacing, Settings icon, General. Everything applies live.
/// </summary>
internal sealed unsafe class SettingsWindow
{
    private enum Kind { Slider, TintSwatch, OutlineSwatch, OverlaySwatch }

    private sealed record Def(string Label, int Page, Kind Kind, int Min = 0, int Max = 0, string Unit = "",
        Func<Config, int>? Get = null, Action<Config, int>? Set = null, bool Blur = false, int? AutoAt = null);

    private const int PAppearance = 0, PImage = 1, PIcons = 2, PSpacing = 3, PGear = 4, PGeneral = 5;
    private static readonly string[] PageNames = { "Appearance", "Image", "Icons", "Spacing", "Settings icon", "General" };

    private const int IdCluster = 400, IdRename = 401, IdNewCluster = 402, IdDeleteCluster = 403,
        IdDesign = 410, IdSaveDesign = 411, IdDeleteDesign = 412, IdTab = 420,
        IdPreset = 300, IdSnap = 301, IdLabels = 302, IdReset = 303, IdAutoGrid = 304, IdStartup = 305, IdGear = 306, IdPreview = 311, IdGearPreview = 312,
        IdApply = 313, IdCancel = 314, IdClusterPreview = 315, IdExpandFrom = 316, IdClickLaunches = 320, IdSortAz = 317, IdSortZa = 318, IdReverse = 319,
        IdImgChoose = 307, IdImgClear = 308, IdGearAlways = 309, IdBase = 1000;

    private static readonly Def[] Defs =
    {
        // Appearance
        new("Blur", PAppearance, Kind.Slider, 0, 80, "px", c => c.Blur, (c, v) => c.Blur = v, Blur: true),
        new("Opacity", PAppearance, Kind.Slider, 0, 100, "%", c => c.Opacity, (c, v) => c.Opacity = v),
        new("Tint strength", PAppearance, Kind.Slider, 0, 100, "%", c => (int)MathF.Round((Config.Color(c.Tint, 0x8C14181C) >> 24) / 2.55f),
            (c, v) => c.Tint = Config.ColorText(((uint)MathF.Round(v * 2.55f) << 24) | (Config.Color(c.Tint, 0x8C14181C) & 0xFFFFFF))),
        new("Tint color", PAppearance, Kind.TintSwatch),
        new("Outline color", PAppearance, Kind.OutlineSwatch),
        new("Outline opacity", PAppearance, Kind.Slider, 0, 100, "%", c => c.OutlineOpacity, (c, v) => c.OutlineOpacity = v),
        new("Outline thickness", PAppearance, Kind.Slider, 0, 12, "px", c => (int)MathF.Round(c.OutlineWidth), (c, v) => c.OutlineWidth = v),
        new("Corner roundness", PAppearance, Kind.Slider, 0, 100, "px", c => (int)MathF.Round(c.CornerRadius), (c, v) => c.CornerRadius = v),
        // Image (the picture sits on top of the tint color)
        new("Picture opacity", PImage, Kind.Slider, 0, 100, "%", c => c.ImageOpacity, (c, v) => c.ImageOpacity = v),
        new("Overlay color", PImage, Kind.OverlaySwatch),
        new("Overlay opacity", PImage, Kind.Slider, 0, 100, "%", c => c.ImageOverlayOpacity, (c, v) => c.ImageOverlayOpacity = v),
        new("Picture zoom", PImage, Kind.Slider, 100, 400, "%", c => c.ImageZoom, (c, v) => c.ImageZoom = v),
        new("Picture position X", PImage, Kind.Slider, 0, 100, "%", c => c.ImagePanX, (c, v) => c.ImagePanX = v),
        new("Picture position Y", PImage, Kind.Slider, 0, 100, "%", c => c.ImagePanY, (c, v) => c.ImagePanY = v),
        // Icons
        new("Icons when closed", PIcons, Kind.Slider, 1, 16, "", c => c.ClosedCount, (c, v) => c.ClosedCount = v),
        new("Icons per row", PIcons, Kind.Slider, 0, 12, "", c => c.BoxCols, (c, v) => c.BoxCols = v, AutoAt: 0),
        new("Icons per column", PIcons, Kind.Slider, 0, 12, "", c => c.BoxRows, (c, v) => c.BoxRows = v, AutoAt: 0),
        new("Icon size", PIcons, Kind.Slider, 50, 200, "%", c => (int)MathF.Round(c.IconScale * 100), (c, v) => c.IconScale = v / 100f),
        new("Name size", PIcons, Kind.Slider, 6, 24, "px", c => (int)MathF.Round(c.LabelSize), (c, v) => c.LabelSize = v),
        // Spacing
        new("Padding, closed", PSpacing, Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.ClosedPadding), (c, v) => c.ClosedPadding = v),
        new("Spacing, closed", PSpacing, Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.ClosedGap), (c, v) => c.ClosedGap = v),
        new("Padding, expanded", PSpacing, Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.OpenPadding), (c, v) => c.OpenPadding = v),
        new("Spacing, expanded", PSpacing, Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.OpenGap), (c, v) => c.OpenGap = v),
        // Settings icon
        new("Size", PGear, Kind.Slider, 0, 40, "px", c => c.GearSize, (c, v) => c.GearSize = v, AutoAt: 0),
        new("Distance from edge, X", PGear, Kind.Slider, -1, 120, "px", c => c.GearOffsetX, (c, v) => c.GearOffsetX = v, AutoAt: -1),
        new("Distance from edge, Y", PGear, Kind.Slider, -1, 120, "px", c => c.GearOffsetY, (c, v) => c.GearOffsetY = v, AutoAt: -1),
    };

    // Closed-box spacing presets: (name, padding, gap). Padding is never smaller than the gap, so icons never sit
    // closer to the border than to each other.
    private static readonly (string Name, float Padding, float Gap)[] Presets = { ("Compact", 12f, 6f), ("Cozy", 18f, 12f), ("Roomy", 26f, 20f) };

    private static readonly (string Name, string Value)[] Corners =
    {
        ("Automatic (no overlap)", "auto"), ("Top right", "topRight"), ("Top left", "topLeft"),
        ("Bottom right", "bottomRight"), ("Bottom left", "bottomLeft"),
    };

    private const int RowH = 40, PageX = 32, TrackX = 190, TrackW = 280, ValueX = 476;

    private static SettingsWindow? _open;
    private static bool _classRegistered;

    private Widget _w;
    private nint _hwnd, _font, _tab, _clusterCombo, _designCombo, _presetCombo, _gearCombo,
        _snapCheck, _labelsCheck, _expandFromCombo, _clickLaunchCheck, _autoGridCheck, _clusterPreviewCheck, _startupCheck, _gearAlwaysCheck, _imageName, _previewToggle, _gearToggle, _iconFont;
    private readonly Dictionary<Widget, Config> _snaps = new();   // each edited cluster as it was when last applied (for Cancel)
    private float _s = 1f;
    private readonly List<nint>[] _pages = Enumerable.Range(0, PageNames.Length).Select(_ => new List<nint>()).ToArray();
    private readonly int[] _pageY = new int[PageNames.Length];
    private readonly nint[] _tracks = new nint[Defs.Length], _values = new nint[Defs.Length];
    private int _currentPage;
    private nint _pageBrush;
    private readonly HashSet<nint> _labels = new();   // the plain text labels (see WM_CTLCOLORSTATIC)

    private SettingsWindow(Widget w) { _w = w; }

    // ---------------------------------------------------------------- shared entry points

    public static Widget? Target => _open?._w;
    public static bool IsOpen => _open != null;

    public static void Show(Widget w)
    {
        if (_open != null && Native.IsWindow(_open._hwnd) != 0)
        {
            Native.ShowWindow(_open._hwnd, 9 /* SW_RESTORE */);
            Native.SetForegroundWindow(_open._hwnd);
            if (_open._w != w) _open.SwitchTo(w);
            return;
        }
        _open = new SettingsWindow(w);
        _open.Create();
    }

    /// <summary>Clusters or their files changed: refresh the pickers and values.</summary>
    public static void Refresh() { if (_open != null) { _open.RefreshClusters(); _open.Sync(); } }

    public static void TargetRemoved(Widget gone)
    {
        _open?._snaps.Remove(gone);   // a deleted cluster must not be saved again by Apply or Cancel
        if (_open != null && _open._w == gone)
        {
            var next = App.Widgets.FirstOrDefault();
            if (next == null) return;
            _open._w = next;
            _open.TakeSnap(next);
            _open.ApplyToggles(next);
            _open.RefreshClusters(); _open.Sync();
        }
        else Refresh();
    }

    public static void DeleteWithConfirm(Widget w)
    {
        nint owner = _open?._hwnd ?? 0;
        if (App.Widgets.Count <= 1)
        {
            Native.MessageBox(owner, "This is the only cluster, so it can't be deleted. Create another one first.", "Delete cluster", 0x40 /* MB_ICONINFORMATION */);
            return;
        }
        if (Native.MessageBox(owner, $"Delete the cluster \"{w.Cfg.Name}\" and its list of icons?\n\nThe files and programs themselves are not touched.",
                "Delete cluster", 0x4 | 0x30 | 0x100 /* YESNO | EXCLAMATION | DEFBUTTON2 */) != 6) return;
        App.DeleteCluster(w);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcThunk(nint h, uint m, nint w, nint l)
    {
        try { return _open != null ? _open.WndProc(h, m, w, l) : Native.DefWindowProc(h, m, w, l); }
        catch (Exception) { return Native.DefWindowProc(h, m, w, l); }
    }

    // ---------------------------------------------------------------- building the window

    private void Create()
    {
        nint inst = Native.GetModuleHandle(0);
        if (!_classRegistered)
        {
            fixed (char* cls = "ClusterWidgetSettings")
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)sizeof(WNDCLASSEX),
                    lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProcThunk,
                    hInstance = inst,
                    hCursor = Native.LoadCursor(0, 32512),
                    hbrBackground = 6 /* COLOR_WINDOW + 1: same as the tab pages, so labels and checkboxes have no grey boxes */,
                    lpszClassName = cls,
                };
                Native.RegisterClassEx(ref wc);
            }
            _classRegistered = true;
        }

        // WS_EX_TOOLWINDOW: no taskbar button. The app lives in the tray, not on the taskbar.
        const uint style = 0x00C00000 | 0x00080000 | 0x02000000; // WS_CAPTION | WS_SYSMENU | WS_CLIPCHILDREN
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, "ClusterWidgetSettings", "Cluster settings", style, 200, 200, 600, 560, 0, 0, inst, 0);
        uint dpi = Native.GetDpiForWindow(_hwnd);
        _s = dpi == 0 ? 1f : dpi / 96f;
        _font = Native.CreateFont(-(int)MathF.Round(9f * _s * 96f / 72f), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");

        // ---- always-visible header: which cluster, which saved design
        Header("Cluster", 14);
        _clusterCombo = Ctl(-1, "COMBOBOX", "", 0x3 /* CBS_DROPDOWNLIST */ | 0x200000 | 0x10000, 84, 12, 222, 200, IdCluster);
        Ctl(-1, "BUTTON", "Rename...", 0x10000, 312, 11, 84, 28, IdRename);
        Ctl(-1, "BUTTON", "New", 0x10000, 402, 11, 60, 28, IdNewCluster);
        Ctl(-1, "BUTTON", "Delete", 0x10000, 468, 11, 68, 28, IdDeleteCluster);
        Header("Design", 52);
        _designCombo = Ctl(-1, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, 84, 50, 222, 200, IdDesign);
        Ctl(-1, "BUTTON", "Save as...", 0x10000, 312, 49, 84, 28, IdSaveDesign);
        Ctl(-1, "BUTTON", "Delete", 0x10000, 402, 49, 68, 28, IdDeleteDesign);

        // ---- view toggles (top bar): see the cluster expanded / see the settings icon while editing
        _iconFont = Native.CreateFont(-(int)MathF.Round(13f * _s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe Fluent Icons");
        Header("View", 90);
        _previewToggle = Ctl(-1, "BUTTON", "\uE740", 0x3 /* AUTOCHECKBOX */ | 0x1000 /* PUSHLIKE */ | 0x10000, 84, 88, 38, 30, IdPreview);
        Native.SendMessage(_previewToggle, 0x30, _iconFont, 1);
        Ctl(-1, "STATIC", "Expanded view", 0, 128, 94, 110, 20, 0);
        _gearToggle = Ctl(-1, "BUTTON", "\uE713", 0x3 | 0x1000 | 0x10000, 250, 88, 38, 30, IdGearPreview);
        Native.SendMessage(_gearToggle, 0x30, _iconFont, 1);
        Ctl(-1, "STATIC", "Show settings icon", 0, 294, 94, 150, 20, 0);

        // ---- tabs
        int tabTop = 132;
        _tab = Native.CreateWindowEx(0, "SysTabControl32", "", 0x40000000 | 0x10000000 | 0x04000000 /* CHILD | VISIBLE | CLIPSIBLINGS */ | 0x10000,
            (int)(12 * _s), (int)(tabTop * _s), (int)(576 * _s), (int)(420 * _s), _hwnd, IdTab, inst, 0);
        Native.SendMessage(_tab, 0x30, _font, 1);
        for (int i = 0; i < PageNames.Length; i++)
        {
            fixed (char* t = PageNames[i])
            {
                var item = new TCITEM { mask = 1 /* TCIF_TEXT */, pszText = t };
                Native.SendMessage(_tab, 0x133E /* TCM_INSERTITEMW */, i, (nint)(&item));
            }
            _pageY[i] = tabTop + 46;
        }

        BuildAppearance();
        BuildImage();
        BuildIcons();
        BuildSpacing();
        BuildGear();
        BuildGeneral();

        RefreshClusters();
        RefreshDesigns(null);
        Sync();
        ShowPage(0);

        Ctl(-1, "BUTTON", "Apply changes", 0x1 | 0x10000, 300, tabTop + 420 + 10, 150, 32, IdApply);
        Ctl(-1, "BUTTON", "Cancel", 0x10000, 458, tabTop + 420 + 10, 110, 32, IdCancel);
        TakeSnap(_w);
        int clientW = 600, clientH = tabTop + 420 + 10 + 32 + 14;
        Native.GetWindowRect(_hwnd, out RECT wr);
        Native.GetClientRect(_hwnd, out RECT cr);
        int w = (int)(clientW * _s) + (wr.Right - wr.Left) - cr.Right, h = (int)(clientH * _s) + (wr.Bottom - wr.Top) - cr.Bottom;
        Native.SetWindowPos(_hwnd, 0 /* HWND_TOP */, 220, 100, w, h, 0);
        Native.ShowWindow(_hwnd, 5);
        Native.SetForegroundWindow(_hwnd);
    }

    private nint Ctl(int page, string cls, string text, uint style, int x, int y, int w, int h, int id)
    {
        nint c = Native.CreateWindowEx(0, cls, text, 0x40000000 | 0x10000000 | style, (int)(x * _s), (int)(y * _s), (int)(w * _s), (int)(h * _s),
            _hwnd, id, Native.GetModuleHandle(0), 0);
        Native.SendMessage(c, 0x30 /* WM_SETFONT */, _font, 1);
        if (page >= 0) _pages[page].Add(c);
        if (cls == "STATIC") _labels.Add(c);
        return c;
    }

    private void Header(string text, int y) => Ctl(-1, "STATIC", text, 0, 16, y + 4, 64, 20, 0);

    private int NextY(int page, int height = RowH) { int y = _pageY[page]; _pageY[page] += height; return y; }

    /// <summary>One row for every slider / swatch of a page, in order.</summary>
    private void AddDefs(int page)
    {
        for (int i = 0; i < Defs.Length; i++)
        {
            if (Defs[i].Page != page) continue;
            var d = Defs[i];
            int y = NextY(page);
            Ctl(page, "STATIC", d.Label, 0, PageX, y + 4, 156, 20, 0);
            if (d.Kind != Kind.Slider)
            {
                Ctl(page, "BUTTON", "", 0xB /* BS_OWNERDRAW */ | 0x10000, TrackX - 4, y, 80, 28, IdBase + i);
                continue;
            }
            _tracks[i] = Ctl(page, "msctls_trackbar32", "", 0x10 /* TBS_NOTICKS */ | 0x10000, TrackX, y, TrackW, 28, IdBase + i);
            Native.SendMessage(_tracks[i], 0x407 /* TBM_SETRANGEMIN */, 0, d.Min);
            Native.SendMessage(_tracks[i], 0x408 /* TBM_SETRANGEMAX */, 0, d.Max);
            _values[i] = Ctl(page, "STATIC", "", 0x2 /* SS_RIGHT */, ValueX, y + 4, 64, 20, 0);
            _labels.Remove(_values[i]);   // values change, so they repaint their background (transparent text would pile up)
        }
    }

    private void Row(int page, string label, Func<int, nint> make)
    {
        int y = NextY(page);
        Ctl(page, "STATIC", label, 0, PageX, y + 4, 156, 20, 0);
        make(y);
    }

    private void BuildAppearance() => AddDefs(PAppearance);

    private void BuildImage()
    {
        int y = NextY(PImage);
        Ctl(PImage, "STATIC", "Picture", 0, PageX, y + 6, 156, 20, 0);
        Ctl(PImage, "BUTTON", "Choose...", 0x10000, TrackX - 4, y, 110, 28, IdImgChoose);
        Ctl(PImage, "BUTTON", "Remove", 0x10000, TrackX + 112, y, 90, 28, IdImgClear);
        y = NextY(PImage, 30);
        _imageName = Ctl(PImage, "STATIC", "", 0x4000 /* SS_ENDELLIPSIS */, TrackX, y, 340, 20, 0);
        _labels.Remove(_imageName);
        AddDefs(PImage);
    }

    private void BuildIcons()
    {
        int y = NextY(PIcons, 34);
        _autoGridCheck = Ctl(PIcons, "BUTTON", "Automatic grid (size follows the icon count)", 0x3 /* BS_AUTOCHECKBOX */ | 0x10000, PageX, y, 440, 24, IdAutoGrid);
        AddDefs(PIcons);
        y = NextY(PIcons, 34);
        _clusterPreviewCheck = Ctl(PIcons, "BUTTON", "Last closed icon is a mini preview of the rest", 0x3 | 0x10000, PageX, y, 440, 24, IdClusterPreview);
        y = NextY(PIcons, 34);
        _labelsCheck = Ctl(PIcons, "BUTTON", "Show names when expanded", 0x3 | 0x10000, PageX, y, 340, 24, IdLabels);
        y = NextY(PIcons, 34);
        _clickLaunchCheck = Ctl(PIcons, "BUTTON", "Clicking an icon opens the app (only the preview opens the cluster)", 0x3 | 0x10000, PageX, y, 520, 24, IdClickLaunches);
        y = NextY(PIcons, 44);
        Ctl(PIcons, "STATIC", "Order (or drag icons)", 0, PageX, y + 6, 230, 20, 0);
        Ctl(PIcons, "BUTTON", "A to Z", 0x10000, PageX + 240, y, 76, 28, IdSortAz);
        Ctl(PIcons, "BUTTON", "Z to A", 0x10000, PageX + 322, y, 76, 28, IdSortZa);
        Ctl(PIcons, "BUTTON", "Reverse", 0x10000, PageX + 404, y, 76, 28, IdReverse);
    }

    private void BuildSpacing()
    {
        AddDefs(PSpacing);
        Row(PSpacing, "Closed preset", y =>
        {
            _presetCombo = Ctl(PSpacing, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, TrackX - 4, y, 220, 160, IdPreset);
            foreach (var p in Presets) AddString(_presetCombo, p.Name);
            return _presetCombo;
        });
    }

    private void BuildGear()
    {
        Row(PGear, "Position", y =>
        {
            _gearCombo = Ctl(PGear, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, TrackX - 4, y, 220, 200, IdGear);
            foreach (var c in Corners) AddString(_gearCombo, c.Name);
            return _gearCombo;
        });
        AddDefs(PGear);
        int y = NextY(PGear);
        _gearAlwaysCheck = Ctl(PGear, "BUTTON", "Always show the settings icon", 0x3 | 0x10000, PageX, y, 340, 24, IdGearAlways);
    }

    private void BuildGeneral()
    {
        Row(PGeneral, "Expand from", y =>
        {
            _expandFromCombo = Ctl(PGeneral, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, TrackX - 4, y, 240, 120, IdExpandFrom);
            AddString(_expandFromCombo, "Center (grows evenly all round)");
            AddString(_expandFromCombo, "Top-left corner (grows right and down)");
            return _expandFromCombo;
        });
        int y = NextY(PGeneral, 34);
        _snapCheck = Ctl(PGeneral, "BUTTON", "Snap to the desktop grid", 0x3 | 0x10000, PageX, y, 360, 24, IdSnap);
        y = NextY(PGeneral, 34);
        _startupCheck = Ctl(PGeneral, "BUTTON", "Start with Windows (all clusters)", 0x3 | 0x10000, PageX, y, 360, 24, IdStartup);
        y = NextY(PGeneral, 50);
        Ctl(PGeneral, "BUTTON", "Reset this cluster's look and layout", 0x10000, PageX, y + 6, 250, 30, IdReset);
    }

    private void AddString(nint combo, string s)
    {
        fixed (char* p = s) Native.SendMessage(combo, 0x143 /* CB_ADDSTRING */, 0, (nint)p);
    }

    private void ShowPage(int page)
    {
        _currentPage = page;
        Native.SendMessage(_tab, 0x130C /* TCM_SETCURSEL */, page, 0);
        for (int p = 0; p < _pages.Length; p++)
            foreach (var c in _pages[p]) Native.ShowWindow(c, p == page ? 5 : 0);
    }

    // ---------------------------------------------------------------- values <-> controls

    private static string ValueText(Def d, int v) => d.AutoAt == v ? "auto" : v + d.Unit;

    /// <summary>Pushes the current config into every control.</summary>
    private void Sync()
    {
        var cfg = _w.Cfg;
        for (int i = 0; i < Defs.Length; i++)
        {
            if (Defs[i].Kind != Kind.Slider) continue;
            int v = Defs[i].Get!(cfg);
            Native.SendMessage(_tracks[i], 0x405 /* TBM_SETPOS */, 1, v);
            Native.SetWindowText(_values[i], ValueText(Defs[i], v));
        }
        Native.SendMessage(_snapCheck, 0xF1 /* BM_SETCHECK */, cfg.Snap ? 1 : 0, 0);
        Native.SendMessage(_labelsCheck, 0xF1, cfg.ShowLabels ? 1 : 0, 0);
        bool autoGrid = !(cfg.BoxCols > 0 && cfg.BoxRows > 0);
        Native.SendMessage(_autoGridCheck, 0xF1, autoGrid ? 1 : 0, 0);
        Native.SendMessage(_clusterPreviewCheck, 0xF1, cfg.ClusterPreview ? 1 : 0, 0);
        Native.SendMessage(_clickLaunchCheck, 0xF1, cfg.ClickLaunches ? 1 : 0, 0);
        UpdateEnabled(autoGrid);
        Native.SendMessage(_gearAlwaysCheck, 0xF1, cfg.GearAlways ? 1 : 0, 0);
        Native.SendMessage(_startupCheck, 0xF1, Native.StartsWithWindows() ? 1 : 0, 0);
        Native.SetWindowText(_imageName, cfg.BackgroundImage.Length == 0 ? "(none)" : System.IO.Path.GetFileName(cfg.BackgroundImage));

        int preset = -1;
        for (int i = 0; i < Presets.Length; i++)
            if (MathF.Abs(cfg.ClosedPadding - Presets[i].Padding) < 0.5f && MathF.Abs(cfg.ClosedGap - Presets[i].Gap) < 0.5f) preset = i;
        Native.SendMessage(_presetCombo, 0x14E /* CB_SETCURSEL */, preset, 0);
        Native.SendMessage(_expandFromCombo, 0x14E, cfg.ExpandFrom == "topLeft" ? 1 : 0, 0);
        int corner = 0;
        for (int i = 0; i < Corners.Length; i++)
            if (Corners[i].Value.Equals(cfg.GearCorner, StringComparison.OrdinalIgnoreCase)) corner = i;
        Native.SendMessage(_gearCombo, 0x14E, corner, 0);
        for (int i = 0; i < Defs.Length; i++)
            if (Defs[i].Kind != Kind.Slider) Native.InvalidateRect(Native.GetDlgItem(_hwnd, IdBase + i), 0, 1);
    }

    /// <summary>Icons when closed only applies to an automatic grid; icons per row / column only to a fixed one.</summary>
    private void UpdateEnabled(bool autoGrid)
    {
        for (int i = 0; i < Defs.Length; i++)
        {
            bool count = Defs[i].Label == "Icons when closed", grid = Defs[i].Label is "Icons per row" or "Icons per column";
            if (!count && !grid) continue;
            int on = (count ? autoGrid : !autoGrid) ? 1 : 0;
            Native.EnableWindow(_tracks[i], on);
            Native.EnableWindow(_values[i], on);
        }
    }

    private void RefreshClusters()
    {
        Native.SendMessage(_clusterCombo, 0x14B /* CB_RESETCONTENT */, 0, 0);
        foreach (var w in App.Widgets) AddString(_clusterCombo, w.Cfg.Name);
        Native.SendMessage(_clusterCombo, 0x14E, Math.Max(0, App.Widgets.IndexOf(_w)), 0);
    }

    private void RefreshDesigns(string? select)
    {
        Native.SendMessage(_designCombo, 0x14B, 0, 0);
        AddString(_designCombo, "(saved designs)");
        string[] names = Designs.List();
        foreach (var n in names) AddString(_designCombo, n);
        int idx = select == null ? 0 : Array.FindIndex(names, n => n.Equals(select, StringComparison.OrdinalIgnoreCase)) + 1;
        Native.SendMessage(_designCombo, 0x14E, Math.Max(0, idx), 0);
    }

    private string? SelectedDesign()
    {
        int sel = (int)Native.SendMessage(_designCombo, 0x147 /* CB_GETCURSEL */, 0, 0);
        string[] names = Designs.List();
        return sel >= 1 && sel <= names.Length ? names[sel - 1] : null;
    }

    /// <summary>Remembers how a cluster looked before this editing session, so Cancel can put it back.</summary>
    private void TakeSnap(Widget w) { if (!_snaps.ContainsKey(w)) _snaps[w] = w.Cfg.Clone(); }

    private void ApplyToggles(Widget w)
    {
        w.SetPreviewOpen(Native.SendMessage(_previewToggle, 0xF0, 0, 0) == 1);
        w.SetGearPreview(Native.SendMessage(_gearToggle, 0xF0, 0, 0) == 1);
    }

    private void Apply()
    {
        foreach (var w in _snaps.Keys.ToArray())
        {
            if (!App.Widgets.Contains(w)) { _snaps.Remove(w); continue; }
            w.SaveNow(); _snaps[w] = w.Cfg.Clone();
        }
    }

    private void Cancel()
    {
        foreach (var kv in _snaps.ToArray())
        {
            var w = kv.Key; var snap = kv.Value;
            if (!App.Widgets.Contains(w)) continue;
            bool snapFlag = w.Cfg.Snap != snap.Snap;
            w.Cfg.ApplyDesign(snap);
            w.Cfg.Snap = snap.Snap;
            if (snapFlag) w.SnapToggled(); else w.LookChanged(true);
            w.SaveNow(); // the file goes back to exactly what was applied before
        }
        _snaps.Clear();
        Native.DestroyWindow(_hwnd);
    }

    private void SwitchTo(Widget w)
    {
        var old = _w;
        old.SetPreviewOpen(false); old.SetGearPreview(false);
        _w = w;
        TakeSnap(w);
        ApplyToggles(w);
        RefreshClusters();
        Sync();
        w.Repaint();
    }

    // ---------------------------------------------------------------- pickers

    private static uint ColorRef(uint argb) => ((argb >> 16) & 0xFF) | (argb & 0xFF00) | ((argb & 0xFF) << 16);
    private static uint FromColorRef(uint c, uint alpha) => (alpha << 24) | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);

    private uint ColorOf(Kind kind) => kind switch
    {
        Kind.TintSwatch => Config.Color(_w.Cfg.Tint, 0x8C14181C),
        Kind.OutlineSwatch => Config.Color(_w.Cfg.OutlineColor, 0xFF4CC2FF),
        _ => Config.Color(_w.Cfg.ImageOverlayColor, 0xFFFFFFFF),
    };

    private void PickColor(Kind kind, int controlId)
    {
        var cfg = _w.Cfg;
        uint cur = ColorOf(kind);
        uint* custom = stackalloc uint[16];
        for (int i = 0; i < 16; i++) custom[i] = 0xFFFFFF;
        var cc = new CHOOSECOLOR
        {
            lStructSize = (uint)sizeof(CHOOSECOLOR), hwndOwner = _hwnd, rgbResult = ColorRef(cur),
            lpCustColors = (nint)custom, Flags = 0x1 | 0x2 /* CC_RGBINIT | CC_FULLOPEN */,
        };
        if (Native.ChooseColor(ref cc) == 0) return;
        uint argb = FromColorRef(cc.rgbResult, cur >> 24);
        if (kind == Kind.TintSwatch) cfg.Tint = Config.ColorText(argb);
        else if (kind == Kind.OutlineSwatch) cfg.OutlineColor = Config.ColorText(argb);
        else cfg.ImageOverlayColor = Config.ColorText(argb | 0xFF000000);
        Native.InvalidateRect(Native.GetDlgItem(_hwnd, controlId), 0, 1);
        _w.LookChanged(false);
    }

    private void ChooseImage()
    {
        // "Images\0*.png;...\0All files\0*.*\0\0": the dialog wants NUL-separated, double-NUL-terminated text
        const string filter = "Images\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff\0All files\0*.*\0\0";
        char* file = stackalloc char[1024];
        for (int i = 0; i < 1024; i++) file[i] = '\0';
        fixed (char* f = filter)
        {
            var ofn = new OPENFILENAME
            {
                lStructSize = (uint)sizeof(OPENFILENAME), hwndOwner = _hwnd, lpstrFilter = f, nFilterIndex = 1,
                lpstrFile = file, nMaxFile = 1024,
                Flags = 0x1000 | 0x800 | 0x80000 | 0x8, // FILEMUSTEXIST | PATHMUSTEXIST | EXPLORER | NOCHANGEDIR
            };
            if (Native.GetOpenFileName(ref ofn) == 0) return;
        }
        _w.Cfg.BackgroundImage = new string(file);
        Sync();
        _w.LookChanged(false);
    }

    private void SaveDesign()
    {
        string? name = TextPrompt.Ask(_hwnd, "Save design", "Name for this design:", SelectedDesign() ?? "My design");
        if (name == null) return;
        if (Designs.Exists(name) &&
            Native.MessageBox(_hwnd, $"A design called \"{name}\" already exists. Replace it?", "Save design", 0x4 | 0x20 | 0x100) != 6) return;
        Designs.Save(name, _w.Cfg);
        RefreshDesigns(name);
    }

    private void LoadDesign(string name)
    {
        var d = Designs.Load(name);
        if (d == null) return;
        _w.Cfg.ApplyDesign(d);
        Sync();
        _w.LookChanged(true);
    }

    private void DeleteDesign()
    {
        string? name = SelectedDesign();
        if (name == null) return;
        if (Native.MessageBox(_hwnd, $"Delete the saved design \"{name}\"?", "Delete design", 0x4 | 0x30 | 0x100) != 6) return;
        Designs.Delete(name);
        RefreshDesigns(null);
    }

    // ---------------------------------------------------------------- messages

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case 0x138: // WM_CTLCOLORSTATIC: draw labels without their own grey box, so they sit cleanly on the tab pages
                if (_labels.Contains(lp))
                {
                    Native.SetBkMode(wp, 1 /* TRANSPARENT */);
                    return Native.GetStockObject(5 /* NULL_BRUSH */);
                }
                // trackbars ask too: paint them the colour of the tab page instead of dialog grey
                if (_pageBrush == 0) _pageBrush = Native.CreateSolidBrush(0xFBFBFB);
                return _pageBrush;

            case 0x4E: // WM_NOTIFY: tab changed
            {
                var nm = (NMHDR*)lp;
                if (nm->hwndFrom == _tab && nm->code == -551 /* TCN_SELCHANGE */)
                    ShowPage((int)Native.SendMessage(_tab, 0x130B /* TCM_GETCURSEL */, 0, 0));
                return 0;
            }

            case 0x114: // WM_HSCROLL
                for (int i = 0; i < Defs.Length; i++)
                {
                    if (_tracks[i] == 0 || _tracks[i] != lp) continue;
                    int pos = (int)Native.SendMessage(lp, 0x400 /* TBM_GETPOS */, 0, 0);
                    Defs[i].Set!(_w.Cfg, pos);
                    Native.SetWindowText(_values[i], ValueText(Defs[i], pos));
                    _w.LookChanged(Defs[i].Blur);
                    break;
                }
                Sync(); // padding/spacing sliders may no longer match a preset
                return 0;

            case 0x111: // WM_COMMAND
                OnCommand((int)(wp & 0xFFFF), (int)((wp >> 16) & 0xFFFF));
                return 0;

            case 0x2B: // WM_DRAWITEM: the color swatch buttons
            {
                var di = (DRAWITEMSTRUCT*)lp;
                int index = (int)di->CtlID - IdBase;
                if (index < 0 || index >= Defs.Length) break;
                uint argb = ColorOf(Defs[index].Kind);
                nint fill = Native.CreateSolidBrush(ColorRef(argb)), edge = Native.CreateSolidBrush(0x808080);
                Native.FillRect(di->hDC, di->rcItem, fill);
                Native.FrameRect(di->hDC, di->rcItem, edge);
                Native.DeleteObject(fill); Native.DeleteObject(edge);
                return 1;
            }

            case 0x10: // WM_CLOSE: the window's X works like Cancel
                Cancel();
                return 0;

            case Native.WM_DESTROY:
            {
                if (_font != 0) { Native.DeleteObject(_font); _font = 0; }
                if (_iconFont != 0) { Native.DeleteObject(_iconFont); _iconFont = 0; }
                if (_pageBrush != 0) { Native.DeleteObject(_pageBrush); _pageBrush = 0; }
                _open = null;
                foreach (var cw in App.Widgets.ToArray()) { cw.SetPreviewOpen(false); cw.SetGearPreview(false); } // previews end with the window
                return 0; // closing settings must not quit the app
            }
        }
        return Native.DefWindowProc(h, msg, wp, lp);
    }

    private void OnCommand(int id, int code)
    {
        var cfg = _w.Cfg;
        if (id >= IdBase && id < IdBase + Defs.Length && Defs[id - IdBase].Kind != Kind.Slider) { PickColor(Defs[id - IdBase].Kind, id); return; }
        switch (id)
        {
            case IdCluster when code == 1 /* CBN_SELCHANGE */:
            {
                int sel = (int)Native.SendMessage(_clusterCombo, 0x147, 0, 0);
                if (sel >= 0 && sel < App.Widgets.Count && App.Widgets[sel] != _w) SwitchTo(App.Widgets[sel]);
                break;
            }
            case IdRename:
            {
                string? name = TextPrompt.Ask(_hwnd, "Rename cluster", "Cluster name:", cfg.Name);
                if (name == null) break;
                cfg.Name = name;
                _w.SaveNow();
                RefreshClusters();
                break;
            }
            case IdNewCluster: SwitchTo(App.NewCluster(_w)); break;
            case IdDeleteCluster: DeleteWithConfirm(_w); break;

            case IdDesign when code == 1:
            {
                string? name = SelectedDesign();
                if (name != null) LoadDesign(name);
                break;
            }
            case IdSaveDesign: SaveDesign(); break;
            case IdDeleteDesign: DeleteDesign(); break;

            case IdPreset when code == 1:
            {
                int sel = (int)Native.SendMessage(_presetCombo, 0x147, 0, 0);
                if (sel >= 0 && sel < Presets.Length)
                {
                    cfg.ClosedPadding = Presets[sel].Padding; cfg.ClosedGap = Presets[sel].Gap;
                    Sync();
                    _w.LookChanged(false);
                }
                break;
            }
            case IdGear when code == 1:
            {
                int sel = (int)Native.SendMessage(_gearCombo, 0x147, 0, 0);
                if (sel >= 0 && sel < Corners.Length) { cfg.GearCorner = Corners[sel].Value; _w.LookChanged(false); }
                break;
            }
            case IdExpandFrom when code == 1:
                cfg.ExpandFrom = Native.SendMessage(_expandFromCombo, 0x147, 0, 0) == 1 ? "topLeft" : "center";
                _w.LookChanged(false);
                break;
            case IdSortAz: _w.SortItems(0); break;
            case IdSortZa: _w.SortItems(1); break;
            case IdReverse: _w.SortItems(2); break;
            case IdImgChoose: ChooseImage(); break;
            case IdImgClear: cfg.BackgroundImage = ""; Sync(); _w.LookChanged(false); break;

            case IdSnap: cfg.Snap = Native.SendMessage(_snapCheck, 0xF0 /* BM_GETCHECK */, 0, 0) == 1; _w.SnapToggled(); break;
            case IdLabels: cfg.ShowLabels = Native.SendMessage(_labelsCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdGearAlways: cfg.GearAlways = Native.SendMessage(_gearAlwaysCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdPreview: _w.SetPreviewOpen(Native.SendMessage(_previewToggle, 0xF0, 0, 0) == 1); break;
            case IdGearPreview: _w.SetGearPreview(Native.SendMessage(_gearToggle, 0xF0, 0, 0) == 1); break;
            case IdApply: Apply(); break;
            case IdCancel: Cancel(); break;
            case IdClickLaunches: cfg.ClickLaunches = Native.SendMessage(_clickLaunchCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdClusterPreview: cfg.ClusterPreview = Native.SendMessage(_clusterPreviewCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdAutoGrid:
                if (Native.SendMessage(_autoGridCheck, 0xF0, 0, 0) == 1) { cfg.BoxCols = 0; cfg.BoxRows = 0; }
                else { var (c, r) = _w.GridDims; cfg.BoxCols = Math.Max(1, c); cfg.BoxRows = Math.Max(1, r); }
                Sync();
                _w.LookChanged(false);
                break;
            case IdStartup: Native.SetStartsWithWindows(Native.SendMessage(_startupCheck, 0xF0, 0, 0) == 1); break;
            case IdReset:
                // MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2: "No" is the default so Enter cannot reset by accident
                if (Native.MessageBox(_hwnd, $"Reset the look and layout of \"{cfg.Name}\" to the defaults?\n\nIts icons and position are kept. Saved designs are not touched.",
                        "Reset settings", 0x4 | 0x20 | 0x100) == 6 /* IDYES */)
                {
                    cfg.ResetLook(); Sync(); _w.LookChanged(true);
                }
                break;
        }
    }
}
