using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>The settings window behind the small gear icon: sliders and color swatches that change the widget live.</summary>
internal sealed unsafe class SettingsWindow
{
    private const int IdBlur = 100, IdOpacity = 101, IdTintA = 102, IdTint = 103, IdOutline = 104, IdThick = 105, IdRadius = 106,
        IdIcon = 107, IdLabels = 108, IdReset = 109;
    private const int Rows = 8; // slider-or-swatch rows, ids 100..107

    private static SettingsWindow? _open;
    private static bool _classRegistered;

    private readonly Widget _w;
    private nint _hwnd, _font;
    private float _s = 1f;
    private readonly nint[] _tracks = new nint[Rows], _values = new nint[Rows];
    private nint _labelsCheck;

    private SettingsWindow(Widget w) { _w = w; }

    public static void Show(Widget w)
    {
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

        const uint style = 0x00C00000 | 0x00080000 | 0x00020000; // WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX
        _hwnd = Native.CreateWindowEx(0, "ClusterWidgetSettings", "Cluster settings", style, 200, 200, 400, 500, 0, 0, inst, 0);
        uint dpi = Native.GetDpiForWindow(_hwnd);
        _s = dpi == 0 ? 1f : dpi / 96f;
        _font = Native.CreateFont(-(int)MathF.Round(9f * _s * 96f / 72f), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");

        Row(0, "Blur", IdBlur, 0, 80, _w.Cfg.Blur, "px");
        Row(1, "Opacity", IdOpacity, 0, 100, _w.Cfg.Opacity, "%");
        Row(2, "Tint strength", IdTintA, 0, 100, TintPercent(), "%");
        Swatch(3, "Tint color", IdTint);
        Swatch(4, "Outline color", IdOutline);
        Row(5, "Outline thickness", IdThick, 0, 12, (int)MathF.Round(_w.Cfg.OutlineWidth), "px");
        Row(6, "Corner roundness", IdRadius, 0, 100, (int)MathF.Round(_w.Cfg.CornerRadius), "px");
        Row(7, "Icon size", IdIcon, 50, 200, (int)MathF.Round(_w.Cfg.IconScale * 100), "%");

        _labelsCheck = Ctl("BUTTON", "Show names when expanded", 0x3 /* BS_AUTOCHECKBOX */ | 0x10000 /* WS_TABSTOP */, 16, 16 + Rows * 40, 340, 24, IdLabels);
        Native.SendMessage(_labelsCheck, 0xF1 /* BM_SETCHECK */, _w.Cfg.ShowLabels ? 1 : 0, 0);
        Ctl("BUTTON", "Reset look", 0x10000, 16, 16 + Rows * 40 + 40, 110, 28, IdReset);

        int clientW = (int)(380 * _s), clientH = (int)((16 + Rows * 40 + 40 + 28 + 16) * _s);
        Native.GetWindowRect(_hwnd, out RECT wr);
        Native.GetClientRect(_hwnd, out RECT cr);
        int w = clientW + (wr.Right - wr.Left) - cr.Right, h = clientH + (wr.Bottom - wr.Top) - cr.Bottom;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 220, 160, w, h, Native.SWP_NOACTIVATE);
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

    private void Row(int index, string label, int id, int min, int max, int value, string unit)
    {
        int y = 16 + index * 40;
        Ctl("STATIC", label, 0, 16, y + 4, 120, 20, 0);
        _tracks[index] = Ctl("msctls_trackbar32", "", 0x10 /* TBS_NOTICKS */ | 0x10000, 140, y, 170, 28, id);
        Native.SendMessage(_tracks[index], 0x406 /* TBM_SETRANGE */, 1, (nint)((max << 16) | (min & 0xFFFF)));
        Native.SendMessage(_tracks[index], 0x405 /* TBM_SETPOS */, 1, value);
        _values[index] = Ctl("STATIC", value + unit, 0x2 /* SS_RIGHT */, 316, y + 4, 52, 20, 0);
        _units[index] = unit;
    }

    private readonly string[] _units = new string[Rows];

    private void Swatch(int index, string label, int id)
    {
        int y = 16 + index * 40;
        Ctl("STATIC", label, 0, 16, y + 4, 120, 20, 0);
        Ctl("BUTTON", "", 0xB /* BS_OWNERDRAW */ | 0x10000, 140, y, 80, 28, id);
    }

    private int TintPercent() => (int)MathF.Round((Config.Color(_w.Cfg.Tint, 0x8C14181C) >> 24) / 2.55f);

    private static uint ColorRef(uint argb) => ((argb >> 16) & 0xFF) | (argb & 0xFF00) | ((argb & 0xFF) << 16);
    private static uint FromColorRef(uint c, uint alpha) => (alpha << 24) | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);

