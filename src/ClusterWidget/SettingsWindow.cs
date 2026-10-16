using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>The settings window (gear icon / tray icon): sliders, swatches and presets that change the widget live.</summary>
internal sealed unsafe class SettingsWindow
{
    private enum Kind { Slider, TintSwatch, OutlineSwatch }

    private sealed record Def(string Label, Kind Kind, int Min = 0, int Max = 0, string Unit = "",
        Func<Config, int>? Get = null, Action<Config, int>? Set = null, bool Blur = false, int? AutoAt = null);

    private const int IdPreset = 300, IdSnap = 301, IdLabels = 302, IdReset = 303, IdKeepOpen = 304, IdStartup = 305, IdGear = 306, IdImgChoose = 307, IdImgClear = 308, IdBase = 100;
    private const int RowH = 40, LeftX = 16, RightX = 404;

    private static readonly Def[] Look =
    {
        new("Blur", Kind.Slider, 0, 80, "px", c => c.Blur, (c, v) => c.Blur = v, Blur: true),
        new("Opacity", Kind.Slider, 0, 100, "%", c => c.Opacity, (c, v) => c.Opacity = v),
        new("Tint strength", Kind.Slider, 0, 100, "%", c => (int)MathF.Round((Config.Color(c.Tint, 0x8C14181C) >> 24) / 2.55f),
            (c, v) => c.Tint = Config.ColorText(((uint)MathF.Round(v * 2.55f) << 24) | (Config.Color(c.Tint, 0x8C14181C) & 0xFFFFFF))),
        new("Tint color", Kind.TintSwatch),
        new("Outline color", Kind.OutlineSwatch),
        new("Outline opacity", Kind.Slider, 0, 100, "%", c => c.OutlineOpacity, (c, v) => c.OutlineOpacity = v),
        new("Outline thickness", Kind.Slider, 0, 12, "px", c => (int)MathF.Round(c.OutlineWidth), (c, v) => c.OutlineWidth = v),
        new("Corner roundness", Kind.Slider, 0, 100, "px", c => (int)MathF.Round(c.CornerRadius), (c, v) => c.CornerRadius = v),
        new("Image zoom", Kind.Slider, 100, 400, "%", c => c.ImageZoom, (c, v) => c.ImageZoom = v),
        new("Image position X", Kind.Slider, 0, 100, "%", c => c.ImagePanX, (c, v) => c.ImagePanX = v),
        new("Image position Y", Kind.Slider, 0, 100, "%", c => c.ImagePanY, (c, v) => c.ImagePanY = v),
    };

