using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ClusterWidget;

/// <summary>
/// The settings window (gear icon / tray icon). Top: which cluster you are editing, which saved design, and two preview
/// toggles. Below: tabs (Look, Picture, Icons, Size and spacing, Behavior, About). Everything applies live; Apply keeps it,
/// Cancel puts it back. Every control has hover help.
/// </summary>
internal sealed unsafe class SettingsWindow
{
    private enum Kind { Slider, TintSwatch, OutlineSwatch, OverlaySwatch }

    private sealed record Def(string Label, int Page, Kind Kind, string Tip, int Min = 0, int Max = 0, string Unit = "",
        Func<Config, int>? Get = null, Action<Config, int>? Set = null, bool Blur = false, int? AutoAt = null);

    private const int PLook = 0, PPicture = 1, PIcons = 2, PSize = 3, PBehavior = 4, PAbout = 5;
    private static readonly string[] PageNames = { "Look", "Picture", "Icons", "Size and spacing", "Behavior", "About" };

    private const int IdApply = 1, IdCancel = 2,   // IDOK / IDCANCEL: Enter and Esc work like in any dialog
        IdCluster = 400, IdRename = 401, IdNewCluster = 402, IdDeleteCluster = 403,
        IdDesign = 410, IdSaveDesign = 411, IdDeleteDesign = 412, IdTab = 420,
        IdPreset = 300, IdSnap = 301, IdLabels = 302, IdReset = 303, IdAutoGrid = 304, IdStartup = 305, IdGear = 306,
        IdImgChoose = 307, IdImgClear = 308, IdGearAlways = 309, IdPreview = 311, IdGearPreview = 312,
        IdClusterPreview = 315, IdExpandFrom = 316, IdSortAz = 317, IdSortZa = 318, IdReverse = 319, IdClickLaunches = 320,
        IdOpenFolder = 321, IdOpenFile = 322, IdSetDefault = 323, IdClearDefault = 324, IdBase = 1000;

    private static readonly Def[] Defs =
    {
        // ---- Look
        new("Blur", PLook, Kind.Slider, "How strongly the wallpaper behind the cluster is blurred. 0 shows it sharp.", 0, 80, "px", c => c.Blur, (c, v) => c.Blur = v, Blur: true),
        new("Opacity", PLook, Kind.Slider, "How solid the whole box is. Lower it to see your desktop through the box.", 0, 100, "%", c => c.Opacity, (c, v) => c.Opacity = v),
        new("Tint strength", PLook, Kind.Slider, "How much of the tint colour is painted over the blurred wallpaper.", 0, 100, "%",
            c => (int)MathF.Round((Config.Color(c.Tint, 0x8C14181C) >> 24) / 2.55f),
            (c, v) => c.Tint = Config.ColorText(((uint)MathF.Round(v * 2.55f) << 24) | (Config.Color(c.Tint, 0x8C14181C) & 0xFFFFFF))),
        new("Tint color", PLook, Kind.TintSwatch, "The colour painted over the blur. Click the square to choose one."),
        new("Outline color", PLook, Kind.OutlineSwatch, "Colour of the line around the box. Click the square to choose one."),
        new("Outline opacity", PLook, Kind.Slider, "How visible the outline is.", 0, 100, "%", c => c.OutlineOpacity, (c, v) => c.OutlineOpacity = v),
        new("Outline thickness", PLook, Kind.Slider, "Thickness of the outline in pixels. 0 hides it.", 0, 12, "px", c => (int)MathF.Round(c.OutlineWidth), (c, v) => c.OutlineWidth = v),
        new("Corner roundness", PLook, Kind.Slider, "How round the corners are. Large values make a pill or a circle.", 0, 100, "px", c => (int)MathF.Round(c.CornerRadius), (c, v) => c.CornerRadius = v),
        // ---- Picture (sits on top of the tint colour)
        new("Picture opacity", PPicture, Kind.Slider, "How visible the picture is over the tint colour.", 0, 100, "%", c => c.ImageOpacity, (c, v) => c.ImageOpacity = v),
        new("Overlay color", PPicture, Kind.OverlaySwatch, "A second colour painted over the picture, for example a light white tint so icons stand out. Click the square to choose one."),
        new("Overlay opacity", PPicture, Kind.Slider, "How strong the overlay colour is. 0 turns it off.", 0, 100, "%", c => c.ImageOverlayOpacity, (c, v) => c.ImageOverlayOpacity = v),
        new("Zoom", PPicture, Kind.Slider, "Zoom into the picture. 100% just covers the box.", 100, 400, "%", c => c.ImageZoom, (c, v) => c.ImageZoom = v),
        new("Position, left to right", PPicture, Kind.Slider, "Which part of a zoomed picture is shown (horizontally).", 0, 100, "%", c => c.ImagePanX, (c, v) => c.ImagePanX = v),
        new("Position, top to bottom", PPicture, Kind.Slider, "Which part of a zoomed picture is shown (vertically).", 0, 100, "%", c => c.ImagePanY, (c, v) => c.ImagePanY = v),
        // ---- Icons (the closed box)
        new("Icons when closed", PIcons, Kind.Slider, "How many icons the closed box shows. Used when the automatic grid is on.", 1, 16, "", c => c.ClosedCount, (c, v) => c.ClosedCount = v),
        new("Icons per row", PIcons, Kind.Slider, "Columns of the closed box. Used when the automatic grid is off.", 0, 12, "", c => c.BoxCols, (c, v) => c.BoxCols = v, AutoAt: 0),
        new("Icons per column", PIcons, Kind.Slider, "Rows of the closed box. Used when the automatic grid is off.", 0, 12, "", c => c.BoxRows, (c, v) => c.BoxRows = v, AutoAt: 0),
        // ---- Size and spacing
        new("Icon size", PSize, Kind.Slider, "Icon size as a percentage of your Windows desktop icon size.", 50, 200, "%", c => (int)MathF.Round(c.IconScale * 100), (c, v) => c.IconScale = v / 100f),
        new("Name size", PSize, Kind.Slider, "Size of the names shown under icons in the expanded view.", 6, 24, "px", c => (int)MathF.Round(c.LabelSize), (c, v) => c.LabelSize = v),
        new("Padding, closed", PSize, Kind.Slider, "Space between the box edge and the icons when closed.", 0, 60, "px", c => (int)MathF.Round(c.ClosedPadding), (c, v) => c.ClosedPadding = v),
        new("Spacing, closed", PSize, Kind.Slider, "Space between icons when closed. 0 makes them touch.", 0, 60, "px", c => (int)MathF.Round(c.ClosedGap), (c, v) => c.ClosedGap = v),
        new("Padding, expanded", PSize, Kind.Slider, "Space between the box edge and the icons when expanded.", 0, 60, "px", c => (int)MathF.Round(c.OpenPadding), (c, v) => c.OpenPadding = v),
        new("Spacing, expanded", PSize, Kind.Slider, "Space between icons when expanded. 0 makes them touch.", 0, 60, "px", c => (int)MathF.Round(c.OpenGap), (c, v) => c.OpenGap = v),
        // ---- Behavior > settings icon
        new("Size", PBehavior, Kind.Slider, "Size of the settings icon. Auto scales with your icons.", 0, 40, "px", c => c.GearSize, (c, v) => c.GearSize = v, AutoAt: 0),
        new("Distance from edge, X", PBehavior, Kind.Slider, "How far the settings icon is from the left or right edge. Auto picks a distance for you.", -1, 120, "px", c => c.GearOffsetX, (c, v) => c.GearOffsetX = v, AutoAt: -1),
        new("Distance from edge, Y", PBehavior, Kind.Slider, "How far the settings icon is from the top or bottom edge. Auto picks a distance for you.", -1, 120, "px", c => c.GearOffsetY, (c, v) => c.GearOffsetY = v, AutoAt: -1),
    };

