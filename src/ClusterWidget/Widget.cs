using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>Reads the desktop icon grid the shell uses, so the widget can snap to the same cells.</summary>
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
        nint def = Native.FindWindowEx(Native.GetShellWindow(), 0, "SHELLDLL_DefView", null);
        for (nint w = 0; def == 0 && (w = Native.FindWindowEx(0, w, "WorkerW", null)) != 0;)
            def = Native.FindWindowEx(w, 0, "SHELLDLL_DefView", null);
        return def == 0 ? 0 : Native.FindWindowEx(def, 0, "SysListView32", null);
    }
}

/// <summary>
/// The widget window: a per-pixel-alpha tool window owned by the desktop (so Win+D leaves it alone), drawn by GDI+ into a
/// premultiplied DIB and pushed with UpdateLayeredWindow. Event-driven: timers only run while animating.
/// </summary>
internal sealed unsafe class Widget
{
    private const nuint TimerAnim = 1, TimerReload = 2, TimerBackdrop = 3, TimerSave = 4;
    private const uint WM_CONFIG_CHANGED = Native.WM_APP + 1, WM_TRAY = Native.WM_APP + 3;

    private sealed class Slot
    {
        public Item Item = null!;
        public IconImage? Image, Mini;
        public bool Failed, MiniFailed;
    }

    private static Widget _self = null!;

    private Config _cfg;
    private nint _hwnd, _trayIcon;
    private uint _taskbarCreated;
    private nuint _gdipToken;
    private readonly List<Slot> _slots = new();
    private FileSystemWatcher? _watcher;
    private long _lastSave;
    private Backdrop? _backdrop;
    private BackgroundImage? _bgImage;
    private RECT _monitor;

    // geometry, physical pixels. _u scales every authored pixel value (DPI x cluster size).
    private int _ox, _oy, _cw, _ch;           // desktop grid origin and cell size
    private float _scale = 1f, _u = 1f;
    private int _iconPx, _miniPx, _tileW, _tileH;
    private float _gapC, _padC, _gapO, _padO;
    private int _ke, _ccols, _crows, _ecols, _erows; // closed count, closed grid, expanded grid
    private float _w0, _h0, _w1, _h1;         // natural content size, closed / expanded
    private RECT _pa, _pe;                    // closed / expanded window rects, screen coords

    // animation + input state
    private float _t;       // 0 closed .. 1 expanded
    private int _target;
    private float _animFrom;
    private long _animStart;
    private bool _pressed, _dragging, _tracking, _inMenu, _downGear, _hoverGear, _inside, _allowZ;
    private POINT _downPt, _grab;
    private int _downTile, _hover = -1;

    // drawing resources
    private nint _memDc, _dib, _bmp, _g;
    private int _canvasW, _canvasH;
    private nint _fontFamily, _font, _strFmt, _brushHover, _gearFamily, _gearFont;
    private float _fontEm, _gearEm;
    private uint _outlineArgb, _hoverArgb, _labelArgb;

    public Widget(Config cfg) { _cfg = cfg; _self = this; }

    public Config Cfg => _cfg;

    public void Run()
    {
        Gdip.GdiplusStartup(out _gdipToken, new GdiplusStartupInput { GdiplusVersion = 1 }, 0);
        Native.CoInitializeEx(0, 2 /* apartment-threaded */);
        Native.InitCommonControlsEx(new INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(INITCOMMONCONTROLSEX), dwICC = 0x4 /* BAR_CLASSES */ });
        CreateWindow();
        LoadSlots();
        ApplyStyle();
        Recompute();
        ApplyBackgroundImage();
        Frame();
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        SendToBottom();
        AddTrayIcon();
        StartWatcher();
        Trim();

        while (Native.GetMessage(out MSG m, 0, 0, 0) > 0) { Native.TranslateMessage(m); Native.DispatchMessage(m); }

