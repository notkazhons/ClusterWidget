using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>Reads the desktop icon grid the shell uses, so the widget snaps to the same cells.</summary>
internal static class DesktopGrid
{
    public static (int W, int H) CellSize()
    {
        nint lv = FindListView();
        if (lv != 0)
        {
            const uint LVM_GETITEMSPACING = 0x1033; // wParam 0 = large-icon spacing
            nint r = Native.SendMessage(lv, LVM_GETITEMSPACING, 0, 0);
            int cx = (int)(r & 0xFFFF), cy = (int)((r >> 16) & 0xFFFF);
            if (cx > 16 && cy > 16) return (cx, cy);
        }
        return (Math.Max(Native.GetSystemMetrics(38), 48), Math.Max(Native.GetSystemMetrics(39), 48)); // SM_CX/CYICONSPACING
    }

    private static nint FindListView()
    {
        nint def = Native.FindWindowEx(Native.FindWindow("Progman", null), 0, "SHELLDLL_DefView", null);
        for (nint w = 0; def == 0 && (w = Native.FindWindowEx(0, w, "WorkerW", null)) != 0;)
            def = Native.FindWindowEx(w, 0, "SHELLDLL_DefView", null);
        return def == 0 ? 0 : Native.FindWindowEx(def, 0, "SysListView32", null);
    }
}

/// <summary>The widget window: acrylic backdrop, GDI+ drawing into a premultiplied DIB, event-driven (timers only while animating).</summary>
internal sealed unsafe class Widget
{
    private const nuint TimerAnim = 1, TimerReload = 2;
    private const uint WM_CONFIG_CHANGED = Native.WM_APP + 1;

    private sealed class Slot
    {
        public Item Item = null!;
        public IconImage? Image;
        public bool Failed;
    }

    private static Widget _self = null!;

    private Config _cfg;
    private nint _hwnd;
    private nuint _gdipToken;
    private readonly List<Slot> _slots = new();
    private FileSystemWatcher? _watcher;
    private long _lastSave;

    // grid + geometry (physical pixels)
    private int _ox, _oy, _cw, _ch;
    private float _scale = 1f;
    private CellRect _bounds, _anchor, _expandedCells;
    private int _tileCells, _cols, _rows, _iconPx;
    private RECT _pa, _pe; // collapsed / expanded window rects, screen coords

    // animation + input state
    private float _t;       // 0 collapsed .. 1 expanded
    private int _target;
    private float _animFrom;
    private long _animStart;
    private bool _pressed, _dragging, _tracking, _inMenu, _allowZ;
    private POINT _downPt, _grab;
    private int _downTile, _hover = -1;

    // drawing resources
    private nint _memDc, _dib, _bmp, _g;
    private int _canvasW, _canvasH;
    private nint _fontFamily, _font, _strFmt, _brushLabel, _brushHover;
    private float _fontEm;
    private uint _outlineArgb, _hoverArgb, _labelArgb;

    public Widget(Config cfg) { _cfg = cfg; _self = this; }

    public void Run()
    {
        Gdip.GdiplusStartup(out _gdipToken, new GdiplusStartupInput { GdiplusVersion = 1 }, 0);
        Native.CoInitializeEx(0, 2 /* apartment-threaded */);
        CreateWindow();
        LoadSlots();
        ApplyStyle();
        Recompute();
        ApplyWindow();
        Render();
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        SendToBottom();
        Present();
        StartWatcher();
        Native.EmptyWorkingSet(Native.GetCurrentProcess());

        while (Native.GetMessage(out MSG m, 0, 0, 0) > 0) { Native.TranslateMessage(m); Native.DispatchMessage(m); }

        _watcher?.Dispose();
        FreeCanvas();
        FreeStyle();
        Gdip.GdiplusShutdown(_gdipToken);
    }

    // ---------------------------------------------------------------- window setup

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcThunk(nint h, uint m, nint w, nint l)
    {
        try { return _self.WndProc(h, m, w, l); }
        catch (Exception) { return Native.DefWindowProc(h, m, w, l); }
    }