    private void PickColor(int id)
    {
        var cfg = _w.Cfg;
        uint cur = id == IdTint ? Config.Color(cfg.Tint, 0x8C14181C) : Config.Color(cfg.OutlineColor, 0xFF4CC2FF);
        uint* custom = stackalloc uint[16];
        for (int i = 0; i < 16; i++) custom[i] = 0xFFFFFF;
        var cc = new CHOOSECOLOR
        {
            lStructSize = (uint)sizeof(CHOOSECOLOR), hwndOwner = _hwnd, rgbResult = ColorRef(cur),
            lpCustColors = (nint)custom, Flags = 0x1 | 0x2 /* CC_RGBINIT | CC_FULLOPEN */,
        };
        if (Native.ChooseColor(ref cc) == 0) return;
        uint argb = FromColorRef(cc.rgbResult, cur >> 24);
        if (id == IdTint) cfg.Tint = Config.ColorText(argb); else cfg.OutlineColor = Config.ColorText(argb);
        Native.InvalidateRect(Native.GetDlgItem(_hwnd, id), 0, 1);
        _w.LookChanged(false);
    }

    private void OnSlider(int index, nint trackbar)
    {
        int pos = (int)Native.SendMessage(trackbar, 0x400 /* TBM_GETPOS */, 0, 0);
        Native.SetWindowText(_values[index], pos + _units[index]);
        var cfg = _w.Cfg;
        bool blur = false;
        switch (index)
        {
            case 0: cfg.Blur = pos; blur = true; break;
            case 1: cfg.Opacity = pos; break;
            case 2:
                uint tint = Config.Color(cfg.Tint, 0x8C14181C);
                cfg.Tint = Config.ColorText(((uint)MathF.Round(pos * 2.55f) << 24) | (tint & 0xFFFFFF));
                break;
            case 5: cfg.OutlineWidth = pos; break;
            case 6: cfg.CornerRadius = pos; break;
            case 7: cfg.IconScale = pos / 100f; break;
        }
        _w.LookChanged(blur);
    }

    private void ResetAll()
    {
        _w.Cfg.ResetLook();
        var cfg = _w.Cfg;
        int[] vals = { cfg.Blur, cfg.Opacity, TintPercent(), 0, 0, (int)cfg.OutlineWidth, (int)cfg.CornerRadius, (int)MathF.Round(cfg.IconScale * 100) };
        for (int i = 0; i < Rows; i++)
        {
            if (_tracks[i] == 0) continue;
            Native.SendMessage(_tracks[i], 0x405, 1, vals[i]);
            Native.SetWindowText(_values[i], vals[i] + _units[i]);
        }
        Native.SendMessage(_labelsCheck, 0xF1, cfg.ShowLabels ? 1 : 0, 0);
        Native.InvalidateRect(Native.GetDlgItem(_hwnd, IdTint), 0, 1);
        Native.InvalidateRect(Native.GetDlgItem(_hwnd, IdOutline), 0, 1);
        _w.LookChanged(true);
    }

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case 0x114: // WM_HSCROLL
                for (int i = 0; i < Rows; i++)
                    if (_tracks[i] != 0 && _tracks[i] == lp) { OnSlider(i, lp); break; }
                return 0;

            case 0x111: // WM_COMMAND
            {
                int id = (int)(wp & 0xFFFF);
                if (id == IdTint || id == IdOutline) PickColor(id);
                else if (id == IdReset) ResetAll();
                else if (id == IdLabels)
                {
                    _w.Cfg.ShowLabels = Native.SendMessage(_labelsCheck, 0xF0 /* BM_GETCHECK */, 0, 0) == 1;
                    _w.LookChanged(false);
                }
                return 0;
            }

            case 0x2B: // WM_DRAWITEM: the color swatch buttons
            {
                var di = (DRAWITEMSTRUCT*)lp;
                uint argb = di->CtlID == IdTint ? Config.Color(_w.Cfg.Tint, 0x8C14181C) : Config.Color(_w.Cfg.OutlineColor, 0xFF4CC2FF);
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