        RemoveTrayIcon();
        _watcher?.Dispose();
        FreeCanvas();
        FreeStyle();
        _backdrop?.Dispose();
        _bgImage?.Dispose();
        Gdip.GdiplusShutdown(_gdipToken);
    }

    private static void Trim() => Native.EmptyWorkingSet(Native.GetCurrentProcess());

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
        // Per-pixel-alpha top-level tool window. It is *owned* by the desktop (Progman): an owned window always stays
        // above its owner, so when "show desktop" (Win+D) raises the desktop above every app, the widget rides along.
        const uint WS_EX_LAYERED = 0x80000;
        _hwnd = Native.CreateWindowEx(WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_ACCEPTFILES, "ClusterWidgetWindow",
            "ClusterWidget", Native.WS_POPUP, 0, 0, 100, 100, 0, 0, inst, 0);
        Native.SetWindowLongPtr(_hwnd, -8 /* GWLP_HWNDPARENT: sets the owner */, Native.GetShellWindow());
        Native.DragAcceptFiles(_hwnd, 1);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    // ---------------------------------------------------------------- tray icon (lives in the hidden-icons flyout until pinned)

    private void AddTrayIcon()
    {
        if (_trayIcon == 0) _trayIcon = MakeTrayIcon();
        NOTIFYICONDATA d = default;
        d.cbSize = (uint)sizeof(NOTIFYICONDATA);
        d.hWnd = _hwnd; d.uID = 1; d.uFlags = 0x7 /* MESSAGE | ICON | TIP */; d.uCallbackMessage = WM_TRAY; d.hIcon = _trayIcon;
        const string tip = "Cluster widget";
        for (int i = 0; i < tip.Length; i++) d.szTip[i] = tip[i];
        Native.ShellNotifyIcon(0 /* NIM_ADD */, &d);
    }

    private void RemoveTrayIcon()
    {
        NOTIFYICONDATA d = default;
        d.cbSize = (uint)sizeof(NOTIFYICONDATA); d.hWnd = _hwnd; d.uID = 1;
        Native.ShellNotifyIcon(2 /* NIM_DELETE */, &d);
        if (_trayIcon != 0) { Native.DestroyIcon(_trayIcon); _trayIcon = 0; }
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
        nint body = RoundRect(2, 2, N - 4, N - 4, 9);
        Gdip.GdipFillPath(g, blue, body);
        Gdip.GdipDeletePath(body);
        for (int k = 0; k < 4; k++)
        {
            nint dot = RoundRect(7 + k % 2 * 11, 7 + k / 2 * 11, 8, 8, 4);
            Gdip.GdipFillPath(g, white, dot);
            Gdip.GdipDeletePath(dot);
        }
        Gdip.GdipDeleteBrush(blue); Gdip.GdipDeleteBrush(white); Gdip.GdipDeleteGraphics(g);
        Gdip.GdipCreateHICONFromBitmap(bmp, out nint icon);
        Gdip.GdipDisposeImage(bmp);
        Marshal.FreeHGlobal(pixels);
        return icon;
    }

    // ---------------------------------------------------------------- config / slots

    private void LoadSlots()
    {
        foreach (var s in _slots) { s.Image?.Dispose(); s.Mini?.Dispose(); }
        _slots.Clear();
        foreach (var it in _cfg.Items) _slots.Add(new Slot { Item = it });
    }

    private void ReloadConfig()
    {
        _cfg = Config.Load();
        LoadSlots();
        ApplyStyle();
        Recompute();
        RebuildBackdrop();
        ApplyBackgroundImage();
        Frame();
    }

    /// <summary>Loads (or drops) the background picture to match the config; only reloads the file when its path changed.</summary>
    private void ApplyBackgroundImage()
    {
        string path = _cfg.BackgroundImage ?? "";
        if (_bgImage != null && _bgImage.Path == path) { _bgImage.SetOpacity(_cfg.Opacity / 100f); return; }
        _bgImage?.Dispose();
        _bgImage = path.Length == 0 ? null : BackgroundImage.Load(path, _cfg.Opacity / 100f);
    }

    private void SaveConfig()
    {
        _lastSave = Environment.TickCount64;
        _cfg.Save();
    }

    /// <summary>Called by the settings window after it changed a value.</summary>
    public void LookChanged(bool blurChanged)
    {
        ApplyStyle();
        Recompute();
        _backdrop?.SetOpacity(_cfg.Opacity / 100f);
        ApplyBackgroundImage();
        if (blurChanged) Native.SetTimer(_hwnd, TimerBackdrop, 70, 0); // sliders fire fast: rebuild once they settle
        Frame();
        Native.SetTimer(_hwnd, TimerSave, 500, 0);
    }

    /// <summary>Settings opened (or "stay open" switched on): with "stay open" the cluster expands so edits are visible.</summary>
    public void SettingsOpened()
    {
        if (_cfg.KeepOpen && _target == 0 && _slots.Count > 0) Expand();
    }

    /// <summary>Called after the snap option flipped: carry the current position over so the box does not jump.</summary>
    public void SnapToggled()
    {
        if (_cfg.Snap)
        {
            _cfg.X = (int)Math.Round((_pa.Left - (int)(_cfg.Gap * _scale) - _ox) / (double)_cw);
            _cfg.Y = (int)Math.Round((_pa.Top - (int)(_cfg.Gap * _scale) - _oy) / (double)_ch);
        }
        else { _cfg.FreeX = _pa.Left; _cfg.FreeY = _pa.Top; }
        LookChanged(false);
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
        uint dpi = Native.GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1f : dpi / 96f;
        _u = _scale;
        (_cw, _ch) = DesktopGrid.CellSize();
        var primary = default(MONITORINFO); primary.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(default, 1), ref primary);
        _ox = primary.rcWork.Left; _oy = primary.rcWork.Top;

        // Same size as the desktop icons (Explorer's own "icon size" setting, DPI scaled), times the user's multiplier.
        uint logical = Native.RegDword(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop", "IconSize", out uint v) && v is >= 16 and <= 256 ? v : 48;
        _baseIcon = Math.Max(16, (int)(logical * _u * _cfg.IconScale));
        Relayout();
    }

    private int _baseIcon;

    /// <summary>Recomputes every size and rectangle from the config and the cached grid (cheap enough for each drag step).</summary>
    private void Relayout()
    {
        _gapC = _cfg.ClosedGap * _u; _padC = _cfg.ClosedPadding * _u;
        _gapO = _cfg.OpenGap * _u; _padO = _cfg.OpenPadding * _u;
        int gap = (int)(_cfg.Gap * _scale);
        int n = _slots.Count;

        // Closed box: exactly the icon grid plus the padding on every side. Icons never shrink; padding and spacing
        // resize the box. Columns x Rows (both set) is the grid of icon slots; otherwise it is as square as the count allows.
        bool gridSet = _cfg.BoxCols > 0 && _cfg.BoxRows > 0;
        _ke = Math.Clamp(n, 1, gridSet ? _cfg.BoxCols * _cfg.BoxRows : _cfg.ClosedCount);
        int s = _baseIcon;
        if (s != _iconPx)
        {
            _iconPx = s;
            foreach (var sl in _slots) { sl.Image?.Dispose(); sl.Image = null; sl.Failed = false; sl.Mini?.Dispose(); sl.Mini = null; sl.MiniFailed = false; }
        }
        _miniPx = Math.Max(8, (int)(s * 0.44f));

        float fontEm = MathF.Max(8f, _cfg.LabelSize * _u);
        float labelH = _cfg.ShowLabels ? fontEm * 1.5f + 4 * _u : 0;
        _tileW = Math.Max((int)(s * 1.45f), (int)(s + 12 * _u));
        _tileH = (int)(s + labelH);

        if (gridSet) { _ccols = _cfg.BoxCols; _crows = _cfg.BoxRows; }
        else
        {
            _ccols = (int)Math.Ceiling(Math.Sqrt(_ke));
            _crows = (_ke + _ccols - 1) / _ccols;
        }
        _w0 = _ccols * s + (_ccols - 1) * _gapC + 2 * _padC;
        _h0 = _crows * s + (_crows - 1) * _gapC + 2 * _padC;
        float boxW = _w0, boxH = _h0;
        int bw = (int)Math.Ceiling(boxW), bh = (int)Math.Ceiling(boxH);

        // which monitor are we on?
        POINT center = _cfg.Snap
            ? new POINT { X = _ox + _cfg.X * _cw + bw / 2, Y = _oy + _cfg.Y * _ch + bh / 2 }
            : new POINT { X = _cfg.FreeX + bw / 2, Y = _cfg.FreeY + bh / 2 };
        var mi = default(MONITORINFO); mi.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(center, 1), ref mi);
        _monitor = mi.rcMonitor;
        int workW = mi.rcWork.Right - mi.rcWork.Left, workH = mi.rcWork.Bottom - mi.rcWork.Top;
        var bounds = new CellRect(mi.rcWork.Left, mi.rcWork.Top, workW, workH);

        // expanded grid: shaped like the tiles' pixel proportions, capped to the work area
        int pitchW = _tileW + (int)_gapO, pitchH = _tileH + (int)_gapO;
        int maxC = Math.Max(2, (int)((workW - 2 * _padO + _gapO) / pitchW));
        int maxR = Math.Max(2, (int)((workH - 2 * _padO + _gapO) / pitchH));
        (_ecols, _erows) = Layout.TileGrid(n, maxC, maxR, pitchW, pitchH);
        _w1 = _ecols * _tileW + (_ecols - 1) * _gapO + 2 * _padO;
        _h1 = _erows * _tileH + (_erows - 1) * _gapO + 2 * _padO;

        // Position. Snapping only decides where the top-left corner sits (on a desktop cell); the size stays exact.
        CellRect closed;
        if (_cfg.Snap)
        {
            int xmin = (int)Math.Ceiling((mi.rcWork.Left - _ox - gap) / (double)_cw), xmax = (int)Math.Floor((mi.rcWork.Right - _ox - gap - bw) / (double)_cw);
            int ymin = (int)Math.Ceiling((mi.rcWork.Top - _oy - gap) / (double)_ch), ymax = (int)Math.Floor((mi.rcWork.Bottom - _oy - gap - bh) / (double)_ch);
            _cfg.X = Math.Clamp(_cfg.X, xmin, Math.Max(xmin, xmax));
            _cfg.Y = Math.Clamp(_cfg.Y, ymin, Math.Max(ymin, ymax));
            closed = Layout.Clamp(new CellRect(_ox + _cfg.X * _cw + gap, _oy + _cfg.Y * _ch + gap, bw, bh), bounds);
        }
        else
        {
            closed = Layout.Clamp(new CellRect(_cfg.FreeX, _cfg.FreeY, bw, bh), bounds);
            _cfg.FreeX = closed.X; _cfg.FreeY = closed.Y;
        }
        var open = Layout.Expand(closed, (int)Math.Ceiling(_w1), (int)Math.Ceiling(_h1), bounds);
        _pa = new RECT { Left = closed.X, Top = closed.Y, Right = closed.Right, Bottom = closed.Bottom };
        _pe = new RECT { Left = open.X, Top = open.Y, Right = open.Right, Bottom = open.Bottom };

        EnsureCanvas(_pe.Right - _pe.Left, _pe.Bottom - _pe.Top);
        ChooseGear();
        EnsureFont();
        if (_backdrop == null || !SameRect(_backdrop.Monitor, _monitor)) RebuildBackdrop();
    }

    // ---------------------------------------------------------------- settings icon placement

    private int _gearCorner;           // 0 top-right, 1 top-left, 2 bottom-right, 3 bottom-left
    private float _gearSz, _gearMx, _gearMy;

    private static (float X, float Y) CornerPos(float left, float top, float right, float bottom, int corner, float size, float mx, float my) => corner switch
    {
        0 => (right - mx - size, top + my),
        1 => (left + mx, top + my),
        2 => (right - mx - size, bottom - my - size),
        _ => (left + mx, bottom - my - size),
    };

    /// <summary>
    /// Picks the settings icon's corner, size and distance from the box edge. Manual values win. Automatic: it scales
    /// with the icons and takes the first corner (top-right first) where it clears every icon in both the closed and
    /// expanded layouts, shrinking a little if the padding is tight.
    /// </summary>
    private void ChooseGear()
    {
        string c = _cfg.GearCorner.ToLowerInvariant();
        int forced = c switch { "topright" => 0, "topleft" => 1, "bottomright" => 2, "bottomleft" => 3, _ => -1 };
        float full = _cfg.GearSize > 0 ? _cfg.GearSize * _u : Math.Clamp(_iconPx * 0.27f, 10 * _u, 26 * _u);
        float OffX(float sz) => _cfg.GearOffsetX >= 0 ? _cfg.GearOffsetX * _u : Math.Max(3 * _u, sz * 0.4f);
        float OffY(float sz) => _cfg.GearOffsetY >= 0 ? _cfg.GearOffsetY * _u : Math.Max(3 * _u, sz * 0.4f);
        if (forced >= 0) { _gearCorner = forced; _gearSz = full; _gearMx = OffX(full); _gearMy = OffY(full); return; }

        foreach (float k in new[] { 1f, 0.8f, 0.65f })
        {
            float sz = Math.Max(8 * _u, full * k);
            for (int corner = 0; corner < 4; corner++)
                if (GearClear(corner, sz, OffX(sz), OffY(sz))) { _gearCorner = corner; _gearSz = sz; _gearMx = OffX(sz); _gearMy = OffY(sz); return; }
        }
        float last = Math.Max(8 * _u, full * 0.65f);
        _gearCorner = 0; _gearSz = last; _gearMx = OffX(last); _gearMy = OffY(last);
    }

    private bool GearClear(int corner, float sz, float mx, float my)
    {
        float pad = 1.5f * _u;
        // closed: the gear sits on the closed rect; the icons are the closed slots
        float cl = _pa.Left - _pe.Left, ct = _pa.Top - _pe.Top, cr = _pa.Right - _pe.Left, cb = _pa.Bottom - _pe.Top;
        var g = CornerPos(cl, ct, cr, cb, corner, sz, mx, my);
        for (int i = 0; i < _ke; i++)
        {
            var r = TileRect(i, 0f);
            if (Hit(g.X - pad, g.Y - pad, sz + 2 * pad, sz + 2 * pad, r.X, r.Y, r.W, r.H)) return false;
        }
        // expanded: the gear sits on the expanded rect; the tiles are all shown items
        g = CornerPos(0, 0, _pe.Right - _pe.Left, _pe.Bottom - _pe.Top, corner, sz, mx, my);
        int shown = Math.Min(_slots.Count, _ecols * _erows);
        for (int i = 0; i < shown; i++)
        {
            var r = TileRect(i, 1f);
            if (Hit(g.X - pad, g.Y - pad, sz + 2 * pad, sz + 2 * pad, r.X, r.Y, r.W, r.H)) return false;
        }
        return true;
    }
    private static bool Hit(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
        => ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

    private static bool SameRect(RECT a, RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    private void RebuildBackdrop()
    {
        _backdrop?.Dispose();
        _backdrop = Backdrop.Build(_monitor, _cfg.Blur, _cfg.Opacity / 100f);
    }

    private RECT PixelRect(CellRect c, int gap) => new()
    {
        Left = _ox + c.X * _cw + gap, Top = _oy + c.Y * _ch + gap,
        Right = _ox + c.Right * _cw - gap, Bottom = _oy + c.Bottom * _ch - gap,
    };

    private RECT Current() => new()
    {
        Left = Lerp(_pa.Left, _pe.Left, _t), Top = Lerp(_pa.Top, _pe.Top, _t),
        Right = Lerp(_pa.Right, _pe.Right, _t), Bottom = Lerp(_pa.Bottom, _pe.Bottom, _t),
    };

    private static int Lerp(int a, int b, float t) => (int)MathF.Round(a + (b - a) * t);
    private static float LerpF(float a, float b, float t) => a + (b - a) * t;
    private float Radius => _cfg.CornerRadius * _u;

    // ---------------------------------------------------------------- tiles

    private int ShownCount => Math.Min(_slots.Count, _ecols * _erows);
    private bool Clustered => _slots.Count > _ke;
    private int VisibleSlots => _t <= 0.001f ? _ke : Math.Max(ShownCount, _ke);

    /// <summary>Tile rect in canvas coordinates (origin = expanded rect's top-left) at expansion <paramref name="t"/>.</summary>
    private (float X, float Y, float W, float H) TileRect(int i, float t)
    {
        float offX1 = ((_pe.Right - _pe.Left) - _w1) / 2f, offY1 = ((_pe.Bottom - _pe.Top) - _h1) / 2f;
        float ex = offX1 + _padO + i % _ecols * (_tileW + _gapO);
        float ey = offY1 + _padO + i / _ecols * (_tileH + _gapO);
        if (i >= _ke) return (ex, ey, _tileW, _tileH);
        float offX0 = ((_pa.Right - _pa.Left) - _w0) / 2f, offY0 = ((_pa.Bottom - _pa.Top) - _h0) / 2f;
        float cx = _pa.Left - _pe.Left + offX0 + _padC + i % _ccols * (_iconPx + _gapC);
        float cy = _pa.Top - _pe.Top + offY0 + _padC + i / _ccols * (_iconPx + _gapC);
        return (LerpF(cx, ex, t), LerpF(cy, ey, t), LerpF(_iconPx, _tileW, t), LerpF(_iconPx, _tileH, t));
    }

    private int HitTile(int canvasX, int canvasY)
    {
        float slop = Math.Max(2f, LerpF(_gapC, _gapO, _t) / 2f);
        for (int i = 0; i < VisibleSlots; i++)
        {
            var r = TileRect(i, _t);
            if (canvasX >= r.X - slop && canvasX < r.X + r.W + slop && canvasY >= r.Y - slop && canvasY < r.Y + r.H + slop) return i;
        }
        return -1;
    }

    private (float X, float Y, float S) GearRect()
    {
        RECT cur = Current();
        var p = CornerPos(cur.Left - _pe.Left, cur.Top - _pe.Top, cur.Right - _pe.Left, cur.Bottom - _pe.Top, _gearCorner, _gearSz, _gearMx, _gearMy);
        return (p.X, p.Y, _gearSz);
    }

    private bool HitGear(int canvasX, int canvasY)
    {
        if (!_inside) return false; // the gear only exists while the pointer is over the cluster
        var g = GearRect();
        float pad = 5 * _u;
        return canvasX >= g.X - pad && canvasX < g.X + g.S + pad && canvasY >= g.Y - pad && canvasY < g.Y + g.S + pad;
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
        // GDI+ draws straight into the DIB's memory as premultiplied ARGB, which is exactly what UpdateLayeredWindow wants.
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
        Gdip.GdipCreateSolidFill(_hoverArgb, out _brushHover);
        Gdip.GdipCreateFontFamilyFromName(_cfg.FontFamily, 0, out _fontFamily);
        if (_fontFamily == 0) Gdip.GdipCreateFontFamilyFromName("Segoe UI", 0, out _fontFamily);
        Gdip.GdipCreateStringFormat(0x4000 /* NoWrap */, 0, out _strFmt);
        Gdip.GdipSetStringFormatAlign(_strFmt, 1);
        Gdip.GdipSetStringFormatLineAlign(_strFmt, 0);
        Gdip.GdipSetStringFormatTrimming(_strFmt, 3); // ellipsis at character
        Gdip.GdipCreateFontFamilyFromName("Segoe Fluent Icons", 0, out _gearFamily);
        if (_gearFamily == 0) Gdip.GdipCreateFontFamilyFromName("Segoe MDL2 Assets", 0, out _gearFamily);
        _fontEm = 0;
    }

    private void FreeStyle()
    {
        if (_font != 0) { Gdip.GdipDeleteFont(_font); _font = 0; }
        if (_gearFont != 0) { Gdip.GdipDeleteFont(_gearFont); _gearFont = 0; }
        if (_fontFamily != 0) { Gdip.GdipDeleteFontFamily(_fontFamily); _fontFamily = 0; }
        if (_gearFamily != 0) { Gdip.GdipDeleteFontFamily(_gearFamily); _gearFamily = 0; }
        if (_strFmt != 0) { Gdip.GdipDeleteStringFormat(_strFmt); _strFmt = 0; }
        if (_brushHover != 0) { Gdip.GdipDeleteBrush(_brushHover); _brushHover = 0; }
    }

    private void EnsureFont()
    {
        float em = MathF.Max(8f, _cfg.LabelSize * _u);
        if (_font == 0 || MathF.Abs(em - _fontEm) >= 0.5f)
        {
            if (_font != 0) Gdip.GdipDeleteFont(_font);
            Gdip.GdipCreateFont(_fontFamily, em, 0, 2 /* pixel */, out _font);
            _fontEm = em;
        }
        if (_gearFont == 0 || MathF.Abs(_gearSz - _gearEm) >= 0.5f)
        {
            if (_gearFont != 0) Gdip.GdipDeleteFont(_gearFont);
            _gearFont = 0;
            if (_gearFamily != 0) Gdip.GdipCreateFont(_gearFamily, Math.Max(6f, _gearSz * 0.95f), 0, 2, out _gearFont);
            _gearEm = _gearSz;
        }
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

    private static uint WithAlpha(uint argb, float a) => ((uint)(Math.Clamp(a, 0f, 1f) * (argb >> 24)) << 24) | (argb & 0xFFFFFF);

    /// <summary>Draws an icon scaled to a square, optionally translucent. Full-size icons are 1:1 so they skip filtering.</summary>
    private void DrawIcon(IconImage img, float x, float y, float size, float alpha, bool smooth = false)
    {
        if (alpha <= 0.01f) return;
        Gdip.GdipSetInterpolationMode(_g, smooth ? 6 : 5);
        nint attrs = 0;
        float* m = stackalloc float[25];
        if (alpha < 0.99f)
        {
            for (int k = 0; k < 25; k++) m[k] = 0;
            m[0] = m[6] = m[12] = m[24] = 1f; m[18] = alpha;
            Gdip.GdipCreateImageAttributes(out attrs);
            Gdip.GdipSetImageAttributesColorMatrix(attrs, 1, 1, m, null, 0);
        }
        Gdip.GdipDrawImageRectRectI(_g, img.Bitmap, (int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(size), (int)MathF.Round(size),
            0, 0, img.Width, img.Height, 2, attrs, 0, 0);
        if (attrs != 0) Gdip.GdipDisposeImageAttributes(attrs);
    }

    private void Render()
    {
        if (_g == 0) return;
        Gdip.GdipGraphicsClear(_g, 0);
        RECT cur = Current();
        float cx = cur.Left - _pe.Left, cy = cur.Top - _pe.Top, cwid = cur.Right - cur.Left, chei = cur.Bottom - cur.Top;
        float radius = MathF.Min(Radius, MathF.Min(cwid, chei) / 2);

        // background: blurred wallpaper, then the tint on top
        nint bg = RoundRect(cx, cy, cwid, chei, radius);
        if (_bgImage != null)
        {
            _bgImage.Place(cx, cy, cwid, chei, _cfg.ImageZoom / 100f, _cfg.ImagePanX / 100f, _cfg.ImagePanY / 100f);
            Gdip.GdipSetInterpolationMode(_g, 6);
            Gdip.GdipFillPath(_g, _bgImage.Brush, bg);
            Gdip.GdipSetInterpolationMode(_g, 7);
        }
        else if (_backdrop != null)
        {
            _backdrop.Place(_pe.Left, _pe.Top);
            Gdip.GdipSetInterpolationMode(_g, 6);
            Gdip.GdipFillPath(_g, _backdrop.Brush, bg);
            Gdip.GdipSetInterpolationMode(_g, 7);
        }
        Gdip.GdipCreateSolidFill(WithAlpha(Config.Color(_cfg.Tint, 0x8C14181C), _cfg.Opacity / 100f), out nint tint);
        Gdip.GdipFillPath(_g, tint, bg);
        Gdip.GdipDeleteBrush(tint);
        Gdip.GdipDeletePath(bg);

        float ow = MathF.Max(0f, _cfg.OutlineWidth * _u);
        if (ow > 0)
        {
            Gdip.GdipCreatePen1(WithAlpha(_outlineArgb, _cfg.OutlineOpacity / 100f), ow, 2, out nint pen);
            nint path = RoundRect(cx + ow / 2, cy + ow / 2, cwid - ow, chei - ow, radius - ow / 2);
            Gdip.GdipDrawPath(_g, pen, path);
            Gdip.GdipDeletePath(path);
            Gdip.GdipDeletePen(pen);
        }

        if (_slots.Count == 0) DrawEmptySlot(TileRect(0, _t), 1f - _t);
        int visible = Math.Min(VisibleSlots, _slots.Count);
        for (int i = 0; i < visible; i++) DrawTile(i, TileRect(i, _t));
        if (_inside) DrawGear();
    }

    private void DrawEmptySlot((float X, float Y, float W, float H) r, float alpha)
    {
        if (alpha <= 0.01f) return;
        float s = MathF.Min(r.W, r.H) * 0.12f, mx = r.X + r.W / 2, my = r.Y + r.H / 2;
        Gdip.GdipCreatePen1(WithAlpha(_outlineArgb, 0.35f * alpha), MathF.Max(1.5f, 2f * _u), 2, out nint pen);
        Gdip.GdipDrawLine(_g, pen, mx - s, my, mx + s, my);
        Gdip.GdipDrawLine(_g, pen, mx, my - s, mx, my + s);
        Gdip.GdipDeletePen(pen);
    }

    private void DrawTile(int i, (float X, float Y, float W, float H) r)
    {
        var slot = _slots[i];
        if (i == _hover)
        {
            float inset = -Math.Max(2f, LerpF(_gapC, _gapO, _t) / 2f);
            nint hp = RoundRect(r.X + inset, r.Y + inset, r.W - inset * 2, r.H - inset * 2, MathF.Min(Radius * 0.5f, 14 * _u));
            Gdip.GdipFillPath(_g, _brushHover, hp);
            Gdip.GdipDeletePath(hp);
        }

        float left = r.X + (r.W - _iconPx) / 2, top = r.Y;
        bool cluster = i == _ke - 1 && Clustered;
        if (cluster) DrawCluster(i, left, top, 1f - _t);
        float bigAlpha = cluster ? _t : (i < _ke ? 1f : _t);
        var img = GetImage(slot);
        if (img != null) DrawIcon(img, left, top, _iconPx, bigAlpha);
        else if (bigAlpha > 0.05f)
        {
            nint ph = RoundRect(left, top, _iconPx, _iconPx, _iconPx * 0.22f);
            Gdip.GdipFillPath(_g, _brushHover, ph);
            Gdip.GdipDeletePath(ph);
        }

        if (_cfg.ShowLabels && _t > 0.02f)
        {
            Gdip.GdipCreateSolidFill(WithAlpha(_labelArgb, _t), out nint brush);
            var rc = new RectF { X = r.X, Y = top + _iconPx + 3 * _u, Width = r.W, Height = _fontEm * 1.6f };
            string name = slot.Item.DisplayName;
            Gdip.GdipDrawString(_g, name, name.Length, _font, rc, _strFmt, brush);
            Gdip.GdipDeleteBrush(brush);
        }
    }

    /// <summary>The "folder" look of the last closed slot: a mini 2x2 preview of the items that did not fit.</summary>
    private void DrawCluster(int firstIndex, float left, float top, float alpha)
    {
        if (alpha <= 0.01f) return;
        float gap = _iconPx * 0.08f, mini = (_iconPx - gap) / 2f;
        for (int k = 0; k < 4 && firstIndex + k < _slots.Count; k++)
        {
            var mi = GetMini(_slots[firstIndex + k]);
            if (mi == null) continue;
            DrawIcon(mi, left + k % 2 * (mini + gap), top + k / 2 * (mini + gap), mini, alpha, smooth: true);
        }
    }

    private void DrawGear()
    {
        if (_gearFont == 0) return;
        var g = GearRect();
        Gdip.GdipCreateSolidFill(WithAlpha(_labelArgb, _hoverGear ? 0.95f : 0.6f), out nint brush);
        var rc = new RectF { X = g.X - g.S * 0.3f, Y = g.Y - g.S * 0.1f, Width = g.S * 1.6f, Height = g.S * 1.4f };
        Gdip.GdipDrawString(_g, "\uE713", 1, _gearFont, rc, _strFmt, brush);
        Gdip.GdipDeleteBrush(brush);
    }

    private IconImage? GetImage(Slot s)
    {
        if (s.Image == null && !s.Failed)
        {
            s.Image = IconImage.Load(s.Item.Path, Math.Max(16, _iconPx));
            s.Failed = s.Image == null;
        }
        return s.Image;
    }

    private IconImage? GetMini(Slot s)
    {
        if (s.Mini == null && !s.MiniFailed)
        {
            s.Mini = IconImage.Load(s.Item.Path, Math.Max(16, _miniPx));
            s.MiniFailed = s.Mini == null;
        }
        return s.Mini;
    }

    /// <summary>Loads every visible icon ahead of time so expanding never waits on the shell.</summary>
    private void Preload()
    {
        for (int i = 0; i < ShownCount; i++) GetImage(_slots[i]);
    }

    /// <summary>Drops icons that are only needed while expanded.</summary>
    private void UnloadHidden()
    {
        int keep = Clustered ? _ke - 1 : _ke;
        for (int i = keep; i < _slots.Count; i++) { _slots[i].Image?.Dispose(); _slots[i].Image = null; _slots[i].Failed = false; }
    }

    /// <summary>Moves, resizes and repaints the window in a single call, so animation never tears.</summary>
    private void Present()
    {
        if (_memDc == 0) return;
        RECT cur = Current();
        var size = new SIZE { cx = cur.Right - cur.Left, cy = cur.Bottom - cur.Top };
        var dst = new POINT { X = cur.Left, Y = cur.Top };
        var src = new POINT { X = cur.Left - _pe.Left, Y = cur.Top - _pe.Top };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        Native.UpdateLayeredWindow(_hwnd, 0, &dst, &size, _memDc, &src, 0, &blend, 2 /* ULW_ALPHA */);
    }

    private void Frame() { Render(); Present(); }

    // ---------------------------------------------------------------- expand / collapse

    private void StartAnim(int target)
    {
        _target = target;
        _animFrom = _t;
        _animStart = Environment.TickCount64;
        if (_cfg.AnimationMs <= 0) { _t = target; Frame(); FinishAnim(); return; }
        Native.SetTimer(_hwnd, TimerAnim, 8, 0);
    }

    private void Expand()
    {
        if (_target == 1) return;
        Preload();
        // Pop above the other windows while open, then take the keyboard so Esc and click-away (deactivation) can close it.
        _allowZ = true;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _allowZ = false;
        Native.SetForegroundWindow(_hwnd);
        StartAnim(1);
    }

    private void Collapse()
    {
        if (_target == 0) return;
        StartAnim(0);
    }

    /// <summary>Back to the desktop layer: just above the desktop window (the owner), below every app.</summary>
    private void SendToBottom()
    {
        _allowZ = true;
        Native.SetWindowPos(_hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _allowZ = false;
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
        UnloadHidden();
        Frame();
        Trim();
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
        if (_target == 1)
        {
            // "Stay open" is for editing settings: launching keeps it open, and a click on empty space closes it.
            if (tileHit) { Launch(_downTile); if (!_cfg.KeepOpen) Collapse(); }
            else if (_cfg.KeepOpen) Collapse();
            return;
        }
        if (Clustered) { Expand(); return; }
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
            var item = new Item { Path = new string(buf, 0, (int)len) };
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
        Native.AppendMenu(menu, 0, 5, "Settings...");
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
                _slots[tile].Image?.Dispose(); _slots[tile].Mini?.Dispose();
                _cfg.Items.RemoveAt(tile);
                _slots.RemoveAt(tile);
                SaveConfig();
                Recompute();
                Frame();
                break;
            case 4: Native.PostQuitMessage(0); break;
            case 5: SettingsWindow.Show(this); break;
        }
    }

    // ---------------------------------------------------------------- messages

    private int CanvasX(nint lp) => (short)(lp & 0xFFFF) + Current().Left - _pe.Left;
    private int CanvasY(nint lp) => (short)((lp >> 16) & 0xFFFF) + Current().Top - _pe.Top;

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0) { AddTrayIcon(); return 0; } // Explorer restarted

        switch (msg)
        {
            case Native.WM_ERASEBKGND: return 1;

            case WM_TRAY:
                switch ((uint)(lp & 0xFFFF))
                {
                    case 0x202 /* WM_LBUTTONUP */: SettingsWindow.Show(this); break;
                    case 0x205 /* WM_RBUTTONUP */: ShowMenu(-1); break;
                }
                return 0;

            case Native.WM_MOUSEACTIVATE:
                // Collapsed: a click must not raise the widget above other windows. Expanded: take focus (Esc, click-away).
                return _target == 0 ? 3 /* MA_NOACTIVATE */ : 1;

            case Native.WM_WINDOWPOSCHANGING:
                if (_target == 0 && !_allowZ && _t == 0f) ((WINDOWPOS*)lp)->flags |= Native.SWP_NOZORDER;
                break;

            case Native.WM_ACTIVATE:
                if ((wp & 0xFFFF) == 0 && !_inMenu)
                {
                    // lost focus: clicked elsewhere. With "stay open" it just drops back to the desktop layer, still expanded.
                    if (_cfg.KeepOpen && _target == 1) SendToBottom(); else Collapse();
                }
                return 0;

            case Native.WM_KEYDOWN:
                if (wp == 0x1B) Collapse();
                return 0;

            case Native.WM_LBUTTONDOWN:
            {
                _pressed = true; _dragging = false;
                Native.GetCursorPos(out _downPt);
                int cx = CanvasX(lp), cy = CanvasY(lp);
                _downGear = HitGear(cx, cy);
                _downTile = HitTile(cx, cy);
                Native.SetCapture(h);
                return 0;
            }

            case Native.WM_MOUSEMOVE:
            {
                if (_pressed)
                {
                    Native.GetCursorPos(out POINT now);
                    if (!_dragging && !_downGear && _target == 0 && _t == 0f
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
                bool entered = !_tracking;
                if (entered)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 2 /* LEAVE */, hwndTrack = h };
                    Native.TrackMouseEvent(ref tme);
                    _tracking = true;
                    _inside = true;
                    if (_target == 0) Preload(); // hover precedes the click: icons are ready before the animation starts
                }
                int hit = HitTile(CanvasX(lp), CanvasY(lp));
                if (hit >= _slots.Count) hit = -1;
                bool gear = HitGear(CanvasX(lp), CanvasY(lp));
                if (gear) hit = -1;
                if (entered || hit != _hover || gear != _hoverGear) { _hover = hit; _hoverGear = gear; Frame(); }
                return 0;
            }

            case Native.WM_MOUSELEAVE:
                _tracking = false; _inside = false; _hoverGear = false;
                _hover = -1;
                if (_target == 0 && _t == 0f) { UnloadHidden(); Frame(); Trim(); }
                else Frame();
                return 0;

            case Native.WM_LBUTTONUP:
            {
                bool wasDrag = _dragging;
                _pressed = false; _dragging = false;
                Native.ReleaseCapture();
                if (wasDrag) SaveConfig();
                else if (_downGear && HitGear(CanvasX(lp), CanvasY(lp))) SettingsWindow.Show(this);
                else Click();
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
                else if ((nuint)wp == TimerBackdrop) { Native.KillTimer(h, TimerBackdrop); RebuildBackdrop(); Frame(); Trim(); }
                else if ((nuint)wp == TimerSave) { Native.KillTimer(h, TimerSave); SaveConfig(); }
                return 0;

            case WM_CONFIG_CHANGED:
                if (Environment.TickCount64 - _lastSave > 1000) Native.SetTimer(h, TimerReload, 250, 0);
                return 0;

            case Native.WM_DISPLAYCHANGE:
            case Native.WM_SETTINGCHANGE:
                if (_target == 0)
                {
                    Recompute();
                    if (msg == Native.WM_DISPLAYCHANGE || wp == 0x14 /* SPI_SETDESKWALLPAPER */) RebuildBackdrop();
                    Frame();
                    Trim();
                }
                return 0;

            case Native.WM_DESTROY:
                Native.PostQuitMessage(0);
                return 0;
        }
        return Native.DefWindowProc(h, msg, wp, lp);
    }

    private void DragTo(POINT cursor)
    {
        if (_cfg.Snap)
        {
            int gap = (int)(_cfg.Gap * _scale);
            _cfg.X = (int)Math.Round((cursor.X - _grab.X - gap - _ox) / (double)_cw);
            _cfg.Y = (int)Math.Round((cursor.Y - _grab.Y - gap - _oy) / (double)_ch);
        }
        else
        {
            _cfg.FreeX = cursor.X - _grab.X;
            _cfg.FreeY = cursor.Y - _grab.Y;
        }
        Relayout();
        Frame();
    }
}
