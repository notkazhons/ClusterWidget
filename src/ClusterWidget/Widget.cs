using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>Reads the desktop icon grid the shell uses, so a cluster can snap to the same cells.</summary>
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
/// One cluster: a per-pixel-alpha tool window owned by the desktop (so Win+D leaves it alone), drawn by GDI+ into a
/// premultiplied DIB and pushed with UpdateLayeredWindow. Event-driven: timers only run while animating.
/// </summary>
internal sealed unsafe class Widget
{
    private const nuint TimerAnim = 1, TimerBackdrop = 3, TimerSave = 4, TimerPreload = 5, TimerTip = 6, TimerReveal = 7;

    private sealed class Slot
    {
        public Item Item = null!;
        public IconImage? Image, Mini;
        public bool Failed, MiniFailed;
    }

    private static readonly Dictionary<nint, Widget> Map = new();
    private static bool _classRegistered;

    private Config _cfg;
    private nint _hwnd;
    private readonly List<Slot> _slots = new();
    private DateTime _stamp;      // last write time of our file as we know it (to tell our own saves from outside edits)
    private Backdrop? _backdrop;
    private BackgroundImage? _bgImage;
    private RECT _monitor;

    // geometry, physical pixels. _u scales every authored pixel value (DPI).
    private int _ox, _oy, _cw, _ch, _baseIcon;  // desktop grid origin, cell size, icon size from Windows
    private float _scale = 1f, _u = 1f;
    private int _iconPx, _miniPx, _tileW, _tileH;
    private float _gapC, _padC, _gapO, _padO;
    private int _ke, _ccols, _crows, _ecols, _erows; // closed count, closed grid, expanded grid
    private float _w0, _h0, _w1, _h1;         // natural content size, closed / expanded
    private RECT _pa, _pe;                    // closed / expanded window rects, screen coords
    private POINT? _stickyTopLeft;            // while editing an open cluster, keep its top-left where it is

    // animation + input state
    private float _t;       // 0 closed .. 1 expanded
    private int _target;
    private float _animFrom;
    private long _animStart;
    private bool _pressed, _dragging, _tracking, _inMenu, _downGear, _hoverGear, _inside, _allowZ;
    private bool _kbMode;                 // keyboard navigation: this cluster has the focus and _kbIndex is the focused icon
    private int _kbIndex;
    private bool _dead;                    // destroyed: must never write its file again (that would resurrect a deleted cluster)
    private int _itemDrag = -1, _itemDragTarget = -1, _itemDragX, _itemDragY;   // reordering icons by dragging them in the expanded view
    private bool _preview, _gearPreview;      // settings-window toggles: hold the cluster open / show the settings icon
    private POINT _downPt, _grab;
    private int _downTile, _hover = -1;

    // drawing resources
    private readonly Perf.Series _animFrames = new(), _hoverFrames = new(), _dragFrames = new();
    private readonly Perf.Series _pBg = new(), _pIcons = new(), _pLabels = new(), _pPresent = new();   // where a frame's time goes (perf mode)
    private nint _memDc, _dib, _bmp, _g, _bits;
    // cached background (blurred wallpaper + tint) for the whole expanded rect, so a frame copies it instead of re-blurring/blending millions of pixels
    private nint _bgDib, _bgDc, _bgBmp, _bgG, _bgBits, _bgBrush;
    private int _bgW, _bgH, _bgOx, _bgOy, _vx, _vy, _vw, _vh;   // _v*: the part of the cache that is valid
    private bool _bgDirty = true;
    private int _preloadNext;
    private nint _tip, _tipBuf;               // hover help: a tracking tooltip that follows the pointer
    private int _tipKey = -2;
    private string? _tipText;
    private long _lastPreloadFrame;
    private int _canvasW, _canvasH;
    private nint _fontFamily, _font, _strFmt, _brushHover, _gearFamily, _gearFont;
    private float _fontEm, _gearEm;
    private uint _outlineArgb, _hoverArgb, _labelArgb;

    public Widget(Config cfg) { _cfg = cfg; }

    public Config Cfg => _cfg;

    /// <summary>Creates the window and shows the cluster.</summary>
    public void Create()
    {
        long t0 = Perf.Start();
        CreateWindow();
        LoadSlots();
        ApplyStyle();
        Recompute();
        ApplyBackgroundImage();
        Frame();
        Native.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
        SendToBottom();
        _stamp = SafeWriteTime();
        Trim();
        if (Perf.On) Perf.Log($"[{_cfg.Name}] create: {Perf.Ms(t0):F1}ms, {_slots.Count} items, icon {_iconPx}px, closed {_ccols}x{_crows}, expanded {_ecols}x{_erows}, window {_pe.Right - _pe.Left}x{_pe.Bottom - _pe.Top}");
    }

    public void Destroy()
    {
        _dead = true;
        HideTip();
        if (_tip != 0) { Native.DestroyWindow(_tip); _tip = 0; }
        if (_tipBuf != 0) { Marshal.FreeHGlobal(_tipBuf); _tipBuf = 0; }
        Map.Remove(_hwnd);
        if (_hwnd != 0) Native.DestroyWindow(_hwnd);
        _hwnd = 0;
        FreeCanvas();
        FreeStyle();
        _backdrop?.Dispose(); _backdrop = null;
        _bgImage?.Dispose(); _bgImage = null;
        foreach (var s in _slots) { s.Image?.Dispose(); s.Mini?.Dispose(); }
        _slots.Clear();
    }

    /// <summary>After work that allocated: collect garbage and give the pages back (the widget sits idle most of the time).</summary>
    private static void Trim()
    {
        GC.Collect();
        Native.EmptyWorkingSet(Native.GetCurrentProcess());
    }

    private DateTime SafeWriteTime()
    {
        try { return File.GetLastWriteTimeUtc(_cfg.SourcePath); } catch (Exception) { return default; }
    }

    // ---------------------------------------------------------------- window setup

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcThunk(nint h, uint m, nint w, nint l)
    {
        try { if (Map.TryGetValue(h, out var self)) return self.WndProc(h, m, w, l); }
        catch (Exception) { /* never let an exception cross into native code */ }
        return Native.DefWindowProc(h, m, w, l);
    }