    private void CreateWindow()
    {
        nint inst = Native.GetModuleHandle(0);
        fixed (char* cls = "ClusterWidgetWindow")
        {
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)sizeof(WNDCLASSEX),
                lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProcThunk,
                hInstance = inst,
                hCursor = Native.LoadCursor(0, 32512 /* IDC_ARROW */),
                lpszClassName = cls,
            };
            Native.RegisterClassEx(ref wc);
        }
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW | Native.WS_EX_ACCEPTFILES, "ClusterWidgetWindow", "ClusterWidget",
            Native.WS_POPUP, 0, 0, 100, 100, 0, 0, inst, 0);
        Native.DragAcceptFiles(_hwnd, 1);

        // Let DWM own the whole surface so pixels we leave at alpha 0 show the acrylic; draw our own corners/border.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        int v = 1; Native.DwmSetWindowAttribute(_hwnd, 33 /* WINDOW_CORNER_PREFERENCE */, ref v, 4); // DONOTROUND
        v = unchecked((int)0xFFFFFFFE); Native.DwmSetWindowAttribute(_hwnd, 34 /* BORDER_COLOR */, ref v, 4); // none
        ApplyAcrylic();
    }

    private void ApplyAcrylic()
    {
        // "acrylic": real acrylic, DWM rounds the corners (fixed system radius). "blur": lighter blur that obeys a
        // custom corner radius through the window region.
        _useRegion = _cfg.Backdrop.Equals("blur", StringComparison.OrdinalIgnoreCase);
        int corner = _useRegion ? 1 /* DONOTROUND */ : 2 /* ROUND */;
        Native.DwmSetWindowAttribute(_hwnd, 33 /* WINDOW_CORNER_PREFERENCE */, ref corner, 4);
        uint argb = Config.Color(_cfg.Tint, 0x9E14181C);
        uint abgr = (argb & 0xFF00FF00) | ((argb & 0xFF) << 16) | ((argb >> 16) & 0xFF);
        var accent = new ACCENT_POLICY { AccentState = _useRegion ? 3 /* BLURBEHIND */ : 4 /* ACRYLICBLURBEHIND */, AccentFlags = 0, GradientColor = abgr };
        var data = new WINCOMPATTRDATA { Attrib = 19, pvData = (nint)(&accent), cbData = sizeof(ACCENT_POLICY) };
        Native.SetWindowCompositionAttribute(_hwnd, ref data);
    }

    private void SendToBottom()
    {
        _allowZ = true;
        Native.SetWindowPos(_hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _allowZ = false;
    }

    // ---------------------------------------------------------------- config / slots

    private void LoadSlots()
    {
        foreach (var s in _slots) s.Image?.Dispose();
        _slots.Clear();
        foreach (var it in _cfg.Items) _slots.Add(new Slot { Item = it });
    }

    private void ReloadConfig()
    {
        _cfg = Config.Load();
        LoadSlots();
        ApplyAcrylic();
        ApplyStyle();
        Recompute();
        Frame();
    }

    private void SaveConfig()
    {
        _lastSave = Environment.TickCount64;
        _cfg.Save();
    }

    private void StartWatcher()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);
            _watcher = new FileSystemWatcher(Config.Dir, "config.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
            _watcher.Changed += (_, _) => Native.PostMessage(_hwnd, WM_CONFIG_CHANGED, 0, 0);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { /* hot reload is optional; the menu entry still works */ }
    }

    // ---------------------------------------------------------------- geometry

    private void Recompute()
    {
        (_cw, _ch) = DesktopGrid.CellSize();
        uint dpi = Native.GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1f : dpi / 96f;
        var primary = default(MONITORINFO); primary.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(default, 1), ref primary);
        _ox = primary.rcWork.Left; _oy = primary.rcWork.Top;
        Relayout();
    }

    /// <summary>Cheap path used while dragging: reuses the cached grid, refreshes bounds, anchor and rects.</summary>
    private void Relayout()
    {
        _tileCells = _cfg.TileCells;
        int cc = _tileCells * 2;
        var center = new POINT { X = _ox + (_cfg.X + cc / 2) * _cw, Y = _oy + (_cfg.Y + cc / 2) * _ch };
        var mi = default(MONITORINFO); mi.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(center, 1), ref mi);
        int bx = (int)Math.Ceiling((mi.rcWork.Left - _ox) / (double)_cw), by = (int)Math.Ceiling((mi.rcWork.Top - _oy) / (double)_ch);
        int bw = (int)Math.Floor((mi.rcWork.Right - _ox) / (double)_cw) - bx, bh = (int)Math.Floor((mi.rcWork.Bottom - _oy) / (double)_ch) - by;
        _bounds = new CellRect(bx, by, Math.Max(bw, cc), Math.Max(bh, cc));

        (_cols, _rows) = Layout.TileGrid(_slots.Count, _bounds.W / _tileCells, _bounds.H / _tileCells);
        _anchor = Layout.Clamp(new CellRect(_cfg.X, _cfg.Y, cc, cc), _bounds);
        _cfg.X = _anchor.X; _cfg.Y = _anchor.Y;
        _expandedCells = Layout.Expand(_anchor, _cols, _rows, _tileCells, _bounds);
        _pa = PixelRect(_anchor);
        _pe = PixelRect(_expandedCells);

        int px = (int)(Math.Min(_tileCells * _cw, _tileCells * _ch) * _cfg.IconRatio);
        if (px != _iconPx) { _iconPx = px; foreach (var s in _slots) { s.Image?.Dispose(); s.Image = null; s.Failed = false; } }
        EnsureCanvas(_pe.Right - _pe.Left, _pe.Bottom - _pe.Top);
        EnsureFont();
    }

    private RECT PixelRect(CellRect c)
    {
        int gap = (int)(_cfg.Gap * _scale);
        return new RECT
        {
            Left = _ox + c.X * _cw + gap, Top = _oy + c.Y * _ch + gap,
            Right = _ox + c.Right * _cw - gap, Bottom = _oy + c.Bottom * _ch - gap,
        };
    }

    private RECT Current() => new()
    {
        Left = Lerp(_pa.Left, _pe.Left, _t), Top = Lerp(_pa.Top, _pe.Top, _t),
        Right = Lerp(_pa.Right, _pe.Right, _t), Bottom = Lerp(_pa.Bottom, _pe.Bottom, _t),
    };

    private static int Lerp(int a, int b, float t) => (int)MathF.Round(a + (b - a) * t);
    private static float LerpF(float a, float b, float t) => a + (b - a) * t;

    private void ApplyWindow()
    {
        RECT r = Current();
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Native.SetWindowPos(_hwnd, 0, r.Left, r.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOCOPYBITS | Native.SWP_NOREDRAW);
        if (!_useRegion) return;
        int d = (int)Radius * 2;
        Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d), 0);
    }

    private bool _useRegion;
    private float Radius => (_useRegion ? _cfg.CornerRadius : 8f) * _scale;

    // ---------------------------------------------------------------- tiles

    private int ShownCount => Math.Min(_slots.Count, _cols * _rows);
    private int VisibleSlots => _t <= 0.001f ? Layout.VisibleSlots : Math.Max(ShownCount, Layout.VisibleSlots);

    /// <summary>Tile rect in canvas coordinates (origin = expanded window's top-left) at expansion <paramref name="t"/>.</summary>
    private (float X, float Y, float W, float H) TileRect(int i, float t)
    {
        int tw = _tileCells * _cw, th = _tileCells * _ch;
        float ex = _ox + (_expandedCells.X + i % _cols * _tileCells) * _cw - _pe.Left;
        float ey = _oy + (_expandedCells.Y + i / _cols * _tileCells) * _ch - _pe.Top;
        if (i >= Layout.VisibleSlots) return (ex, ey, tw, th);
        float cx = _ox + (_anchor.X + i % 2 * _tileCells) * _cw - _pe.Left;
        float cy = _oy + (_anchor.Y + i / 2 * _tileCells) * _ch - _pe.Top;
        return (LerpF(cx, ex, t), LerpF(cy, ey, t), tw, th);
    }

    private int HitTile(int canvasX, int canvasY)
    {
        for (int i = 0; i < VisibleSlots; i++)
        {
            var r = TileRect(i, _t);
            if (canvasX >= r.X && canvasX < r.X + r.W && canvasY >= r.Y && canvasY < r.Y + r.H) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- drawing

    private void EnsureCanvas(int w, int h)
    {
        if (_g != 0 && w == _canvasW && h == _canvasH) return;
        FreeCanvas();
        _canvasW = w; _canvasH = h;
        var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint screen = Native.GetDC(0);
        _memDc = Native.CreateCompatibleDC(screen);
        _dib = Native.CreateDIBSection(screen, ref bi, 0, out nint bits, 0, 0);
        Native.ReleaseDC(0, screen);
        Native.SelectObject(_memDc, _dib);
        // GDI+ draws straight into the DIB's memory as premultiplied ARGB, so alpha reaches DWM intact.
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, bits, out _bmp);
        Gdip.GdipGetImageGraphicsContext(_bmp, out _g);
        Gdip.GdipSetSmoothingMode(_g, 4);        // anti-alias
        Gdip.GdipSetTextRenderingHint(_g, 4);    // anti-alias text
        Gdip.GdipSetInterpolationMode(_g, 7);    // high-quality bicubic
        Gdip.GdipSetPixelOffsetMode(_g, 2);      // high quality
    }

    private void FreeCanvas()
    {
        if (_g != 0) { Gdip.GdipDeleteGraphics(_g); _g = 0; }
        if (_bmp != 0) { Gdip.GdipDisposeImage(_bmp); _bmp = 0; }
        if (_dib != 0) { Native.DeleteObject(_dib); _dib = 0; }
        if (_memDc != 0) { Native.DeleteDC(_memDc); _memDc = 0; }
    }

    private void ApplyStyle()
    {
        FreeStyle();
        _outlineArgb = Config.Color(_cfg.OutlineColor, 0xFF4CC2FF);
        _hoverArgb = Config.Color(_cfg.HoverColor, 0x2EFFFFFF);
        _labelArgb = Config.Color(_cfg.LabelColor, 0xFFF2F2F2);
        Gdip.GdipCreateSolidFill(_labelArgb, out _brushLabel);
        Gdip.GdipCreateSolidFill(_hoverArgb, out _brushHover);
        Gdip.GdipCreateFontFamilyFromName(_cfg.FontFamily, 0, out _fontFamily);
        if (_fontFamily == 0) Gdip.GdipCreateFontFamilyFromName("Segoe UI", 0, out _fontFamily);
        Gdip.GdipCreateStringFormat(0x4000 /* NoWrap */, 0, out _strFmt);
        Gdip.GdipSetStringFormatAlign(_strFmt, 1);
        Gdip.GdipSetStringFormatLineAlign(_strFmt, 0);
        Gdip.GdipSetStringFormatTrimming(_strFmt, 3); // ellipsis at character
        _fontEm = 0;
    }

    private void FreeStyle()
    {
        if (_font != 0) { Gdip.GdipDeleteFont(_font); _font = 0; }
        if (_fontFamily != 0) { Gdip.GdipDeleteFontFamily(_fontFamily); _fontFamily = 0; }
        if (_strFmt != 0) { Gdip.GdipDeleteStringFormat(_strFmt); _strFmt = 0; }
        if (_brushLabel != 0) { Gdip.GdipDeleteBrush(_brushLabel); _brushLabel = 0; }
        if (_brushHover != 0) { Gdip.GdipDeleteBrush(_brushHover); _brushHover = 0; }
    }

    private void EnsureFont()
    {
        float em = MathF.Max(8f, MathF.Min(_tileCells * _cw, _tileCells * _ch) * _cfg.LabelSize);
        if (_font != 0 && MathF.Abs(em - _fontEm) < 0.5f) return;
        if (_font != 0) Gdip.GdipDeleteFont(_font);
        Gdip.GdipCreateFont(_fontFamily, em, 0, 2 /* pixel */, out _font);
        _fontEm = em;
    }

    private static nint RoundRect(float x, float y, float w, float h, float r)
    {
        r = MathF.Max(0.5f, MathF.Min(r, MathF.Min(w, h) / 2));
        float d = r * 2;
        Gdip.GdipCreatePath(0, out nint p);
        Gdip.GdipAddPathArc(p, x, y, d, d, 180, 90);
        Gdip.GdipAddPathArc(p, x + w - d, y, d, d, 270, 90);
        Gdip.GdipAddPathArc(p, x + w - d, y + h - d, d, d, 0, 90);
        Gdip.GdipAddPathArc(p, x, y + h - d, d, d, 90, 90);
        Gdip.GdipClosePathFigure(p);
        return p;
    }

    private void Render()
    {
        if (_g == 0) return;
        Gdip.GdipGraphicsClear(_g, 0);
        RECT cur = Current();
        float cx = cur.Left - _pe.Left, cy = cur.Top - _pe.Top, cwid = cur.Right - cur.Left, chei = cur.Bottom - cur.Top;
        float radius = Radius;

        // outline, inset so the stroke stays inside the rounded window region
        float ow = MathF.Max(0f, _cfg.OutlineWidth * _scale);
        if (ow > 0)
        {
            Gdip.GdipCreatePen1(_outlineArgb, ow, 2, out nint pen);
            nint path = RoundRect(cx + ow / 2, cy + ow / 2, cwid - ow, chei - ow, radius - ow / 2);
            Gdip.GdipDrawPath(_g, pen, path);
            Gdip.GdipDeletePath(path);
            Gdip.GdipDeletePen(pen);
        }

        int visible = VisibleSlots;
        for (int i = 0; i < visible; i++)
        {
            var r = TileRect(i, _t);
            if (i >= _slots.Count) { DrawEmptySlot(r); continue; }
            DrawTile(i, r);
        }
    }

    private void DrawEmptySlot((float X, float Y, float W, float H) r)
    {
        float s = MathF.Min(r.W, r.H) * 0.12f, mx = r.X + r.W / 2, my = r.Y + r.H / 2;
        Gdip.GdipCreatePen1((_outlineArgb & 0x00FFFFFF) | 0x50000000, MathF.Max(1.5f, 2f * _scale), 2, out nint pen);
        Gdip.GdipDrawLine(_g, pen, mx - s, my, mx + s, my);
        Gdip.GdipDrawLine(_g, pen, mx, my - s, mx, my + s);
        Gdip.GdipDeletePen(pen);
    }

    private void DrawTile(int i, (float X, float Y, float W, float H) r)
    {
        if (i == _hover)
        {
            float inset = 6 * _scale;
            nint hp = RoundRect(r.X + inset, r.Y + inset, r.W - inset * 2, r.H - inset * 2, Radius * 0.6f);
            Gdip.GdipFillPath(_g, _brushHover, hp);
            Gdip.GdipDeletePath(hp);
        }

        float labelH = _cfg.ShowLabels ? _fontEm * 1.5f : 0;
        float iconTop = r.Y + (r.H - _iconPx - labelH) / 2;
        float iconLeft = r.X + (r.W - _iconPx) / 2;
        var img = GetImage(_slots[i]);
        if (img != 0) Gdip.GdipDrawImageRectI(_g, img, (int)iconLeft, (int)iconTop, _iconPx, _iconPx);
        else
        {
            nint ph = RoundRect(iconLeft, iconTop, _iconPx, _iconPx, _iconPx * 0.22f);
            Gdip.GdipFillPath(_g, _brushHover, ph);
            Gdip.GdipDeletePath(ph);
        }

        if (_cfg.ShowLabels)
        {
            var rc = new RectF { X = r.X + 6 * _scale, Y = iconTop + _iconPx + 3 * _scale, Width = r.W - 12 * _scale, Height = labelH };
            string name = _slots[i].Item.DisplayName;
            Gdip.GdipDrawString(_g, name, name.Length, _font, rc, _strFmt, _brushLabel);
        }
    }

    private nint GetImage(Slot s)
    {
        if (s.Image == null && !s.Failed)
        {
            s.Image = IconImage.Load(s.Item.Path, Math.Max(16, _iconPx));
            s.Failed = s.Image == null;
        }
        return s.Image?.Bitmap ?? 0;
    }

    private void Present()
    {
        if (_memDc == 0) return;
        RECT cur = Current();
        nint dc = Native.GetDC(_hwnd);
        Native.BitBlt(dc, 0, 0, cur.Right - cur.Left, cur.Bottom - cur.Top, _memDc, cur.Left - _pe.Left, cur.Top - _pe.Top, Native.SRCCOPY);
        Native.ReleaseDC(_hwnd, dc);
    }

    private void Frame() { ApplyWindow(); Render(); Present(); }

    // ---------------------------------------------------------------- expand / collapse

    private void StartAnim(int target)
    {
        if (_target == target && !(_t > 0f && _t < 1f)) { if (_t == target) return; }
        _target = target;
        _animFrom = _t;
        _animStart = Environment.TickCount64;
        if (_cfg.AnimationMs <= 0) { _t = target; Frame(); FinishAnim(); return; }
        Native.SetTimer(_hwnd, TimerAnim, 10, 0);
    }

    private void Expand()
    {
        if (_target == 1) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.SetForegroundWindow(_hwnd);
        StartAnim(1);
    }

    private void Collapse()
    {
        if (_target == 0) return;
        StartAnim(0);
    }

    private void AnimTick()
    {
        float p = _cfg.AnimationMs <= 0 ? 1f : Math.Clamp((Environment.TickCount64 - _animStart) / (float)_cfg.AnimationMs, 0f, 1f);
        float e = 1f - MathF.Pow(1f - p, 3f);
        _t = p >= 1f ? _target : _animFrom + (_target - _animFrom) * e;
        Frame();
        if (p >= 1f) { Native.KillTimer(_hwnd, TimerAnim); FinishAnim(); }
    }

    private void FinishAnim()
    {
        if (_target != 0) return;
        _hover = -1;
        SendToBottom();
        // Hidden icons are not needed while collapsed: give their memory back.
        for (int i = Layout.VisibleSlots; i < _slots.Count; i++) { _slots[i].Image?.Dispose(); _slots[i].Image = null; _slots[i].Failed = false; }
        Render(); Present();
        Native.EmptyWorkingSet(Native.GetCurrentProcess());
    }

    // ---------------------------------------------------------------- actions

    private void Launch(int i)
    {
        if (i < 0 || i >= _slots.Count) return;
        Native.ShellExecute(_hwnd, "open", _slots[i].Item.Path, null, null, 1);
    }

    private void Click()
    {
        bool tileHit = _downTile >= 0 && _downTile < _slots.Count;
        if (_target == 1) { if (tileHit) { Launch(_downTile); Collapse(); } return; }
        if (_slots.Count > Layout.VisibleSlots) { Expand(); return; }
        if (tileHit) Launch(_downTile);
    }

    private void AddFiles(nint drop)
    {
        uint count = Native.DragQueryFile(drop, 0xFFFFFFFF, null, 0);
        for (uint k = 0; k < count; k++)
        {
            uint len = Native.DragQueryFile(drop, k, null, 0);
            var buf = new char[len + 1];
            fixed (char* p = buf) Native.DragQueryFile(drop, k, p, len + 1);
            string path = new string(buf, 0, (int)len);
            var item = new Item { Path = path };
            _cfg.Items.Add(item);
            _slots.Add(new Slot { Item = item });
        }
        Native.DragFinish(drop);
        SaveConfig();
        Recompute();
        Frame();
    }

    private void ShowMenu(int tile)
    {
        Native.GetCursorPos(out POINT pt);
        nint menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, 0, 1, "Reload config");
        Native.AppendMenu(menu, 0, 2, "Open config file");
        if (tile >= 0 && tile < _slots.Count)
            Native.AppendMenu(menu, 0, 3, $"Remove \"{_slots[tile].Item.DisplayName}\"");
        Native.AppendMenu(menu, 0x800, 0, null);
        Native.AppendMenu(menu, 0, 4, "Exit");
        _inMenu = true;
        Native.SetForegroundWindow(_hwnd);
        int cmd = Native.TrackPopupMenu(menu, 0x100 | 0x2 /* RETURNCMD | RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, 0);
        _inMenu = false;
        Native.DestroyMenu(menu);
        switch (cmd)
        {
            case 1: ReloadConfig(); break;
            case 2: Native.ShellExecute(_hwnd, "open", Config.FilePath, null, null, 1); break;
            case 3:
                _slots[tile].Image?.Dispose();
                _cfg.Items.RemoveAt(tile);
                _slots.RemoveAt(tile);
                SaveConfig();
                Recompute();
                Frame();
                break;
            case 4: Native.PostQuitMessage(0); break;
        }
    }

    // ---------------------------------------------------------------- messages

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND: return 1;

            case Native.WM_PAINT:
            {
                byte* ps = stackalloc byte[72];
                Native.BeginPaint(h, (nint)ps);
                Present();
                Native.EndPaint(h, (nint)ps);
                return 0;
            }

            case Native.WM_MOUSEACTIVATE:
                return _target == 0 ? 3 /* MA_NOACTIVATE: clicking must not raise it above other windows */ : 1;

            case Native.WM_WINDOWPOSCHANGING:
                if (_target == 0 && !_allowZ && _t == 0f) ((WINDOWPOS*)lp)->flags |= Native.SWP_NOZORDER;
                break;

            case Native.WM_ACTIVATE:
                if ((wp & 0xFFFF) == 0 && !_inMenu) Collapse();
                return 0;

            case Native.WM_KEYDOWN:
                if (wp == 0x1B) Collapse();
                return 0;

            case Native.WM_LBUTTONDOWN:
            {
                _pressed = true; _dragging = false;
                Native.GetCursorPos(out _downPt);
                _downTile = HitTile(CanvasX(lp), CanvasY(lp));
                Native.SetCapture(h);
                return 0;
            }

            case Native.WM_MOUSEMOVE:
            {
                if (_pressed)
                {
                    Native.GetCursorPos(out POINT now);
                    if (!_dragging && _target == 0 && _t == 0f
                        && Math.Abs(now.X - _downPt.X) + Math.Abs(now.Y - _downPt.Y) >= (int)(4 * _scale))
                    {
                        _dragging = true;
                        _hover = -1;
                        Native.GetWindowRect(h, out RECT wr);
                        _grab = new POINT { X = _downPt.X - wr.Left, Y = _downPt.Y - wr.Top };
                    }
                    if (_dragging) DragTo(now);
                    return 0;
                }
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 2 /* LEAVE */, hwndTrack = h };
                    Native.TrackMouseEvent(ref tme);
                    _tracking = true;
                }
                int hit = HitTile(CanvasX(lp), CanvasY(lp));
                if (hit >= _slots.Count) hit = -1;
                if (hit != _hover) { _hover = hit; Render(); Present(); }
                return 0;
            }

            case Native.WM_MOUSELEAVE:
                _tracking = false;
                if (_hover != -1) { _hover = -1; Render(); Present(); }
                return 0;

            case Native.WM_LBUTTONUP:
            {
                bool wasDrag = _dragging;
                _pressed = false; _dragging = false;
                Native.ReleaseCapture();
                if (wasDrag) SaveConfig(); else Click();
                return 0;
            }

            case Native.WM_CAPTURECHANGED:
                _pressed = false; _dragging = false;
                return 0;

            case Native.WM_RBUTTONUP:
                ShowMenu(HitTile(CanvasX(lp), CanvasY(lp)));
                return 0;

            case Native.WM_DROPFILES:
                AddFiles(wp);
                return 0;

            case Native.WM_TIMER:
                if ((nuint)wp == TimerAnim) AnimTick();
                else if ((nuint)wp == TimerReload) { Native.KillTimer(h, TimerReload); ReloadConfig(); }
                return 0;

            case WM_CONFIG_CHANGED:
                if (Environment.TickCount64 - _lastSave > 1000) Native.SetTimer(h, TimerReload, 250, 0);
                return 0;

            case Native.WM_DISPLAYCHANGE:
            case Native.WM_SETTINGCHANGE:
                if (_target == 0) { Recompute(); Frame(); }
                return 0;

            case Native.WM_DESTROY:
                Native.PostQuitMessage(0);
                return 0;
        }
        return Native.DefWindowProc(h, msg, wp, lp);
    }

    private int CanvasX(nint lp) => (short)(lp & 0xFFFF) + Current().Left - _pe.Left;
    private int CanvasY(nint lp) => (short)((lp >> 16) & 0xFFFF) + Current().Top - _pe.Top;

    private void DragTo(POINT cursor)
    {
        int gap = (int)(_cfg.Gap * _scale);
        _cfg.X = (int)Math.Round((cursor.X - _grab.X - gap - _ox) / (double)_cw);
        _cfg.Y = (int)Math.Round((cursor.Y - _grab.Y - gap - _oy) / (double)_ch);
        Relayout();
        ApplyWindow();
        Render();
        Present();
    }
}