    private static readonly Def[] Layouts =
    {
        new("Icons when closed", Kind.Slider, 1, 16, "", c => c.ClosedCount, (c, v) => c.ClosedCount = v),
        new("Icons per row", Kind.Slider, 0, 12, "", c => c.BoxCols, (c, v) => c.BoxCols = v, AutoAt: 0),
        new("Icons per column", Kind.Slider, 0, 12, "", c => c.BoxRows, (c, v) => c.BoxRows = v, AutoAt: 0),
        new("Icon size", Kind.Slider, 50, 200, "%", c => (int)MathF.Round(c.IconScale * 100), (c, v) => c.IconScale = v / 100f),
        new("Padding, closed", Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.ClosedPadding), (c, v) => c.ClosedPadding = v),
        new("Spacing, closed", Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.ClosedGap), (c, v) => c.ClosedGap = v),
        new("Padding, expanded", Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.OpenPadding), (c, v) => c.OpenPadding = v),
        new("Spacing, expanded", Kind.Slider, 0, 60, "px", c => (int)MathF.Round(c.OpenGap), (c, v) => c.OpenGap = v),
        new("Settings icon size", Kind.Slider, 0, 40, "px", c => c.GearSize, (c, v) => c.GearSize = v, AutoAt: 0),
        new("Settings icon from edge, X", Kind.Slider, -1, 120, "px", c => c.GearOffsetX, (c, v) => c.GearOffsetX = v, AutoAt: -1),
        new("Settings icon from edge, Y", Kind.Slider, -1, 120, "px", c => c.GearOffsetY, (c, v) => c.GearOffsetY = v, AutoAt: -1),
    };

    // closed-box spacing presets: (name, padding, gap). Padding is never smaller than the gap, so icons never sit
    // closer to the border than to each other.
    private static readonly (string Name, float Padding, float Gap)[] Presets =
    {
        ("Compact", 12f, 6f), ("Cozy", 18f, 12f), ("Roomy", 26f, 20f),
    };

    private static readonly (string Name, string Value)[] Corners =
    {
        ("Automatic (no overlap)", "auto"), ("Top right", "topRight"), ("Top left", "topLeft"),
        ("Bottom right", "bottomRight"), ("Bottom left", "bottomLeft"),
    };

    private static SettingsWindow? _open;
    private static bool _classRegistered;

    private readonly Widget _w;
    private nint _hwnd, _font, _presetCombo, _gearCombo, _snapCheck, _labelsCheck, _keepOpenCheck, _startupCheck, _imageName;
    private float _s = 1f;
    private readonly Def[] _all;
    private readonly nint[] _tracks, _values;

    private SettingsWindow(Widget w)
    {
        _w = w;
        _all = new Def[Look.Length + Layouts.Length];
        Look.CopyTo(_all, 0);
        Layouts.CopyTo(_all, Look.Length);
        _tracks = new nint[_all.Length];
        _values = new nint[_all.Length];
    }

    public static void Show(Widget w)
    {
        w.SettingsOpened();
        if (_open != null && Native.IsWindow(_open._hwnd) != 0)
        {
            Native.ShowWindow(_open._hwnd, 9 /* SW_RESTORE */);
            Native.SetForegroundWindow(_open._hwnd);
            return;
        }
        _open = new SettingsWindow(w);
        _open.Create();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcThunk(nint h, uint m, nint w, nint l)
    {
        try { return _open != null ? _open.WndProc(h, m, w, l) : Native.DefWindowProc(h, m, w, l); }
        catch (Exception) { return Native.DefWindowProc(h, m, w, l); }
    }

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
                    hbrBackground = 16 /* COLOR_BTNFACE + 1 */,
                    lpszClassName = cls,
                };
                Native.RegisterClassEx(ref wc);
            }
            _classRegistered = true;
        }

        // WS_EX_TOOLWINDOW: no taskbar button. The app lives in the tray, not on the taskbar.
        const uint style = 0x00C00000 | 0x00080000; // WS_CAPTION | WS_SYSMENU
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, "ClusterWidgetSettings", "Cluster settings", style, 200, 200, 800, 500, 0, 0, inst, 0);
        uint dpi = Native.GetDpiForWindow(_hwnd);
        _s = dpi == 0 ? 1f : dpi / 96f;
        _font = Native.CreateFont(-(int)MathF.Round(9f * _s * 96f / 72f), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");

        Section("Look", LeftX, 12);
        Section("Layout", RightX, 12);
        for (int i = 0; i < Look.Length; i++) AddRow(i, LeftX, 40 + i * RowH);
        for (int i = 0; i < Layouts.Length; i++) AddRow(Look.Length + i, RightX, 40 + i * RowH);

        int leftBelow = 40 + Look.Length * RowH, rightBelow = 40 + Layouts.Length * RowH;
        Ctl("STATIC", "Background image", 0, LeftX, leftBelow + 6, 128, 20, 0);
        Ctl("BUTTON", "Choose...", 0x10000, LeftX + 132, leftBelow, 100, 28, IdImgChoose);
        Ctl("BUTTON", "Remove", 0x10000, LeftX + 240, leftBelow, 80, 28, IdImgClear);
        _imageName = Ctl("STATIC", "", 0x4000 /* SS_ENDELLIPSIS */, LeftX + 132, leftBelow + 34, 220, 20, 0);
        leftBelow += 62;
        _snapCheck = Ctl("BUTTON", "Snap to the desktop grid", 0x3 /* BS_AUTOCHECKBOX */ | 0x10000, LeftX, leftBelow + 4, 340, 24, IdSnap);
        _labelsCheck = Ctl("BUTTON", "Show names when expanded", 0x3 | 0x10000, LeftX, leftBelow + 34, 340, 24, IdLabels);
        _keepOpenCheck = Ctl("BUTTON", "Stay open when clicking away (for editing)", 0x3 | 0x10000, LeftX, leftBelow + 64, 360, 24, IdKeepOpen);
        _startupCheck = Ctl("BUTTON", "Start with Windows", 0x3 | 0x10000, LeftX, leftBelow + 94, 340, 24, IdStartup);

        Ctl("STATIC", "Closed spacing preset", 0, RightX, rightBelow + 4, 130, 20, 0);
        _presetCombo = Ctl("COMBOBOX", "", 0x3 /* CBS_DROPDOWNLIST */ | 0x200000 /* WS_VSCROLL */ | 0x10000, RightX + 140, rightBelow, 200, 160, IdPreset);
        foreach (var p in Presets)
            fixed (char* t = p.Name) Native.SendMessage(_presetCombo, 0x143 /* CB_ADDSTRING */, 0, (nint)t);
        Ctl("STATIC", "Settings icon position", 0, RightX, rightBelow + 44, 130, 20, 0);
        _gearCombo = Ctl("COMBOBOX", "", 0x3 | 0x200000 | 0x10000, RightX + 140, rightBelow + 40, 200, 200, IdGear);
        foreach (var c in Corners)
            fixed (char* t = c.Name) Native.SendMessage(_gearCombo, 0x143, 0, (nint)t);
        Ctl("BUTTON", "Reset all", 0x10000, RightX, rightBelow + 84, 110, 28, IdReset);
        Sync();

        int bottom = Math.Max(leftBelow + 94 + 24, rightBelow + 84 + 28) + 18;
        int clientW = (int)(796 * _s), clientH = (int)(bottom * _s);
        Native.GetWindowRect(_hwnd, out RECT wr);
        Native.GetClientRect(_hwnd, out RECT cr);
        int w = clientW + (wr.Right - wr.Left) - cr.Right, h = clientH + (wr.Bottom - wr.Top) - cr.Bottom;
        Native.SetWindowPos(_hwnd, 0 /* HWND_TOP */, 220, 120, w, h, 0);
        Native.ShowWindow(_hwnd, 5);
        Native.SetForegroundWindow(_hwnd);
    }

    private nint Ctl(string cls, string text, uint style, int x, int y, int w, int h, int id)
    {
        nint c = Native.CreateWindowEx(0, cls, text, 0x40000000 | 0x10000000 | style, (int)(x * _s), (int)(y * _s), (int)(w * _s), (int)(h * _s),
            _hwnd, id, Native.GetModuleHandle(0), 0);
        Native.SendMessage(c, 0x30 /* WM_SETFONT */, _font, 1);
        return c;
    }

    private void Section(string text, int x, int y) => Ctl("STATIC", text, 0, x, y, 200, 20, 0);

    private void AddRow(int index, int x, int y)
    {
        var d = _all[index];
        Ctl("STATIC", d.Label, 0, x, y + 4, 128, 20, 0);
        if (d.Kind != Kind.Slider)
        {
            Ctl("BUTTON", "", 0xB /* BS_OWNERDRAW */ | 0x10000, x + 132, y, 80, 28, IdBase + index);
            return;
        }
        _tracks[index] = Ctl("msctls_trackbar32", "", 0x10 /* TBS_NOTICKS */ | 0x10000, x + 128, y, 170, 28, IdBase + index);
        Native.SendMessage(_tracks[index], 0x407 /* TBM_SETRANGEMIN */, 0, d.Min);
        Native.SendMessage(_tracks[index], 0x408 /* TBM_SETRANGEMAX */, 0, d.Max);
        _values[index] = Ctl("STATIC", "", 0x2 /* SS_RIGHT */, x + 300, y + 4, 52, 20, 0);
    }

    private static string ValueText(Def d, int v) => d.AutoAt == v ? "auto" : v + d.Unit;

    /// <summary>Pushes the current config into every control.</summary>
    private void Sync()
    {
        var cfg = _w.Cfg;
        for (int i = 0; i < _all.Length; i++)
        {
            if (_all[i].Kind != Kind.Slider) continue;
            int v = _all[i].Get!(cfg);
            Native.SendMessage(_tracks[i], 0x405 /* TBM_SETPOS */, 1, v);
            Native.SetWindowText(_values[i], ValueText(_all[i], v));
        }
        Native.SendMessage(_snapCheck, 0xF1 /* BM_SETCHECK */, cfg.Snap ? 1 : 0, 0);
        Native.SendMessage(_labelsCheck, 0xF1, cfg.ShowLabels ? 1 : 0, 0);
        Native.SendMessage(_keepOpenCheck, 0xF1, cfg.KeepOpen ? 1 : 0, 0);
        Native.SendMessage(_startupCheck, 0xF1, Native.StartsWithWindows() ? 1 : 0, 0);
        Native.SetWindowText(_imageName, cfg.BackgroundImage.Length == 0 ? "(none)" : System.IO.Path.GetFileName(cfg.BackgroundImage));

        int preset = -1;
        for (int i = 0; i < Presets.Length; i++)
            if (MathF.Abs(cfg.ClosedPadding - Presets[i].Padding) < 0.5f && MathF.Abs(cfg.ClosedGap - Presets[i].Gap) < 0.5f) preset = i;
        Native.SendMessage(_presetCombo, 0x14E /* CB_SETCURSEL */, preset, 0);
        int corner = 0;
        for (int i = 0; i < Corners.Length; i++)
            if (Corners[i].Value.Equals(cfg.GearCorner, StringComparison.OrdinalIgnoreCase)) corner = i;
        Native.SendMessage(_gearCombo, 0x14E, corner, 0);
        for (int i = 0; i < _all.Length; i++)
            if (_all[i].Kind != Kind.Slider) Native.InvalidateRect(Native.GetDlgItem(_hwnd, IdBase + i), 0, 1);
    }

    private static uint ColorRef(uint argb) => ((argb >> 16) & 0xFF) | (argb & 0xFF00) | ((argb & 0xFF) << 16);
    private static uint FromColorRef(uint c, uint alpha) => (alpha << 24) | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);

    private void PickColor(Kind kind, int controlId)
    {
        var cfg = _w.Cfg;
        bool tint = kind == Kind.TintSwatch;
        uint cur = tint ? Config.Color(cfg.Tint, 0x8C14181C) : Config.Color(cfg.OutlineColor, 0xFF4CC2FF);
        uint* custom = stackalloc uint[16];
        for (int i = 0; i < 16; i++) custom[i] = 0xFFFFFF;
        var cc = new CHOOSECOLOR
        {
            lStructSize = (uint)sizeof(CHOOSECOLOR), hwndOwner = _hwnd, rgbResult = ColorRef(cur),
            lpCustColors = (nint)custom, Flags = 0x1 | 0x2 /* CC_RGBINIT | CC_FULLOPEN */,
        };
        if (Native.ChooseColor(ref cc) == 0) return;
        uint argb = FromColorRef(cc.rgbResult, cur >> 24);
        if (tint) cfg.Tint = Config.ColorText(argb); else cfg.OutlineColor = Config.ColorText(argb);
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

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case 0x114: // WM_HSCROLL
                for (int i = 0; i < _all.Length; i++)
                {
                    if (_tracks[i] == 0 || _tracks[i] != lp) continue;
                    int pos = (int)Native.SendMessage(lp, 0x400 /* TBM_GETPOS */, 0, 0);
                    _all[i].Set!(_w.Cfg, pos);
                    Native.SetWindowText(_values[i], ValueText(_all[i], pos));
                    _w.LookChanged(_all[i].Blur);
                    break;
                }
                Sync(); // padding/spacing sliders may no longer match a preset
                return 0;

            case 0x111: // WM_COMMAND
            {
                int id = (int)(wp & 0xFFFF), code = (int)((wp >> 16) & 0xFFFF);
                var cfg = _w.Cfg;
                if (id >= IdBase && id < IdBase + _all.Length && _all[id - IdBase].Kind != Kind.Slider) PickColor(_all[id - IdBase].Kind, id);
                else if (id == IdPreset && code == 1 /* CBN_SELCHANGE */)
                {
                    int sel = (int)Native.SendMessage(_presetCombo, 0x147 /* CB_GETCURSEL */, 0, 0);
                    if (sel >= 0 && sel < Presets.Length)
                    {
                        cfg.ClosedPadding = Presets[sel].Padding; cfg.ClosedGap = Presets[sel].Gap;
                        Sync();
                        _w.LookChanged(false);
                    }
                }
                else if (id == IdGear && code == 1)
                {
                    int sel = (int)Native.SendMessage(_gearCombo, 0x147, 0, 0);
                    if (sel >= 0 && sel < Corners.Length) { cfg.GearCorner = Corners[sel].Value; _w.LookChanged(false); }
                }
                else if (id == IdSnap) { cfg.Snap = Native.SendMessage(_snapCheck, 0xF0 /* BM_GETCHECK */, 0, 0) == 1; _w.SnapToggled(); }
                else if (id == IdLabels) { cfg.ShowLabels = Native.SendMessage(_labelsCheck, 0xF0, 0, 0) == 1; _w.LookChanged(false); }
                else if (id == IdKeepOpen)
                {
                    cfg.KeepOpen = Native.SendMessage(_keepOpenCheck, 0xF0, 0, 0) == 1;
                    _w.LookChanged(false);
                    _w.SettingsOpened(); // turning it on opens the cluster so there is something to look at
                }
                else if (id == IdImgChoose) { ChooseImage(); }
                else if (id == IdImgClear) { cfg.BackgroundImage = ""; Sync(); _w.LookChanged(false); }
                else if (id == IdStartup) Native.SetStartsWithWindows(Native.SendMessage(_startupCheck, 0xF0, 0, 0) == 1);
                else if (id == IdReset)
                {
                    // MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2: "No" is the default so Enter cannot reset by accident
                    if (Native.MessageBox(_hwnd, "Reset all look and layout settings to their defaults?\n\nYour icons and the cluster's position are kept.",
                            "Reset settings", 0x4 | 0x20 | 0x100) == 6 /* IDYES */)
                    {
                        cfg.ResetLook(); Sync(); _w.LookChanged(true);
                    }
                }
                return 0;
            }

            case 0x2B: // WM_DRAWITEM: the color swatch buttons
            {
                var di = (DRAWITEMSTRUCT*)lp;
                int index = (int)di->CtlID - IdBase;
                if (index < 0 || index >= _all.Length) break;
                uint argb = _all[index].Kind == Kind.TintSwatch ? Config.Color(_w.Cfg.Tint, 0x8C14181C) : Config.Color(_w.Cfg.OutlineColor, 0xFF4CC2FF);
                nint fill = Native.CreateSolidBrush(ColorRef(argb)), edge = Native.CreateSolidBrush(0x808080);
                Native.FillRect(di->hDC, di->rcItem, fill);
                Native.FrameRect(di->hDC, di->rcItem, edge);
                Native.DeleteObject(fill); Native.DeleteObject(edge);
                return 1;
            }

            case 0x10: // WM_CLOSE
                Native.DestroyWindow(h);
                return 0;

            case Native.WM_DESTROY:
                if (_font != 0) { Native.DeleteObject(_font); _font = 0; }
                _open = null;
                return 0; // closing settings must not quit the widget
        }
        return Native.DefWindowProc(h, msg, wp, lp);
    }
}