    private void CreateWindow()
    {
        nint inst = Native.GetModuleHandle(0);
        if (!_classRegistered)
        {
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
            _classRegistered = true;
        }
        // Per-pixel-alpha top-level tool window. It is *owned* by the desktop (Progman): an owned window always stays
        // above its owner, so when "show desktop" (Win+D) raises the desktop above every app, the cluster rides along.
        const uint WS_EX_LAYERED = 0x80000;
        _hwnd = Native.CreateWindowEx(WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_ACCEPTFILES, "ClusterWidgetWindow",
            "ClusterWidget", Native.WS_POPUP, 0, 0, 100, 100, 0, 0, inst, 0);
        Map[_hwnd] = this;
        Reown(Native.GetShellWindow());
        Native.DragAcceptFiles(_hwnd, 1);
    }

    private nint _owner;

    /// <summary>
    /// Makes <paramref name="desktop"/> the owner. Win+D raises whichever window hosts the desktop icons, which is a WorkerW
    /// rather than Progman on some setups; the cluster has to be owned by that one to ride along.
    /// </summary>
    public void Reown(nint desktop)
    {
        if (_hwnd == 0 || desktop == 0 || desktop == _owner) return;
        _owner = desktop;
        _allowZ = true;
        Native.SetWindowLongPtr(_hwnd, -8 /* GWLP_HWNDPARENT: sets the owner */, desktop);
        _allowZ = false;
    }

    // ---------------------------------------------------------------- config / slots

    private void LoadSlots()
    {
        foreach (var s in _slots) { s.Image?.Dispose(); s.Mini?.Dispose(); }
        _slots.Clear();
        foreach (var it in _cfg.Items) _slots.Add(new Slot { Item = it });
    }

    /// <summary>The file was changed by something other than us (a hand edit): load it again.</summary>
    public void ReloadIfChangedOnDisk()
    {
        DateTime now = SafeWriteTime();
        if (now == _stamp) return;
        var c = Config.Load(_cfg.SourcePath);
        if (c == null) return;
        _stamp = now;
        _cfg = c;
        _bgDirty = true;
        LoadSlots();
        ApplyStyle();
        Recompute();
        RebuildBackdrop();
        ApplyBackgroundImage();
        Frame();
        SettingsWindow.Refresh();
    }

    /// <summary>Loads (or drops) the background picture to match the config; only reloads the file when its path changed.</summary>
    private void ApplyBackgroundImage()
    {
        string path = _cfg.BackgroundImage ?? "";
        float opacity = _cfg.ImageOpacity / 100f;
        if (_bgImage != null && _bgImage.Path == path) { _bgImage.SetOpacity(opacity); return; }
        _bgImage?.Dispose();
        _bgImage = path.Length == 0 ? null : BackgroundImage.Load(path, opacity);
    }

    /// <summary>Writes the cluster's file now.</summary>
    public void SaveNow()
    {
        if (_dead) return;
        _cfg.Save();
        _stamp = SafeWriteTime();
    }

    /// <summary>Called by the settings window after it changed a value.</summary>
    public void LookChanged(bool blurChanged)
    {
        // While an open cluster is being edited keep its top-left corner still, so it does not jump around under the cursor.
        if (_t > 0.01f) _stickyTopLeft = new POINT { X = _pe.Left, Y = _pe.Top };
        ApplyStyle();
        Recompute();
        _stickyTopLeft = null;
        _backdrop?.SetOpacity(_cfg.Opacity / 100f);
        _bgDirty = true;   // tint, opacity or anything else about the background may have changed
        ApplyBackgroundImage();
        if (blurChanged) Native.SetTimer(_hwnd, TimerBackdrop, 70, 0); // sliders fire fast: rebuild once they settle
        Frame();
        if (!SettingsWindow.IsOpen) Native.SetTimer(_hwnd, TimerSave, 500, 0); // with settings open, edits wait for Apply
    }

    /// <summary>Repaints (settings opened/closed, so the settings icon can show or hide).</summary>
    public void Repaint() => Frame();

    /// <summary>Settings toggle: hold the cluster in its expanded view (ignores click-away) so edits to it are visible.</summary>
    public void SetPreviewOpen(bool on)
    {
        _preview = on;
        if (on) { if (_target == 0 && _slots.Count > 0) Expand(activate: false); }
        else Collapse();
    }

    /// <summary>Settings toggle: show the settings icon even when the pointer is elsewhere.</summary>
    public void SetGearPreview(bool on) { _gearPreview = on; Frame(); }

    /// <summary>Current closed grid (columns, rows), used when switching from automatic to a fixed grid.</summary>
    public (int Cols, int Rows) GridDims => (_ccols, _crows);

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

    /// <summary>Recomputes every size and rectangle from the config and the cached grid (cheap enough for each drag step).</summary>
    private void Relayout()
    {
        _gapC = _cfg.ClosedGap * _u; _padC = _cfg.ClosedPadding * _u;
        _gapO = _cfg.OpenGap * _u; _padO = _cfg.OpenPadding * _u;
        int gap = (int)(_cfg.Gap * _scale);
        int n = _slots.Count;

        // Closed box: exactly the icon grid plus the padding on every side. Icons never shrink; padding and spacing
        // resize the box. Icons per row x per column (both set) is the grid of icon slots; otherwise it is as square as the count allows.
        bool gridSet = _cfg.BoxCols > 0 && _cfg.BoxRows > 0;
        _ke = Math.Clamp(n, 1, gridSet ? _cfg.BoxCols * _cfg.BoxRows : _cfg.ClosedCount);
        int s = _baseIcon;
        if (s != _iconPx)
        {
            _iconPx = s;
            foreach (var sl in _slots) { sl.Image?.Dispose(); sl.Image = null; sl.Failed = false; sl.Mini?.Dispose(); sl.Mini = null; sl.MiniFailed = false; }
        }
        _miniPx = Math.Max(8, (int)(s * 0.44f));

        // Tiles are exactly as wide as an icon, so spacing 0 means icons touching, same as the closed box.
        // Names, when shown, take a strip of height under each icon.
        float fontEm = MathF.Max(8f, _cfg.LabelSize * _u);
        float labelH = _cfg.ShowLabels ? fontEm * 1.5f + 3 * _u : 0;
        _tileW = s;
        _tileH = (int)(s + labelH);

        if (gridSet) { _ccols = _cfg.BoxCols; _crows = _cfg.BoxRows; }
        else
        {
            _ccols = (int)Math.Ceiling(Math.Sqrt(_ke));
            _crows = (_ke + _ccols - 1) / _ccols;
        }
        _w0 = _ccols * s + (_ccols - 1) * _gapC + 2 * _padC;
        _h0 = _crows * s + (_crows - 1) * _gapC + 2 * _padC;
        int bw = (int)Math.Ceiling(_w0), bh = (int)Math.Ceiling(_h0);

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
            // The clamp only decides what is shown. The saved cell stays as the user left it, so a monitor that is
            // missing at startup (or a passing display change) cannot overwrite it; a drag commits it (CommitPosition).
            int cx = Math.Clamp(_cfg.X, xmin, Math.Max(xmin, xmax));
            int cy = Math.Clamp(_cfg.Y, ymin, Math.Max(ymin, ymax));
            closed = Layout.Clamp(new CellRect(_ox + cx * _cw + gap, _oy + cy * _ch + gap, bw, bh), bounds);
        }
        else closed = Layout.Clamp(new CellRect(_cfg.FreeX, _cfg.FreeY, bw, bh), bounds);
        int ow = (int)Math.Ceiling(_w1), oh = (int)Math.Ceiling(_h1);
        var open = _stickyTopLeft is POINT tl
            ? Layout.Clamp(new CellRect(tl.X, tl.Y, Math.Min(Math.Max(ow, bw), workW), Math.Min(Math.Max(oh, bh), workH)), bounds)
            : _cfg.ExpandFrom == "topLeft"
                ? Layout.Clamp(new CellRect(closed.X, closed.Y, Math.Min(Math.Max(ow, bw), workW), Math.Min(Math.Max(oh, bh), workH)), bounds)
                : Layout.Expand(closed, ow, oh, bounds);
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