    // Closed-box spacing presets: (name, padding, gap). Padding is never smaller than the gap, so icons never sit
    // closer to the border than to each other.
    private static readonly (string Name, float Padding, float Gap)[] Presets = { ("Compact", 12f, 6f), ("Cozy", 18f, 12f), ("Roomy", 26f, 20f) };

    private static readonly (string Name, string Value)[] Corners =
    {
        ("Automatic (never overlaps an icon)", "auto"), ("Top right", "topRight"), ("Top left", "topLeft"),
        ("Bottom right", "bottomRight"), ("Bottom left", "bottomLeft"),
    };

    private static readonly string AboutText =
        "ClusterWidget " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "") + "\r\n\r\n" +
        "ClusterWidget puts small boxes of shortcuts on your desktop. Each box is a CLUSTER. Closed, it shows a few icons; open it to see them all.\r\n\r\n" +
        "USING A CLUSTER\r\n" +
        "- Click it to open it. Press Esc or click anywhere else to close it.\r\n" +
        "- Drag the box to move it. It snaps to your desktop icon grid unless you turn that off (Behavior tab).\r\n" +
        "- Drop files, folders, programs or shortcuts onto it to add them. Shortcuts are copied into the app's own folder, so deleting the original from your desktop is safe.\r\n" +
        "- Open view: drag an icon onto another slot to re-order, or use A to Z / Z to A on the Icons tab.\r\n" +
        "- Right-click: settings, arrange icons, remove an icon, new or delete cluster, exit.\r\n" +
        "- Keyboard only: with the desktop in front, press Tab to step into the clusters. A cluster that can open opens by itself; Tab / Shift+Tab or the arrow keys move between its icons, Enter opens the focused app, Esc goes back to the desktop. Tab past the last icon continues with the next cluster, then the taskbar.\r\n" +
        "- Move the mouse over a cluster to see the settings icon (it opens this window). Hover any icon to see its name and location.\r\n\r\n" +
        "THIS WINDOW\r\n" +
        "- Changes show live. Apply changes keeps them; Cancel undoes everything since your last Apply. The status next to the buttons tells you which.\r\n" +
        "- Expanded view / Settings icon (top) are previews only: they help you see what you are editing and switch off when this window closes.\r\n" +
        "- A DESIGN is a saved look and layout. Save the current one under a name, then apply it to any cluster. Designs do not include icons or position.\r\n" +
        "- Several clusters: each has its own icons, position and design. Use New / Delete at the top. A new cluster starts with the current design, in the middle of the screen.\r\n\r\n" +
        "GOOD TO KNOW\r\n" +
        "- Clusters stay visible when you press Win+D (show desktop).\r\n" +
        "- The tray icon (in the hidden icons arrow) opens settings, creates clusters and exits.\r\n" +
        "- Idle cost is zero: nothing runs while you are not using a cluster.\r\n" +
        "- Only the wallpaper is blurred behind a cluster. It cannot blur windows.\r\n" +
        "- If you move or delete a program, its icon turns into an empty square. Remove it or drop it again.\r\n\r\n" +
        "YOUR FILES\r\n" +
        "Everything is in %APPDATA%\\ClusterWidget: clusters (one .json per cluster), designs (saved designs) and shortcuts (copies of dropped shortcuts). You can edit the .json files by hand; changes are picked up while the app runs. Copy the folder to back up or move your setup.";

    private const int PageX = 28, LabelW = 150, TrackX = 184, TrackW = 270, ValueX = 458, ValueW = 56, ClientW = 560;

    private static SettingsWindow? _open;
    private static bool _classRegistered;

    private Widget _w;
    private nint _hwnd, _font, _boldFont, _tab, _tip, _clusterCombo, _designCombo, _presetCombo, _gearCombo, _expandFromCombo,
        _snapCheck, _labelsCheck, _autoGridCheck, _clusterPreviewCheck, _clickLaunchCheck, _startupCheck, _gearAlwaysCheck,
        _imageName, _lookNote, _previewToggle, _gearToggle, _applyBtn, _cancelBtn, _status, _pageBrush;
    private float _s = 1f;
    private readonly List<nint>[] _pages = Enumerable.Range(0, PageNames.Length).Select(_ => new List<nint>()).ToArray();
    private readonly int[] _pageY = new int[PageNames.Length];
    private readonly nint[] _tracks = new nint[Defs.Length], _values = new nint[Defs.Length], _defLabels = new nint[Defs.Length];
    private bool _lookDimmed;                       // the first half of Look is covered by a picture
    private readonly HashSet<nint> _labels = new(), _hints = new(), _captions = new();
    private readonly List<nint> _tipTexts = new();
    private readonly Dictionary<Widget, Config> _snaps = new();   // each edited cluster as it was when last applied (for Cancel)

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

    /// <summary>Lets the settings window handle keys like a dialog (Tab, Enter = Apply, Esc = Cancel). Called by the message loop.</summary>
    public static bool HandleMessage(in MSG m) => _open != null && Native.IsDialogMessage(_open._hwnd, m) != 0;

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
            // the app always keeps one cluster, so deleting it empties it instead
            if (Native.MessageBox(owner, $"This is the only cluster, so it stays, but all {w.Cfg.Items.Count} of its icons will be removed.\n\nThe files and programs themselves are not touched.",
                    "Delete cluster", 0x1 | 0x30 | 0x100 /* OKCANCEL | EXCLAMATION | DEFBUTTON2 */) != 1) return;
            w.ClearItems();
            Refresh();
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
                    hbrBackground = Native.CreateSolidBrush(0xFBFBFB) /* the tab page colour, so labels and checkboxes have no visible boxes */,
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
        int px = -(int)MathF.Round(9f * _s * 96f / 72f);
        _font = Native.CreateFont(px, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _boldFont = Native.CreateFont(px, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        _tip = Native.CreateWindowEx(0x8 /* WS_EX_TOPMOST */, "tooltips_class32", "", 0x80000000 | 0x3 /* POPUP | ALWAYSTIP | NOPREFIX */, 0, 0, 0, 0, _hwnd, 0, inst, 0);
        Native.SendMessage(_tip, 0x0418 /* TTM_SETMAXTIPWIDTH */, 0, (nint)(340 * _s));
        Native.SendMessage(_tip, 0x0403 /* TTM_SETDELAYTIME */, 3 /* TTDT_INITIAL */, 450);

        // ---- header: what am I editing?
        const int h1 = 12, h2 = 48, h3 = 86;
        Header("Editing", h1);
        _clusterCombo = Ctl(-1, "COMBOBOX", "", 0x3 /* CBS_DROPDOWNLIST */ | 0x200000 | 0x10000, 76, h1 - 1, 210, 200, IdCluster,
            "Choose which cluster you are editing. Each cluster has its own icons, position and look.");
        Ctl(-1, "BUTTON", "Rename", 0x10000, 294, h1 - 2, 76, 28, IdRename, "Change this cluster's name.");
        Ctl(-1, "BUTTON", "New", 0x10000, 376, h1 - 2, 56, 28, IdNewCluster, "Create another cluster, in the middle of the screen, starting with this one's design.");
        Ctl(-1, "BUTTON", "Delete", 0x10000, 438, h1 - 2, 70, 28, IdDeleteCluster, "Delete this cluster and its list of icons. Your files and programs are not touched.");
        Header("Design", h2);
        _designCombo = Ctl(-1, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, 76, h2 - 1, 210, 200, IdDesign,
            "Pick a saved design to apply its look and layout to this cluster.");
        Ctl(-1, "BUTTON", "Save as...", 0x10000, 294, h2 - 2, 84, 28, IdSaveDesign, "Save this cluster's look and layout under a name so you can reuse it on any cluster.");
        Ctl(-1, "BUTTON", "Delete", 0x10000, 384, h2 - 2, 70, 28, IdDeleteDesign, "Delete the saved design shown in the box on the left.");
        Header("Preview", h3);
        _previewToggle = Ctl(-1, "BUTTON", "Expanded view", 0x3 /* AUTOCHECKBOX */ | 0x1000 /* PUSHLIKE */ | 0x10000, 76, h3 - 2, 120, 28, IdPreview,
            "Keeps this cluster open while you edit, so you can see changes to the expanded view. Switches off when this window closes.");
        _gearToggle = Ctl(-1, "BUTTON", "Settings icon", 0x3 | 0x1000 | 0x10000, 202, h3 - 2, 110, 28, IdGearPreview,
            "Shows the settings icon on the cluster all the time while this window is open, so you can see where you are placing it.");
        Hint(-1, "These two only affect this window.", 322, h3 + 4, 230);

        // ---- tabs
        int tabTop = 124;
        _tab = Native.CreateWindowEx(0, "SysTabControl32", "", 0x40000000 | 0x10000000 | 0x04000000 /* CHILD | VISIBLE | CLIPSIBLINGS */ | 0x10000,
            (int)(10 * _s), (int)(tabTop * _s), (int)((ClientW - 20) * _s), (int)(400 * _s), _hwnd, IdTab, inst, 0);
        Native.SendMessage(_tab, 0x30, _font, 1);
        for (int i = 0; i < PageNames.Length; i++)
        {
            fixed (char* t = PageNames[i])
            {
                var item = new TCITEM { mask = 1 /* TCIF_TEXT */, pszText = t };
                Native.SendMessage(_tab, 0x133E /* TCM_INSERTITEMW */, i, (nint)(&item));
            }
            _pageY[i] = tabTop + 42;
        }

        BuildLook();
        BuildPicture();
        BuildIcons();
        BuildSize();
        BuildBehavior();
        BuildAbout();

        int tabH = Math.Max(_pageY.Max() - tabTop + 14, 360);
        Native.SetWindowPos(_tab, 0, 0, 0, (int)((ClientW - 20) * _s), (int)(tabH * _s), Native.SWP_NOMOVE | Native.SWP_NOZORDER);

        // ---- bottom bar: status on the left, Apply / Cancel on the right
        int bar = tabTop + tabH + 12;
        _status = Ctl(-1, "STATIC", "", 0, 16, bar + 8, 250, 20, 0);
        _labels.Remove(_status); _hints.Add(_status);
        _applyBtn = Ctl(-1, "BUTTON", "Apply changes", 0x1 /* DEFPUSHBUTTON */ | 0x10000, 292, bar, 140, 32, IdApply,
            "Keep all the changes you made. Until you press this, they are only a preview and are not saved.");
        _cancelBtn = Ctl(-1, "BUTTON", "Close", 0x10000, 440, bar, 108, 32, IdCancel,
            "Undo everything since your last Apply and close this window. With nothing to undo it just closes.");

        TakeSnap(_w);
        RefreshClusters();
        RefreshDesigns(null);
        Sync();
        ShowPage(0);

        int clientH = bar + 32 + 14;
        Native.GetWindowRect(_hwnd, out RECT wr);
        Native.GetClientRect(_hwnd, out RECT cr);
        int w = (int)(ClientW * _s) + (wr.Right - wr.Left) - cr.Right, h = (int)(clientH * _s) + (wr.Bottom - wr.Top) - cr.Bottom;
        var mi = default(MONITORINFO); mi.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(default, 1), ref mi);
        int x = Math.Max(mi.rcWork.Left, (mi.rcWork.Left + mi.rcWork.Right - w) / 2), y = Math.Max(mi.rcWork.Top, (mi.rcWork.Top + mi.rcWork.Bottom - h) / 2);
        Native.SetWindowPos(_hwnd, 0 /* HWND_TOP */, x, y, w, h, 0);
        Native.ShowWindow(_hwnd, 5);
        Native.SetForegroundWindow(_hwnd);
    }

    // ---- control helpers

    private nint Ctl(int page, string cls, string text, uint style, int x, int y, int w, int h, int id, string? tip = null)
    {
        nint c = Native.CreateWindowEx(0, cls, text, 0x40000000 | 0x10000000 | style, (int)(x * _s), (int)(y * _s), (int)(w * _s), (int)(h * _s),
            _hwnd, id, Native.GetModuleHandle(0), 0);
        Native.SendMessage(c, 0x30 /* WM_SETFONT */, _font, 1);
        if (page >= 0) _pages[page].Add(c);
        if (cls == "STATIC") _labels.Add(c);
        if (tip != null) Tip(c, tip);
        return c;
    }

    private void Tip(nint control, string text)
    {
        nint buf = Marshal.StringToHGlobalUni(text);
        _tipTexts.Add(buf);
        var ti = new TOOLINFO { cbSize = (uint)sizeof(TOOLINFO), uFlags = 0x1 | 0x10 /* TTF_IDISHWND | TTF_SUBCLASS */, hwnd = _hwnd, uId = (nuint)control, lpszText = (char*)buf };
        Native.SendMessage(_tip, 0x0432 /* TTM_ADDTOOLW */, 0, (nint)(&ti));
    }

    private void Header(string text, int y) { var c = Ctl(-1, "STATIC", text, 0, 16, y + 5, 58, 20, 0); _captions.Add(c); }

    /// <summary>Small grey explanatory line.</summary>
    private void Hint(int page, string text, int x, int y, int w)
    {
        var c = Ctl(page, "STATIC", text, 0, x, y, w, 20, 0);
        _hints.Add(c);
    }

    private int NextY(int page, int height = 36) { int y = _pageY[page]; _pageY[page] += height; return y; }

    private void Section(int page, string text)
    {
        int y = NextY(page, 28);
        var c = Ctl(page, "STATIC", text, 0, PageX - 8, y + 4, 400, 20, 0);
        Native.SendMessage(c, 0x30, _boldFont, 1);
        _captions.Add(c);
    }

    private void AddDefs(int page)
    {
        for (int i = 0; i < Defs.Length; i++)
        {
            if (Defs[i].Page != page) continue;
            var d = Defs[i];
            int y = NextY(page);
            _defLabels[i] = Ctl(page, "STATIC", d.Label, 0, PageX, y + 4, LabelW, 20, 0, d.Tip);
            if (d.Kind != Kind.Slider)
            {
                Ctl(page, "BUTTON", "", 0xB /* BS_OWNERDRAW */ | 0x10000, TrackX - 4, y, 100, 28, IdBase + i, d.Tip);
                continue;
            }
            _tracks[i] = Ctl(page, "msctls_trackbar32", "", 0x10 /* TBS_NOTICKS */ | 0x10000, TrackX, y, TrackW, 28, IdBase + i, d.Tip);
            Native.SendMessage(_tracks[i], 0x407 /* TBM_SETRANGEMIN */, 0, d.Min);
            Native.SendMessage(_tracks[i], 0x408 /* TBM_SETRANGEMAX */, 0, d.Max);
            _values[i] = Ctl(page, "STATIC", "", 0x2 /* SS_RIGHT */, ValueX, y + 4, ValueW, 20, 0);
            _labels.Remove(_values[i]);   // values change, so they repaint their background (transparent text would pile up)
        }
    }

    private nint Check(int page, string text, int id, string tip, int height = 30)
    {
        int y = NextY(page, height);
        return Ctl(page, "BUTTON", text, 0x3 /* BS_AUTOCHECKBOX */ | 0x10000, PageX, y, 500, 24, id, tip);
    }

    private nint ComboRow(int page, string label, int id, string tip, int w = 280)
    {
        int y = NextY(page);
        Ctl(page, "STATIC", label, 0, PageX, y + 4, LabelW, 20, 0, tip);
        return Ctl(page, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, TrackX - 4, y, w, 200, id, tip);
    }

    // ---- pages

    private void BuildLook()
    {
        int y = NextY(PLook, 44);
        _lookNote = Ctl(PLook, "STATIC", "", 0, PageX - 8, y, 490, 38, 0);
        _labels.Remove(_lookNote);
        AddDefs(PLook);
    }

    /// <summary>The first four Look settings (blur, opacity, tint strength, tint color) paint the box underneath a picture.</summary>
    private static bool UnderPicture(Def d) => d.Page == PLook && d.Label is "Blur" or "Opacity" or "Tint strength" or "Tint color";

    /// <summary>Greys those settings out while a fully opaque picture hides them, and says why.</summary>
    private void UpdateLookDim()
    {
        var cfg = _w.Cfg;
        bool dim = cfg.BackgroundImage.Length > 0 && cfg.ImageOpacity >= 100;
        _lookDimmed = dim;
        for (int i = 0; i < Defs.Length; i++)
        {
            if (!UnderPicture(Defs[i])) continue;
            int on = dim ? 0 : 1;
            if (_tracks[i] != 0) { Native.EnableWindow(_tracks[i], on); Native.EnableWindow(_values[i], on); }
            else Native.EnableWindow(Native.GetDlgItem(_hwnd, IdBase + i), on);
            Native.InvalidateRect(_defLabels[i], 0, 1);
            if (_values[i] != 0) Native.InvalidateRect(_values[i], 0, 1);
        }
        Native.SetWindowText(_lookNote, dim ? "Greyed out: your picture covers the box, so blur, opacity and tint can't be seen. Lower the picture's opacity or remove the picture (Picture tab) to use them." : "");
        Native.InvalidateRect(_lookNote, 0, 1);
    }

    private void BuildPicture()
    {
        int y = NextY(PPicture);
        Ctl(PPicture, "STATIC", "Picture", 0, PageX, y + 6, LabelW, 20, 0, "Show a picture inside the box, on top of the tint colour.");
        Ctl(PPicture, "BUTTON", "Choose...", 0x10000, TrackX - 4, y, 110, 28, IdImgChoose, "Pick a picture to show inside the box (png, jpg, bmp, gif or tif).");
        Ctl(PPicture, "BUTTON", "Remove", 0x10000, TrackX + 112, y, 90, 28, IdImgClear, "Remove the picture and go back to the blurred wallpaper.");
        y = NextY(PPicture, 28);
        _imageName = Ctl(PPicture, "STATIC", "", 0x4000 /* SS_ENDELLIPSIS */, TrackX, y, 340, 20, 0);
        _labels.Remove(_imageName); _hints.Add(_imageName);
        AddDefs(PPicture);
    }

    private void BuildIcons()
    {
        _autoGridCheck = Check(PIcons, "Automatic grid (the box shape follows the icon count)", IdAutoGrid,
            "On: the closed box is as square as the icon count allows, and you set how many icons it shows. Off: you choose icons per row and per column.");
        AddDefs(PIcons);
        _clusterPreviewCheck = Check(PIcons, "When there are more icons, the last slot is a mini preview of the rest", IdClusterPreview,
            "On: the last closed slot shows a small 2x2 preview of the icons that did not fit. Off: it is just another icon.");
        _clickLaunchCheck = Check(PIcons, "Clicking an icon opens that app (only the preview opens the cluster)", IdClickLaunches,
            "Only matters when there are more icons than fit closed. On: a click on an icon launches it, and only the mini preview or the box edge opens the cluster. Off: any click opens the cluster.");
        int y = NextY(PIcons, 40);
        Ctl(PIcons, "STATIC", "Order", 0, PageX, y + 6, 60, 20, 0, "Sort the icons by name. You can also drag an icon onto another slot in the expanded view.");
        Ctl(PIcons, "BUTTON", "A to Z", 0x10000, TrackX - 4, y, 76, 28, IdSortAz, "Sort the icons by name, A to Z.");
        Ctl(PIcons, "BUTTON", "Z to A", 0x10000, TrackX + 78, y, 76, 28, IdSortZa, "Sort the icons by name, Z to A.");
        Ctl(PIcons, "BUTTON", "Reverse", 0x10000, TrackX + 160, y, 76, 28, IdReverse, "Reverse the current order.");
        y = NextY(PIcons, 22);
        Hint(PIcons, "Tip: in the expanded view you can also drag an icon onto another slot.", PageX, y, 480);
    }

    private void BuildSize()
    {
        AddDefs(PSize);
        _labelsCheck = Check(PSize, "Show names under the icons in the expanded view", IdLabels, "Show each icon's name under it when the cluster is expanded.");
        int y = NextY(PSize);
        Ctl(PSize, "STATIC", "Closed spacing preset", 0, PageX, y + 4, LabelW + 20, 20, 0, "Quick padding and spacing for the closed box. Padding is never smaller than spacing.");
        _presetCombo = Ctl(PSize, "COMBOBOX", "", 0x3 | 0x200000 | 0x10000, TrackX + 14, y, 180, 160, IdPreset, "Quick padding and spacing for the closed box. Padding is never smaller than spacing.");
        foreach (var p in Presets) AddString(_presetCombo, p.Name);
    }

    private void BuildBehavior()
    {
        Section(PBehavior, "Moving and opening");
        _snapCheck = Check(PBehavior, "Snap to the desktop grid when moving", IdSnap,
            "On: the box snaps to your desktop icon grid when you move it. Off: place it anywhere.");
        _expandFromCombo = ComboRow(PBehavior, "Expand from", IdExpandFrom,
            "Center: grows evenly in every direction. Top-left corner: the corner stays where it is and the box grows right and down.");
        AddString(_expandFromCombo, "Center (grows evenly all round)");
        AddString(_expandFromCombo, "Top-left corner (grows right and down)");
        Section(PBehavior, "Settings icon");
        _gearCombo = ComboRow(PBehavior, "Position", IdGear,
            "Which corner the settings icon sits in. Automatic picks a corner where it overlaps nothing.", 280);
        foreach (var c in Corners) AddString(_gearCombo, c.Name);
        AddDefs(PBehavior);
        _gearAlwaysCheck = Check(PBehavior, "Always show the settings icon (not only on hover)", IdGearAlways,
            "Keep the settings icon visible instead of showing it only while you hover the cluster.");
        Section(PBehavior, "System");
        _startupCheck = Check(PBehavior, "Start with Windows (all clusters)", IdStartup, "Start ClusterWidget when you sign in to Windows.");
        int y = NextY(PBehavior, 40);
        Ctl(PBehavior, "BUTTON", "Use this look as my default", 0x10000, PageX, y + 2, 250, 30, IdSetDefault,
            "Save this cluster's look and layout (including its picture) as the default: new clusters start with it, and Reset returns to it.");
        Ctl(PBehavior, "BUTTON", "Go back to the built-in default", 0x10000, PageX + 258, y + 2, 230, 30, IdClearDefault,
            "Forget your saved default look, so new clusters and Reset use the built-in look again.");
        y = NextY(PBehavior, 40);
        Ctl(PBehavior, "BUTTON", "Reset this cluster's look and layout", 0x10000, PageX, y + 2, 250, 30, IdReset,
            "Put this cluster's look and layout back to your default look. Its icons, position and name stay.");
    }

    private void BuildAbout()
    {
        var box = Ctl(PAbout, "EDIT", AboutText, 0x4 /* MULTILINE */ | 0x800 /* READONLY */ | 0x200000 /* VSCROLL */ | 0x40 /* AUTOVSCROLL */ | 0x800000 /* BORDER */ | 0x10000,
            PageX - 12, _pageY[PAbout], ClientW - 20 - 2 * (PageX - 12) + 10, 296, 0);
        _pageY[PAbout] += 304;
        _labels.Remove(box);
        Ctl(PAbout, "BUTTON", "Open the settings folder", 0x10000, PageX - 12, _pageY[PAbout], 200, 28, IdOpenFolder, "Open %APPDATA%\\ClusterWidget in Explorer: clusters, designs and shortcut copies.");
        Ctl(PAbout, "BUTTON", "Open this cluster's file", 0x10000, PageX + 196, _pageY[PAbout], 200, 28, IdOpenFile, "Open the .json file of the cluster you are editing, to edit it by hand.");
        _pageY[PAbout] += 34;
    }

    private void AddString(nint combo, string s)
    {
        fixed (char* p = s) Native.SendMessage(combo, 0x143 /* CB_ADDSTRING */, 0, (nint)p);
    }

    private void ShowPage(int page)
    {
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
        UpdateLookDim();
        Native.SendMessage(_gearAlwaysCheck, 0xF1, cfg.GearAlways ? 1 : 0, 0);
        Native.SendMessage(_startupCheck, 0xF1, Native.StartsWithWindows() ? 1 : 0, 0);
        Native.SetWindowText(_imageName, cfg.BackgroundImage.Length == 0 ? "(no picture)" : System.IO.Path.GetFileName(cfg.BackgroundImage));

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
        UpdateDirty();
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

    // ---- unsaved-changes tracking: Apply is only available when there is something to apply

    private static string Signature(Config c)
    {
        var d = new Config();
        d.ApplyDesign(c);
        d.Snap = c.Snap;
        return JsonSerializer.Serialize(d, ConfigJson.Default.Config);
    }

    private bool IsDirty() => _snaps.Any(kv => App.Widgets.Contains(kv.Key) && Signature(kv.Key.Cfg) != Signature(kv.Value));

    private void UpdateDirty()
    {
        bool dirty = IsDirty();
        Native.EnableWindow(_applyBtn, dirty ? 1 : 0);
        Native.SetWindowText(_cancelBtn, dirty ? "Cancel" : "Close");
        Native.SetWindowText(_status, dirty ? "Unsaved changes (live preview)" : "All changes saved");
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
        AddString(_designCombo, "Choose a saved design...");
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
        UpdateDirty();
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

    private static uint Fade(uint colorRef) // blend towards the page colour: a disabled swatch
    {
        uint r = colorRef & 0xFF, g = (colorRef >> 8) & 0xFF, b = (colorRef >> 16) & 0xFF;
        r = (r + 0xFB * 3) / 4; g = (g + 0xFB * 3) / 4; b = (b + 0xFB * 3) / 4;
        return r | (g << 8) | (b << 16);
    }

    private static uint Hue(float h, bool dim)
    {
        float x = 1 - MathF.Abs(h / 60f % 2 - 1);
        (float r, float g, float b) = (int)(h / 60f) switch { 0 => (1f, x, 0f), 1 => (x, 1f, 0f), 2 => (0f, 1f, x), 3 => (0f, x, 1f), 4 => (x, 0f, 1f), _ => (1f, 0f, x) };
        uint argb = 0xFF000000 | ((uint)(r * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(b * 255);
        if (dim) argb = (argb & 0x00FFFFFF) | 0x50000000;
        return argb;
    }

    private void DrawSwatch(DRAWITEMSTRUCT* di, uint argb, bool disabled)
    {
        RECT rc = di->rcItem;
        int h = rc.Bottom - rc.Top, wheelW = h;
        var colorRc = new RECT { Left = rc.Left, Top = rc.Top, Right = rc.Right - wheelW, Bottom = rc.Bottom };
        var wheelRc = new RECT { Left = rc.Right - wheelW, Top = rc.Top, Right = rc.Right, Bottom = rc.Bottom };
        uint cref = ColorRef(argb);
        nint fill = Native.CreateSolidBrush(disabled ? Fade(cref) : cref);
        nint wheelBg = Native.CreateSolidBrush((di->itemState & 0x1 /* ODS_SELECTED */) != 0 ? 0xDDDDDDu : 0xF3F3F3u);
        nint edge = Native.CreateSolidBrush(disabled ? 0xC8C8C8u : 0x808080u);
        Native.FillRect(di->hDC, colorRc, fill);
        Native.FillRect(di->hDC, wheelRc, wheelBg);
        Native.FrameRect(di->hDC, rc, edge);
        Native.FrameRect(di->hDC, colorRc, edge);
        Native.DeleteObject(fill); Native.DeleteObject(wheelBg); Native.DeleteObject(edge);

        // the wheel: twelve hue slices with a light hub
        if (Gdip.GdipCreateFromHDC(di->hDC, out nint g) != 0 || g == 0) return;
        Gdip.GdipSetSmoothingMode(g, 4);
        float d = h - 10 * _s / 1.25f, cx = wheelRc.Left + wheelW / 2f, cy = wheelRc.Top + h / 2f;
        d = MathF.Max(8, h - 10);
        for (int k = 0; k < 12; k++)
        {
            Gdip.GdipCreateSolidFill(Hue(k * 30f, disabled), out nint br);
            Gdip.GdipFillPie(g, br, cx - d / 2, cy - d / 2, d, d, k * 30f - 90f, 30.5f);
            Gdip.GdipDeleteBrush(br);
        }
        Gdip.GdipCreateSolidFill(disabled ? 0x50FFFFFFu : 0xFFFFFFFFu, out nint hub);
        Gdip.GdipFillEllipse(g, hub, cx - d * 0.2f, cy - d * 0.2f, d * 0.4f, d * 0.4f);
        Gdip.GdipDeleteBrush(hub);
        Gdip.GdipDeleteGraphics(g);
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
        _w.Cfg.BackgroundImage = Config.KeepPicture(new string(file)); // a copy in the app folder, so moving the original is harmless
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
        if (name == null)
        {
            Native.MessageBox(_hwnd, "Pick a saved design in the box first, then press Delete.", "Delete design", 0x40);
            return;
        }
        if (Native.MessageBox(_hwnd, $"Delete the saved design \"{name}\"?", "Delete design", 0x4 | 0x30 | 0x100) != 6) return;
        Designs.Delete(name);
        RefreshDesigns(null);
    }

    // ---------------------------------------------------------------- messages

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case 0x138: // WM_CTLCOLORSTATIC: labels sit on the tab page without their own grey box; hints and captions get their own colour
                if (lp == _status || lp == _imageName || lp == _lookNote)
                {
                    // text that changes must repaint its background, or old and new text pile up
                    if (_pageBrush == 0) _pageBrush = Native.CreateSolidBrush(0xFBFBFB);
                    Native.SetBkColor(wp, 0xFBFBFB);
                    Native.SetTextColor(wp, lp == _lookNote ? 0x2020B0u /* a warm red, BGR */ : 0x707070u);
                    return _pageBrush;
                }
                for (int i = 0; i < Defs.Length; i++)
                    if (_defLabels[i] == lp && UnderPicture(Defs[i]))
                    {
                        if (_pageBrush == 0) _pageBrush = Native.CreateSolidBrush(0xFBFBFB);
                        Native.SetBkColor(wp, 0xFBFBFB);
                        Native.SetTextColor(wp, _lookDimmed ? 0xA8A8A8u : 0x000000u);
                        return _pageBrush;
                    }
                if (_values.Contains(lp))
                {
                    for (int i = 0; i < Defs.Length; i++)
                        if (_values[i] == lp && UnderPicture(Defs[i]))
                        {
                            if (_pageBrush == 0) _pageBrush = Native.CreateSolidBrush(0xFBFBFB);
                            Native.SetBkColor(wp, 0xFBFBFB);
                            Native.SetTextColor(wp, _lookDimmed ? 0xA8A8A8u : 0x000000u);
                            return _pageBrush;
                        }
                }
                if (_labels.Contains(lp) || _hints.Contains(lp) || _captions.Contains(lp))
                {
                    Native.SetBkMode(wp, 1 /* TRANSPARENT */);
                    if (_hints.Contains(lp)) Native.SetTextColor(wp, 0x707070);
                    else if (_captions.Contains(lp)) Native.SetTextColor(wp, 0x202020);
                    return Native.GetStockObject(5 /* NULL_BRUSH */);
                }
                // trackbars and value labels ask too: paint them the colour of the tab page instead of dialog grey
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
                Sync(); // padding/spacing sliders may no longer match a preset; also refreshes the unsaved-changes state
                return 0;

            case 0x111: // WM_COMMAND
                OnCommand((int)(wp & 0xFFFF), (int)((wp >> 16) & 0xFFFF));
                if (_open != null) UpdateDirty();
                return 0;

            case 0x2B: // WM_DRAWITEM: a color swatch with a colour wheel beside it, so it reads as clickable
            {
                var di = (DRAWITEMSTRUCT*)lp;
                int index = (int)di->CtlID - IdBase;
                if (index < 0 || index >= Defs.Length) break;
                DrawSwatch(di, ColorOf(Defs[index].Kind), (di->itemState & 0x4 /* ODS_DISABLED */) != 0);
                return 1;
            }
            case 0x10: // WM_CLOSE: the window's X works like Cancel
                Cancel();
                return 0;

            case Native.WM_DESTROY:
            {
                foreach (var b in _tipTexts) Marshal.FreeHGlobal(b);
                _tipTexts.Clear();
                if (_font != 0) { Native.DeleteObject(_font); _font = 0; }
                if (_boldFont != 0) { Native.DeleteObject(_boldFont); _boldFont = 0; }
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
            case IdApply: if (IsDirty()) Apply(); break;
            case IdCancel: Cancel(); break;

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
            case IdPreview: _w.SetPreviewOpen(Native.SendMessage(_previewToggle, 0xF0, 0, 0) == 1); break;
            case IdGearPreview: _w.SetGearPreview(Native.SendMessage(_gearToggle, 0xF0, 0, 0) == 1); break;

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
            case IdImgChoose: ChooseImage(); break;
            case IdImgClear: cfg.BackgroundImage = ""; Sync(); _w.LookChanged(false); break;
            case IdSortAz: _w.SortItems(0); break;
            case IdSortZa: _w.SortItems(1); break;
            case IdReverse: _w.SortItems(2); break;

            case IdSnap: cfg.Snap = Native.SendMessage(_snapCheck, 0xF0 /* BM_GETCHECK */, 0, 0) == 1; _w.SnapToggled(); break;
            case IdLabels: cfg.ShowLabels = Native.SendMessage(_labelsCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdGearAlways: cfg.GearAlways = Native.SendMessage(_gearAlwaysCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdClusterPreview: cfg.ClusterPreview = Native.SendMessage(_clusterPreviewCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdClickLaunches: cfg.ClickLaunches = Native.SendMessage(_clickLaunchCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); break;
            case IdAutoGrid:
                if (Native.SendMessage(_autoGridCheck, 0xF0, 0, 0) == 1) { cfg.BoxCols = 0; cfg.BoxRows = 0; }
                else { var (c, r) = _w.GridDims; cfg.BoxCols = Math.Max(1, c); cfg.BoxRows = Math.Max(1, r); }
                Sync();
                _w.LookChanged(false);
                break;
            case IdStartup: Native.SetStartsWithWindows(Native.SendMessage(_startupCheck, 0xF0, 0, 0) == 1); break;
            case IdReset:
                // MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2: "No" is the default so Enter cannot reset by accident
                if (Native.MessageBox(_hwnd, $"Reset the look and layout of \"{cfg.Name}\" to your default look?\n\nIts icons and position are kept. Saved designs are not touched.",
                        "Reset settings", 0x4 | 0x20 | 0x100) == 6 /* IDYES */)
                {
                    cfg.ResetLook(); Sync(); _w.LookChanged(true);
                }
                break;
            case IdSetDefault:
                if (Native.MessageBox(_hwnd, "Use this cluster's look and layout as the default for new clusters and for Reset?\n\nThe picture, if any, is copied into the app folder. Icons and position are not included.", "Use as default", 0x4 | 0x20) == 6)
                {
                    cfg.SaveAsDefault();
                    Native.MessageBox(_hwnd, "Saved. New clusters will start with this look.", "Use as default", 0x40);
                }
                break;
            case IdClearDefault:
                if (Native.MessageBox(_hwnd, "Forget your saved default look and go back to the built-in one?\n\nExisting clusters are not changed.", "Built-in default", 0x4 | 0x20 | 0x100) == 6) Config.ClearDefault();
                break;
            case IdOpenFolder: Native.ShellExecute(_hwnd, "open", Config.RootDir, null, null, 1); break;
            case IdOpenFile: Native.ShellExecute(_hwnd, "open", cfg.SourcePath, null, null, 1); break;
        }
    }
}