        foreach (float k in new[] { 1f, 0.85f, 0.7f })
        {
            float sz = Math.Max(12 * _u, full * k);
            for (int corner = 0; corner < 4; corner++)
                if (GearClear(corner, sz, OffX(sz), OffY(sz))) { _gearCorner = corner; _gearSz = sz; _gearMx = OffX(sz); _gearMy = OffY(sz); return; }
        }
        float last = Math.Max(12 * _u, full * 0.85f); // nothing is fully clear: keep it readable, a few pixels of overlap are fine
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
        long t0 = Perf.Start();
        _backdrop?.Dispose();
        _backdrop = Backdrop.Build(_monitor, _cfg.Blur, _cfg.Opacity / 100f);
        _bgDirty = true;
        if (Perf.On) Perf.Log($"[{_cfg.Name}] backdrop rebuild (blur {_cfg.Blur}px): {Perf.Ms(t0):F1}ms");
    }

    private RECT Current() => new()
    {
        Left = Lerp(_pa.Left, _pe.Left, _t), Top = Lerp(_pa.Top, _pe.Top, _t),
        Right = Lerp(_pa.Right, _pe.Right, _t), Bottom = Lerp(_pa.Bottom, _pe.Bottom, _t),
    };

    private static int Lerp(int a, int b, float t) => (int)MathF.Round(a + (b - a) * t);
    private static float LerpF(float a, float b, float t) => a + (b - a) * t;
    private float Radius => _cfg.CornerRadius * _u;

    // ---------------------------------------------------------------- hover help

    private TOOLINFO TipInfo() => new() { cbSize = (uint)sizeof(TOOLINFO), uFlags = 0x20 | 0x80 /* TTF_TRACK | TTF_ABSOLUTE */, hwnd = _hwnd, uId = 1, lpszText = (char*)_tipBuf };

    private void EnsureTip()
    {
        if (_tip != 0) return;
        nint inst = Native.GetModuleHandle(0);
        _tip = Native.CreateWindowEx(0x8 /* WS_EX_TOPMOST */, "tooltips_class32", "", 0x80000000 | 0x3 /* POPUP | ALWAYSTIP | NOPREFIX */, 0, 0, 0, 0, _hwnd, 0, inst, 0);
        _tipBuf = Marshal.AllocHGlobal(2048);
        *(char*)_tipBuf = '\0';
        Native.SendMessage(_tip, 0x0418 /* TTM_SETMAXTIPWIDTH */, 0, (nint)(420 * _scale));
        var ti = TipInfo();
        Native.SendMessage(_tip, 0x0432 /* TTM_ADDTOOLW */, 0, (nint)(&ti));
    }

    /// <summary>Remembers what the pointer is over; the help text appears if it stays there for a moment.</summary>
    private void ArmTip(int key, string? text)
    {
        if (key == _tipKey) return;
        HideTip();
        _tipKey = key; _tipText = text;
        if (text != null) Native.SetTimer(_hwnd, TimerTip, 550, 0);
    }

    private void ShowTip()
    {
        Native.KillTimer(_hwnd, TimerTip);
        if (_tipText == null || _dragging || _itemDrag >= 0) return;
        EnsureTip();
        string s = _tipText.Length > 900 ? _tipText[..900] : _tipText;
        char* buf = (char*)_tipBuf;
        for (int i = 0; i < s.Length; i++) buf[i] = s[i];
        buf[s.Length] = '\0';
        var ti = TipInfo();
        Native.SendMessage(_tip, 0x0439 /* TTM_UPDATETIPTEXTW */, 0, (nint)(&ti));
        Native.GetCursorPos(out POINT pt);
        Native.SendMessage(_tip, 0x0412 /* TTM_TRACKPOSITION */, 0, (nint)(((pt.Y + (int)(24 * _scale)) << 16) | ((pt.X + (int)(14 * _scale)) & 0xFFFF)));
        Native.SendMessage(_tip, 0x0411 /* TTM_TRACKACTIVATE */, 1, (nint)(&ti));
    }

    private void HideTip()
    {
        Native.KillTimer(_hwnd, TimerTip);
        _tipKey = -2; _tipText = null;
        if (_tip == 0) return;
        var ti = TipInfo();
        Native.SendMessage(_tip, 0x0411 /* TTM_TRACKACTIVATE */, 0, (nint)(&ti));
    }

    /// <summary>What the help bubble says for an icon: its name and place, and what a click does.</summary>
    private string? TipFor(int i)
    {
        if (i < 0 || i >= _slots.Count) return null;
        var it = _slots[i].Item;
        if (_t < 0.5f && Clustered && _cfg.ClusterPreview && i == _ke - 1)
            return $"{_slots.Count - i} more icons\nClick to open the cluster";
        string tail = _t >= 0.5f
            ? "\nClick to open it. Drag onto another slot to move it."
            : (Clustered && !_cfg.ClickLaunches ? "\nClick to open the cluster" : "\nClick to open it");
        return it.DisplayName + "\n" + it.Path + tail;
    }
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

    /// <summary>The settings icon shows while the pointer is over the cluster, always if chosen, or while the settings window's preview toggle is on.</summary>
    private bool GearVisible => _inside || _cfg.GearAlways || _gearPreview;

    private bool HitGear(int canvasX, int canvasY)
    {
        if (!GearVisible) return false;
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
        _bits = bits;
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

    private void FreeBgCache()
    {
        if (_bgBrush != 0) { Gdip.GdipDeleteBrush(_bgBrush); _bgBrush = 0; }
        if (_bgG != 0) { Gdip.GdipDeleteGraphics(_bgG); _bgG = 0; }
        if (_bgBmp != 0) { Gdip.GdipDisposeImage(_bgBmp); _bgBmp = 0; }
        if (_bgDib != 0) { Native.DeleteObject(_bgDib); _bgDib = 0; }
        if (_bgDc != 0) { Native.DeleteDC(_bgDc); _bgDc = 0; }
        _bgBits = 0; _bgDirty = true; _vw = _vh = 0;
    }

    /// <summary>
    /// Makes sure the cached background (blurred wallpaper under the tint, drawn once over the whole expanded rect) is
    /// current. Returns false if it cannot be used, in which case the caller paints the background the slow way.
    /// </summary>
    private bool PrepareBgCache()
    {
        if (_backdrop == null || _canvasW <= 0 || _canvasH <= 0) return false;
        if (_bgG == 0 || _bgW != _canvasW || _bgH != _canvasH)
        {
            FreeBgCache();
            _bgW = _canvasW; _bgH = _canvasH;
            var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = _bgW, biHeight = -_bgH, biPlanes = 1, biBitCount = 32 };
            nint screen = Native.GetDC(0);
            _bgDc = Native.CreateCompatibleDC(screen);
            _bgDib = Native.CreateDIBSection(screen, ref bi, 0, out _bgBits, 0, 0);
            Native.ReleaseDC(0, screen);
            Native.SelectObject(_bgDc, _bgDib);
            Gdip.GdipCreateBitmapFromScan0(_bgW, _bgH, _bgW * 4, Gdip.PixelFormat32bppPARGB, _bgBits, out _bgBmp);
            Gdip.GdipGetImageGraphicsContext(_bgBmp, out _bgG);
            Gdip.GdipSetInterpolationMode(_bgG, 6);
            _bgDirty = true;
        }
        if (_bgOx != _pe.Left || _bgOy != _pe.Top) { _bgOx = _pe.Left; _bgOy = _pe.Top; _bgDirty = true; } // the wallpaper under the rect changed
        // Closed (and not animating): only the closed rectangle is ever shown, so only that part is built. This keeps dragging
        // a big cluster cheap. Expanding builds the whole thing once; it then stays valid until something about the background changes.
        bool full = _t > 0.001f || _target == 1;
        int nx = full ? 0 : _pa.Left - _pe.Left, ny = full ? 0 : _pa.Top - _pe.Top;
        int nw = full ? _bgW : _pa.Right - _pa.Left, nh = full ? _bgH : _pa.Bottom - _pa.Top;
        bool covered = !_bgDirty && nx >= _vx && ny >= _vy && nx + nw <= _vx + _vw && ny + nh <= _vy + _vh;
        if (!covered)
        {
            long t0 = Perf.Start();
            for (int row = 0; row < nh; row++)
                new Span<byte>((void*)(_bgBits + (nint)(((long)(ny + row) * _bgW + nx) * 4)), nw * 4).Clear();
            _backdrop.Place(_pe.Left, _pe.Top);
            Gdip.GdipFillRectangle(_bgG, _backdrop.Brush, nx, ny, nw, nh);
            Gdip.GdipCreateSolidFill(WithAlpha(Config.Color(_cfg.Tint, 0x8C14181C), _cfg.Opacity / 100f), out nint tint);
            Gdip.GdipFillRectangle(_bgG, tint, nx, ny, nw, nh);
            Gdip.GdipDeleteBrush(tint);
            if (_bgBrush != 0) Gdip.GdipDeleteBrush(_bgBrush);
            Gdip.GdipCreateTexture(_bgBmp, 4 /* clamp */, out _bgBrush);
            _vx = nx; _vy = ny; _vw = nw; _vh = nh;
            _bgDirty = false;
            if (Perf.On) Perf.Log($"[{_cfg.Name}] background cache built ({nw}x{nh} of {_bgW}x{_bgH}): {Perf.Ms(t0):F1}ms");
        }
        return true;
    }

    /// <summary>
    /// Paints the rounded background by copying the cached pixels for the current rectangle, then re-drawing only the four
    /// rounded corners with anti-aliasing. Costs a memory copy instead of a full-screen texture fill.
    /// </summary>
    private void FastBackground(nint roundedPath, int x0, int y0, int w, int h, float radius)
    {
        for (int row = 0; row < h; row++)
        {
            long off = ((long)(y0 + row) * _canvasW + x0) * 4;
            Buffer.MemoryCopy((void*)(_bgBits + (nint)off), (void*)(_bits + (nint)off), w * 4, w * 4);
        }
        int rr = Math.Min((int)MathF.Ceiling(radius) + 2, Math.Min(w, h) / 2);
        for (int k = 0; k < 4; k++)
        {
            int xs = k % 2 == 0 ? x0 : x0 + w - rr, ys = k < 2 ? y0 : y0 + h - rr;
            for (int row = 0; row < rr; row++)
                new Span<byte>((void*)(_bits + (nint)(((long)(ys + row) * _canvasW + xs) * 4)), rr * 4).Clear(); // corner square back to transparent
            Gdip.GdipSetClipRectI(_g, xs, ys, rr, rr, 0 /* replace */);
            Gdip.GdipFillPath(_g, _bgBrush, roundedPath);                                                      // anti-aliased rounded corner
        }
        Gdip.GdipResetClip(_g);
    }

    private void FreeCanvas()
    {
        FreeBgCache();
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
        _fontEm = 0; _gearEm = 0;
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

    internal static nint RoundRect(float x, float y, float w, float h, float r)
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

        long tBg = Perf.On ? Perf.Start() : 0;
        // background, bottom to top: blurred wallpaper, tint color, then the picture (if any) over the color
        nint bg = RoundRect(cx, cy, cwid, chei, radius);
        if (PrepareBgCache()) FastBackground(bg, (int)cx, (int)cy, (int)cwid, (int)chei, radius);
        else
        {
            if (_backdrop != null)
            {
                _backdrop.Place(_pe.Left, _pe.Top);
                Gdip.GdipSetInterpolationMode(_g, 6);
                Gdip.GdipFillPath(_g, _backdrop.Brush, bg);
                Gdip.GdipSetInterpolationMode(_g, 7);
            }
            Gdip.GdipCreateSolidFill(WithAlpha(Config.Color(_cfg.Tint, 0x8C14181C), _cfg.Opacity / 100f), out nint tint);
            Gdip.GdipFillPath(_g, tint, bg);
            Gdip.GdipDeleteBrush(tint);
        }
        if (_bgImage != null)
        {
            _bgImage.Place(cx, cy, cwid, chei, _cfg.ImageZoom / 100f, _cfg.ImagePanX / 100f, _cfg.ImagePanY / 100f);
            Gdip.GdipSetInterpolationMode(_g, 6);
            Gdip.GdipFillPath(_g, _bgImage.Brush, bg);
            Gdip.GdipSetInterpolationMode(_g, 7);
            if (_cfg.ImageOverlayOpacity > 0)
            {
                Gdip.GdipCreateSolidFill(WithAlpha(Config.Color(_cfg.ImageOverlayColor, 0xFFFFFFFF), _cfg.ImageOverlayOpacity / 100f), out nint over);
                Gdip.GdipFillPath(_g, over, bg);
                Gdip.GdipDeleteBrush(over);
            }
        }
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

        if (Perf.On) _pBg.Add(Perf.Ms(tBg));
        if (_slots.Count == 0) DrawEmptySlot(TileRect(0, _t), 1f - _t);
        int visible = Math.Min(VisibleSlots, _slots.Count);
        long tTiles = Perf.On ? Perf.Start() : 0;
        for (int i = 0; i < visible; i++) DrawTile(i, TileRect(i, _t));
        if (Perf.On) _pIcons.Add(Perf.Ms(tTiles));
        if (_itemDrag >= 0 && _itemDrag < _slots.Count) DrawItemDrag();
        if (GearVisible) DrawGear();
    }

    /// <summary>While re-ordering: an outline on the slot the icon will take, and the icon itself following the pointer.</summary>
    private void DrawItemDrag()
    {
        if (_itemDragTarget >= 0 && _itemDragTarget != _itemDrag)
        {
            var r = TileRect(_itemDragTarget, _t);
            float pad = Math.Max(2f, _gapO / 2f);
            nint hp = RoundRect(r.X - pad, r.Y - pad, r.W + pad * 2, r.H + pad * 2, MathF.Min(Radius * 0.5f, 14 * _u));
            Gdip.GdipCreatePen1(WithAlpha(_outlineArgb, 0.9f), MathF.Max(2f, 2.5f * _u), 2, out nint pen);
            Gdip.GdipFillPath(_g, _brushHover, hp);
            Gdip.GdipDrawPath(_g, pen, hp);
            Gdip.GdipDeletePen(pen);
            Gdip.GdipDeletePath(hp);
        }
        var img = _slots[_itemDrag].Image;
        if (img != null) DrawIcon(img, _itemDragX - _iconPx / 2f, _itemDragY - _iconPx / 2f, _iconPx, 0.85f);
    }

    /// <summary>Moves an icon to another position in the cluster (the others shift to make room).</summary>
    private void MoveItem(int from, int to)
    {
        var item = _cfg.Items[from]; var slot = _slots[from];
        _cfg.Items.RemoveAt(from); _slots.RemoveAt(from);
        _cfg.Items.Insert(to, item); _slots.Insert(to, slot);
        SaveNow();
        Recompute();
        Frame();
    }

    /// <summary>Orders the icons by name (or reverses the current order).</summary>
    public void SortItems(int mode)
    {
        var order = new List<(Item Item, Slot Slot)>();
        for (int i = 0; i < _slots.Count; i++) order.Add((_cfg.Items[i], _slots[i]));
        if (mode == 2) order.Reverse();
        else
        {
            order.Sort((a, b) => string.Compare(a.Item.DisplayName, b.Item.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            if (mode == 1) order.Reverse();
        }
        _cfg.Items.Clear(); _slots.Clear();
        foreach (var (item, slot) in order) { _cfg.Items.Add(item); _slots.Add(slot); }
        SaveNow();
        Recompute();
        Frame();
    }

    /// <summary>Removes every icon (the files and programs themselves are not touched).</summary>
    public void ClearItems()
    {
        _cfg.Items.Clear(); _slots.Clear();
        SaveNow();
        Recompute();
        Frame();
    }

    /// <summary>Puts the closed box in the middle of the primary monitor's work area.</summary>
    public void CenterOnScreen()
    {
        var mi = default(MONITORINFO); mi.cbSize = (uint)sizeof(MONITORINFO);
        Native.GetMonitorInfo(Native.MonitorFromPoint(default, 1), ref mi);
        int bw = _pa.Right - _pa.Left, bh = _pa.Bottom - _pa.Top;
        int x = (mi.rcWork.Left + mi.rcWork.Right - bw) / 2, y = (mi.rcWork.Top + mi.rcWork.Bottom - bh) / 2;
        if (_cfg.Snap)
        {
            int gap = (int)(_cfg.Gap * _scale);
            _cfg.X = (int)Math.Round((x - gap - _ox) / (double)_cw);
            _cfg.Y = (int)Math.Round((y - gap - _oy) / (double)_ch);
        }
        else { _cfg.FreeX = x; _cfg.FreeY = y; }
        Relayout();
        Frame();
        SaveNow();
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
        if (i == _hover && _itemDrag < 0)
        {
            float inset = -Math.Max(2f, LerpF(_gapC, _gapO, _t) / 2f);
            nint hp = RoundRect(r.X + inset, r.Y + inset, r.W - inset * 2, r.H - inset * 2, MathF.Min(Radius * 0.5f, 14 * _u));
            Gdip.GdipFillPath(_g, _brushHover, hp);
            Gdip.GdipDeletePath(hp);
        }

        if (_kbMode && i == _kbIndex)
        {
            float pad = Math.Max(3f * _u, LerpF(_gapC, _gapO, _t) / 2f);
            nint fp = RoundRect(r.X - pad, r.Y - pad, r.W + pad * 2, r.H + pad * 2, MathF.Min(Radius * 0.5f, 14 * _u));
            Gdip.GdipFillPath(_g, _brushHover, fp);
            Gdip.GdipCreatePen1(0xFFFFFFFF, MathF.Max(2f, 2.5f * _u), 2, out nint ring);   // white ring with a dark edge: visible on any background
            Gdip.GdipCreatePen1(0x99000000, MathF.Max(1f, 1f * _u), 2, out nint edge);
            Gdip.GdipDrawPath(_g, edge, fp);
            Gdip.GdipDrawPath(_g, ring, fp);
            Gdip.GdipDeletePen(ring); Gdip.GdipDeletePen(edge); Gdip.GdipDeletePath(fp);
        }

        float left = r.X + (r.W - _iconPx) / 2, top = r.Y;
        bool cluster = i == _ke - 1 && Clustered && _cfg.ClusterPreview;
        if (cluster) DrawCluster(i, left, top, 1f - _t);
        float bigAlpha = cluster ? _t : (i < _ke ? 1f : _t);
        var img = i < _ke ? GetImage(slot) : slot.Image;   // closed slots load right away; the rest wait for the background loader
        if (img != null) DrawIcon(img, left, top, _iconPx, bigAlpha);
        else if (bigAlpha > 0.05f)
        {
            nint ph = RoundRect(left, top, _iconPx, _iconPx, _iconPx * 0.22f);
            Gdip.GdipFillPath(_g, _brushHover, ph);
            Gdip.GdipDeletePath(ph);
        }

        if (_cfg.ShowLabels && _t > 0.02f)
        {
            long tL = Perf.On ? Perf.Start() : 0;
            Gdip.GdipCreateSolidFill(WithAlpha(_labelArgb, _t), out nint brush);
            // the name may use the spacing on both sides of its icon, so it never runs into the next column's name
            float side = _gapO / 2f;
            var rc = new RectF { X = r.X - side, Y = top + _iconPx + 3 * _u, Width = r.W + 2 * side, Height = _fontEm * 1.6f };
            string name = slot.Item.DisplayName;
            Gdip.GdipDrawString(_g, name, name.Length, _font, rc, _strFmt, brush);
            Gdip.GdipDeleteBrush(brush);
            if (Perf.On) _pLabels.Add(Perf.Ms(tL));
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

    /// <summary>
    /// Loads the icons of the expanded view in the background of the message loop: a few milliseconds of work per tick, so a
    /// cluster with hundreds of icons never freezes. Icons not loaded yet show as faint placeholders until their turn.
    /// </summary>
    private void Preload()
    {
        _preloadNext = 0;
        Native.SetTimer(_hwnd, TimerPreload, 8, 0);
    }

    private void PreloadTick()
    {
        long t0 = Perf.Start();
        int shown = ShownCount, loaded = 0;
        while (_preloadNext < shown && Perf.Ms(t0) < 8.0)
        {
            var s = _slots[_preloadNext++];
            if (s.Image == null && !s.Failed) { GetImage(s); loaded++; }
        }
        if (_preloadNext >= shown) { Native.KillTimer(_hwnd, TimerPreload); if (Perf.On) Perf.Log($"[{_cfg.Name}] icons loaded ({shown} shown)"); }
        // expanded: show newly loaded icons, but at most ~12 times a second (a repaint of a big window is not free)
        if (loaded > 0 && _t > 0.001f && (_preloadNext >= shown || Environment.TickCount64 - _lastPreloadFrame > 80)) { _lastPreloadFrame = Environment.TickCount64; Frame(); }
    }

    /// <summary>Drops icons that are only needed while expanded, but only when they would take a lot of memory.</summary>
    private void UnloadHidden()
    {
        int keep = Clustered && _cfg.ClusterPreview ? _ke - 1 : _ke;
        long hiddenBytes = (long)Math.Max(0, _slots.Count - keep) * _iconPx * _iconPx * 4;
        if (hiddenBytes < 24L * 1024 * 1024) return; // small enough to keep: the next expand is then instant
        for (int i = keep; i < _slots.Count; i++) { _slots[i].Image?.Dispose(); _slots[i].Image = null; _slots[i].Failed = false; }
    }
    /// <summary>Moves, resizes and repaints the window in a single call, so animation never tears.</summary>
    private void Present()
    {
        if (_memDc == 0 || _hwnd == 0) return;
        RECT cur = Current();
        var size = new SIZE { cx = cur.Right - cur.Left, cy = cur.Bottom - cur.Top };
        var dst = new POINT { X = cur.Left, Y = cur.Top };
        var src = new POINT { X = cur.Left - _pe.Left, Y = cur.Top - _pe.Top };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        long tP = Perf.On ? Perf.Start() : 0;
        Native.UpdateLayeredWindow(_hwnd, 0, &dst, &size, _memDc, &src, 0, &blend, 2 /* ULW_ALPHA */);
        if (Perf.On) _pPresent.Add(Perf.Ms(tP));
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

    private void Expand(bool activate = true)
    {
        if (_target == 1) return;
        if (Perf.On) Perf.Log($"[{_cfg.Name}] expand requested");
        Preload();
        // Pop above the other windows while open, then take the keyboard so Esc and click-away (deactivation) can close it.
        // The settings window's preview toggle expands quietly instead: it stays on the desktop layer under the settings window.
        if (activate)
        {
            _allowZ = true;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            _allowZ = false;
            Native.SetForegroundWindow(_hwnd);
        }
        StartAnim(1);
    }

    private void Collapse()
    {
        if (_target == 0) return;
        StartAnim(0);
    }

    /// <summary>Lifts the closed box above the other windows for a few seconds, so it can be found when apps cover the desktop.</summary>
    public void Reveal()
    {
        if (_target == 1 || _dead) return;
        _allowZ = true;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        _allowZ = false;
        Native.SetTimer(_hwnd, TimerReveal, 4000, 0);
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
        long t0 = Perf.Start();
        Frame();
        _animFrames.Add(Perf.Ms(t0));
        if (p >= 1f) { Native.KillTimer(_hwnd, TimerAnim); _animFrames.Flush($"[{_cfg.Name}] animation to {(_target == 1 ? "expanded" : "closed")} frames"); FinishAnim(); }
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
        if (Perf.On) Perf.Log($"[{_cfg.Name}] launch #{i} {_slots[i].Item.Path}");
        Native.ShellExecute(_hwnd, "open", _slots[i].Item.Path, null, null, 1);
    }

    private void Click()
    {
        bool tileHit = _downTile >= 0 && _downTile < _slots.Count;
        if (Perf.On) Perf.Log($"[{_cfg.Name}] click: tileHit={tileHit} target={_target} clustered={Clustered} preview={_preview}");
        if (_target == 1)
        {
            // Held open by the settings window's preview toggle: icons still launch, nothing closes it.
            if (tileHit) { Launch(_downTile); if (!_preview) Collapse(); }
            return;
        }
        if (Clustered)
        {
            // With the option on, a normal icon still opens its app; only the mini preview slot (or the box itself) opens the cluster.
            bool previewSlot = _cfg.ClusterPreview && _downTile == _ke - 1;
            if (_cfg.ClickLaunches && tileHit && !previewSlot) { Launch(_downTile); return; }
            Expand();
            return;
        }
        if (tileHit) Launch(_downTile);
    }

    /// <summary>
    /// Shortcut files (.lnk, .url) are copied into the app's own folder and the cluster uses the copy, so deleting the original
    /// from the desktop never breaks the cluster. Programs, folders and other files are referenced where they are.
    /// </summary>
    private static string KeepCopy(string path)
    {
        string ext = Path.GetExtension(path);
        if (!ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)) return path;
        try
        {
            string dir = Path.Combine(Config.RootDir, "shortcuts");
            if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return path; // already ours
            Directory.CreateDirectory(dir);
            string name = Path.GetFileNameWithoutExtension(path), dst = Path.Combine(dir, name + ext);
            for (int n = 2; File.Exists(dst); n++) dst = Path.Combine(dir, $"{name} ({n}){ext}");
            File.Copy(path, dst);
            return dst;
        }
        catch (Exception) { return path; } // could not copy: fall back to pointing at the original
    }

    // ---------------------------------------------------------------- keyboard navigation

    /// <summary>Position in the reading order of the clusters on screen (top to bottom, then left to right).</summary>
    internal (int Top, int Left) ScreenOrder => (_pa.Top, _pa.Left);

    /// <summary>
    /// Brings keyboard focus to this cluster (Tab from the desktop). A cluster that can open is opened, and the focus lands
    /// on its first icon (or its last one when arriving with Shift+Tab). Tab / arrows move, Enter opens, Esc leaves.
    /// </summary>
    internal void KeyboardEnter(bool fromEnd)
    {
        if (_dead || _slots.Count == 0) { KeyboardLeave(!fromEnd); return; }
        _kbMode = true;
        HideTip();
        App.ForceForeground(_hwnd);
        if (Clustered) Expand(); // it can open, so open it; icons are then reachable one by one
        _kbIndex = fromEnd ? KbLast() : 0;
        Native.SetFocus(_hwnd);
        Frame();
        if (Perf.On) Perf.Log($"[{_cfg.Name}] keyboard focus in at {_kbIndex} (expanded={Clustered})");
    }

    /// <summary>The last icon that can be focused: every shown icon when it can open, otherwise the closed ones.</summary>
    private int KbLast() => (Clustered ? ShownCount : Math.Min(_slots.Count, _ke)) - 1;

    private void KeyboardLeave(bool forward)
    {
        _kbMode = false;
        if (_target == 1 && !_preview) Collapse();
        Frame();
        App.FocusNeighbour(this, forward);
    }

    private void KeyDown(int vk)
    {
        int last = KbLast(), cols = Clustered ? Math.Max(1, _ecols) : Math.Max(1, _ccols);
        switch (vk)
        {
            case 0x09: // Tab / Shift+Tab: next / previous icon, then on to the next cluster
            {
                bool back = Native.GetKeyState(0x10) < 0;
                int next = _kbIndex + (back ? -1 : 1);
                if (next < 0 || next > last) KeyboardLeave(!back);
                else { _kbIndex = next; Frame(); }
                break;
            }
            case 0x27: if (_kbIndex < last) { _kbIndex++; Frame(); } break;                         // right
            case 0x25: if (_kbIndex > 0) { _kbIndex--; Frame(); } break;                            // left
            case 0x28: _kbIndex = Math.Min(last, _kbIndex + cols); Frame(); break;                  // down
            case 0x26: _kbIndex = Math.Max(0, _kbIndex - cols); Frame(); break;                     // up
            case 0x24: _kbIndex = 0; Frame(); break;                                                // home
            case 0x23: _kbIndex = last; Frame(); break;                                             // end
            case 0x0D: case 0x20: // Enter / Space: open the focused app
            {
                int i = _kbIndex;
                _kbMode = false;
                Launch(i);
                if (_target == 1 && !_preview) Collapse();
                Frame();
                break;
            }
            case 0x1B: // Esc: close it and go back to the desktop
                _kbMode = false;
                if (_target == 1 && !_preview) Collapse();
                Frame();
                App.ReturnToDesktop(false);
                break;
        }
    }

    private void AddFiles(nint drop)
    {
        uint count = Native.DragQueryFile(drop, 0xFFFFFFFF, null, 0);
        for (uint k = 0; k < count; k++)
        {
            uint len = Native.DragQueryFile(drop, k, null, 0);
            var buf = new char[len + 1];
            fixed (char* p = buf) Native.DragQueryFile(drop, k, p, len + 1);
            var item = new Item { Path = KeepCopy(new string(buf, 0, (int)len)) };
            _cfg.Items.Add(item);
            _slots.Add(new Slot { Item = item });
        }
        Native.DragFinish(drop);
        SaveNow();
        Recompute();
        Frame();
    }

    private void ShowMenu(int tile)
    {
        Native.GetCursorPos(out POINT pt);
        nint menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, 0, 5, "Settings...");
        if (_slots.Count > 1 && _target == 0) Native.AppendMenu(menu, 0, 8, "Arrange icons (drag them in the expanded view)");
        if (tile >= 0 && tile < _slots.Count)
            Native.AppendMenu(menu, 0, 3, $"Remove \"{_slots[tile].Item.DisplayName}\"");
        Native.AppendMenu(menu, 0x800, 0, null);
        Native.AppendMenu(menu, 0, 6, "New cluster");
        Native.AppendMenu(menu, 0, 7, "Delete this cluster...");
        Native.AppendMenu(menu, 0, 2, "Open this cluster's file");
        Native.AppendMenu(menu, 0x800, 0, null);
        Native.AppendMenu(menu, 0, 4, "Exit");
        _inMenu = true;
        Native.SetForegroundWindow(_hwnd);
        int cmd = Native.TrackPopupMenu(menu, 0x100 | 0x2 /* RETURNCMD | RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, 0);
        _inMenu = false;
        Native.DestroyMenu(menu);
        switch (cmd)
        {
            case 2: Native.ShellExecute(_hwnd, "open", _cfg.SourcePath, null, null, 1); break;
            case 3:
                _slots[tile].Image?.Dispose(); _slots[tile].Mini?.Dispose();
                _cfg.Items.RemoveAt(tile);
                _slots.RemoveAt(tile);
                SaveNow();
                Recompute();
                Frame();
                break;
            case 4: App.Quit(); break;
            case 5: SettingsWindow.Show(this); break;
            case 8: Expand(); break; // opens even when every icon already fits closed, so a small cluster can be re-ordered too
            case 6: SettingsWindow.Show(App.NewCluster(this)); break;
            case 7: SettingsWindow.DeleteWithConfirm(this); break;
        }
    }

    // ---------------------------------------------------------------- messages

    private int CanvasX(nint lp) => (short)(lp & 0xFFFF) + Current().Left - _pe.Left;
    private int CanvasY(nint lp) => (short)((lp >> 16) & 0xFFFF) + Current().Top - _pe.Top;

    private nint WndProc(nint h, uint msg, nint wp, nint lp)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND: return 1;

            case Native.WM_MOUSEACTIVATE:
                // Collapsed: a click must not raise the cluster above other windows. Expanded: take focus (Esc, click-away).
                return _target == 0 ? 3 /* MA_NOACTIVATE */ : 1;

            case Native.WM_WINDOWPOSCHANGING:
                if (_target == 0 && !_allowZ && _t == 0f) ((WINDOWPOS*)lp)->flags |= Native.SWP_NOZORDER;
                break;

            case Native.WM_ACTIVATE:
                if ((wp & 0xFFFF) == 0 && !_inMenu)
                {
                    if (_kbMode) { _kbMode = false; Frame(); }
                    // lost focus: clicked elsewhere. With "stay open" it just drops back to the desktop layer, still expanded.
                    if (_preview && _target == 1) SendToBottom(); else Collapse();
                }
                return 0;

            case Native.WM_KEYDOWN:
                if (_kbMode) { KeyDown((int)wp); return 0; }
                if (wp == 0x1B && !_preview) Collapse();
                return 0;

            case Native.WM_LBUTTONDOWN:
            {
                if (_kbMode) { _kbMode = false; Frame(); }
                HideTip();
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
                    // expanded: dragging an icon re-orders the cluster
                    if (_target == 1 && _t > 0.99f && _downTile >= 0 && _downTile < _slots.Count && !_downGear)
                    {
                        if (_itemDrag < 0 && Math.Abs(now.X - _downPt.X) + Math.Abs(now.Y - _downPt.Y) >= (int)(6 * _scale)) _itemDrag = _downTile;
                        if (_itemDrag >= 0)
                        {
                            _itemDragX = CanvasX(lp); _itemDragY = CanvasY(lp);
                            int over = HitTile(_itemDragX, _itemDragY);
                            _itemDragTarget = over >= 0 && over < _slots.Count ? over : -1;
                            Frame();
                        }
                        return 0;
                    }
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
                ArmTip(gear ? 1000 : hit, gear ? "Cluster settings" : TipFor(hit));
                if (entered || hit != _hover || gear != _hoverGear)
                {
                    long t0 = Perf.Start();
                    _hover = hit; _hoverGear = gear; Frame();
                    _hoverFrames.Add(Perf.Ms(t0));
                }
                return 0;
            }

            case Native.WM_MOUSELEAVE:
                HideTip();
                FlushPerf();
                _tracking = false; _inside = false; _hoverGear = false;
                _hover = -1;
                if (_target == 0 && _t == 0f) { UnloadHidden(); Frame(); Trim(); }
                else Frame();
                return 0;

            case Native.WM_LBUTTONUP:
            {
                if (_itemDrag >= 0)
                {
                    int from = _itemDrag, to = _itemDragTarget;
                    _itemDrag = -1; _itemDragTarget = -1; _pressed = false;
                    Native.ReleaseCapture();
                    if (to >= 0 && to != from) MoveItem(from, to); else Frame();
                    return 0;
                }
                bool wasDrag = _dragging;
                _pressed = false; _dragging = false;
                Native.ReleaseCapture();
                if (wasDrag) { FlushPerf(); SaveNow(); }
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
                else if ((nuint)wp == TimerPreload) PreloadTick();
                else if ((nuint)wp == TimerTip) ShowTip();
                else if ((nuint)wp == TimerBackdrop) { Native.KillTimer(h, TimerBackdrop); RebuildBackdrop(); Frame(); Trim(); }
                else if ((nuint)wp == TimerSave) { Native.KillTimer(h, TimerSave); SaveNow(); }
                else if ((nuint)wp == TimerReveal) { Native.KillTimer(h, TimerReveal); if (_target == 0) SendToBottom(); }
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
        long t0 = Perf.Start();
        Relayout();
        CommitPosition();
        Frame();
        _dragFrames.Add(Perf.Ms(t0));
    }

    /// <summary>Stores where the closed box actually sits (after clamping to the screen) as the saved position.</summary>
    private void CommitPosition()
    {
        if (_cfg.Snap)
        {
            _cfg.X = (int)Math.Round((_pa.Left - (int)(_cfg.Gap * _scale) - _ox) / (double)_cw);
            _cfg.Y = (int)Math.Round((_pa.Top - (int)(_cfg.Gap * _scale) - _oy) / (double)_ch);
        }
        else { _cfg.FreeX = _pa.Left; _cfg.FreeY = _pa.Top; }
    }

    /// <summary>Writes the hover/drag timing series (perf mode only).</summary>
    public void FlushPerf()
    {
        _hoverFrames.Flush($"[{_cfg.Name}] hover redraw");
        _pBg.Flush($"[{_cfg.Name}]   .. background fill"); _pIcons.Flush($"[{_cfg.Name}]   .. all tiles (icons+names)");
        _pLabels.Flush($"[{_cfg.Name}]   .. one name"); _pPresent.Flush($"[{_cfg.Name}]   .. UpdateLayeredWindow");
        _dragFrames.Flush($"[{_cfg.Name}] drag step (relayout+frame)");
    }
}
