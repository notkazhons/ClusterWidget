using System;
using System.Runtime.InteropServices;

namespace ClusterWidget;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public POINT pt; public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEX
{
    public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
    public nint hInstance, hIcon, hCursor, hbrBackground; public char* lpszMenuName; public char* lpszClassName; public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS { public nint hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS { public int Left, Right, Top, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }

[StructLayout(LayoutKind.Sequential)]
internal struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

[StructLayout(LayoutKind.Sequential)]
internal struct WINCOMPATTRDATA { public int Attrib; public nint pvData; public nint cbData; }

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectF { public float X, Y, Width, Height; }

[StructLayout(LayoutKind.Sequential)]
internal struct GdiplusStartupInput { public uint GdiplusVersion; public nint DebugEventCallback; public int SuppressBackgroundThread, SuppressExternalCodecs; }

internal static unsafe partial class Native
{
    // ---- window messages / flags ----
    public const uint WM_DESTROY = 0x2, WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WM_ACTIVATE = 0x6, WM_SETTINGCHANGE = 0x1A,
        WM_MOUSEACTIVATE = 0x21, WM_WINDOWPOSCHANGING = 0x46, WM_DISPLAYCHANGE = 0x7E, WM_KEYDOWN = 0x100, WM_TIMER = 0x113,
        WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_DROPFILES = 0x233,
        WM_CAPTURECHANGED = 0x215, WM_MOUSELEAVE = 0x2A3, WM_APP = 0x8000;

    public const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_ACCEPTFILES = 0x10;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOREDRAW = 8, SWP_NOACTIVATE = 0x10, SWP_NOCOPYBITS = 0x100;
    public static readonly nint HWND_BOTTOM = 1, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    // ---- user32 ----
    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW")] public static partial ushort RegisterClassEx(ref WNDCLASSEX wc);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] public static partial nint DefWindowProc(nint h, uint m, nint w, nint l);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")] public static partial int GetMessage(out MSG m, nint h, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(in MSG m);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] public static partial nint DispatchMessage(in MSG m);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")] public static partial int PostMessage(nint h, uint m, nint w, nint l);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(nint h, int cmd);
    [LibraryImport("user32.dll")] public static partial int SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(nint h, out RECT r);
    [LibraryImport("user32.dll")] public static partial int GetCursorPos(out POINT p);
    [LibraryImport("user32.dll")] public static partial nint SetCapture(nint h);
    [LibraryImport("user32.dll")] public static partial int ReleaseCapture();
    [LibraryImport("user32.dll")] public static partial nuint SetTimer(nint h, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] public static partial int KillTimer(nint h, nuint id);
    [LibraryImport("user32.dll")] public static partial nint GetDC(nint h);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(nint h, nint dc);
    [LibraryImport("user32.dll")] public static partial nint BeginPaint(nint h, nint ps);
    [LibraryImport("user32.dll")] public static partial int EndPaint(nint h, nint ps);
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")] public static partial nint LoadCursor(nint inst, nint name);
    [LibraryImport("user32.dll")] public static partial int SetForegroundWindow(nint h);
    [LibraryImport("user32.dll")] public static partial int TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [LibraryImport("user32.dll")] public static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)] public static partial int AppendMenu(nint m, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] public static partial int DestroyMenu(nint m);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenu(nint m, uint flags, int x, int y, int res, nint h, nint rc);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromPoint(POINT p, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] public static partial int GetMonitorInfo(nint m, ref MONITORINFO mi);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindow(string? cls, string? title);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")] public static partial nint SendMessage(nint h, uint m, nint w, nint l);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int i);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint h);
    [LibraryImport("user32.dll")] public static partial int SetWindowRgn(nint h, nint rgn, int redraw);
    [LibraryImport("user32.dll")] public static partial int SetWindowCompositionAttribute(nint h, ref WINCOMPATTRDATA d);
    [LibraryImport("user32.dll")] public static partial int InvalidateRect(nint h, nint rc, int erase);

    // ---- gdi32 ----
    public const int SRCCOPY = 0x00CC0020;
    [LibraryImport("gdi32.dll")] public static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] public static partial nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bi, uint usage, out nint bits, nint section, uint offset);
    [LibraryImport("gdi32.dll")] public static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] public static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] public static partial int DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")] public static partial int BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int rop);
    [LibraryImport("gdi32.dll")] public static partial nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [LibraryImport("gdi32.dll")] public static partial int GetDIBits(nint dc, nint bmp, uint start, uint lines, nint bits, ref BITMAPINFOHEADER bi, uint usage);
    [LibraryImport("gdi32.dll")] public static partial int GetObjectW(nint obj, int cb, out BITMAPHDR bmp);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPHDR { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }

    // ---- dwmapi ----
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint h, int attr, ref int value, int size);
    [LibraryImport("dwmapi.dll")] public static partial int DwmExtendFrameIntoClientArea(nint h, ref MARGINS m);

    // ---- shell32 / ole32 / kernel32 / psapi ----
    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecute(nint h, string? verb, string file, string? args, string? dir, int show);
    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, nint bc, in Guid riid, out nint ppv);
    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint DragQueryFile(nint drop, uint index, char* buf, uint cch);
    [LibraryImport("shell32.dll")] public static partial void DragAcceptFiles(nint h, int accept);
    [LibraryImport("shell32.dll")] public static partial void DragFinish(nint drop);
    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint r, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")] public static partial nint GetModuleHandle(nint name);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexW", StringMarshalling = StringMarshalling.Utf16)] public static partial nint CreateMutex(nint attr, int owner, string name);
    [LibraryImport("kernel32.dll")] public static partial uint GetLastError();
    [LibraryImport("psapi.dll")] public static partial int EmptyWorkingSet(nint process);
    [LibraryImport("kernel32.dll")] public static partial nint GetCurrentProcess();
}

/// <summary>Flat GDI+ API (gdiplus.dll). Handles are plain pointers; the caller deletes them.</summary>
internal static unsafe partial class Gdip
{
    private const string Lib = "gdiplus.dll";
    public const int PixelFormat32bppPARGB = 0x000E200B, PixelFormat32bppARGB = 0x0026200A;

    [LibraryImport(Lib)] public static partial int GdiplusStartup(out nuint token, in GdiplusStartupInput input, nint output);
    [LibraryImport(Lib)] public static partial void GdiplusShutdown(nuint token);
    [LibraryImport(Lib)] public static partial int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint scan0, out nint bmp);
    [LibraryImport(Lib)] public static partial int GdipGetImageGraphicsContext(nint img, out nint g);
    [LibraryImport(Lib)] public static partial int GdipDeleteGraphics(nint g);
    [LibraryImport(Lib)] public static partial int GdipDisposeImage(nint img);
    [LibraryImport(Lib)] public static partial int GdipSetSmoothingMode(nint g, int mode);
    [LibraryImport(Lib)] public static partial int GdipSetTextRenderingHint(nint g, int hint);
    [LibraryImport(Lib)] public static partial int GdipSetInterpolationMode(nint g, int mode);
    [LibraryImport(Lib)] public static partial int GdipSetPixelOffsetMode(nint g, int mode);
    [LibraryImport(Lib)] public static partial int GdipGraphicsClear(nint g, uint argb);
    [LibraryImport(Lib)] public static partial int GdipCreateSolidFill(uint argb, out nint brush);
    [LibraryImport(Lib)] public static partial int GdipDeleteBrush(nint brush);
    [LibraryImport(Lib)] public static partial int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [LibraryImport(Lib)] public static partial int GdipDeletePen(nint pen);
    [LibraryImport(Lib)] public static partial int GdipCreatePath(int fillMode, out nint path);
    [LibraryImport(Lib)] public static partial int GdipDeletePath(nint path);
    [LibraryImport(Lib)] public static partial int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Lib)] public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);
    [LibraryImport(Lib)] public static partial int GdipClosePathFigure(nint path);
    [LibraryImport(Lib)] public static partial int GdipFillPath(nint g, nint brush, nint path);
    [LibraryImport(Lib)] public static partial int GdipDrawPath(nint g, nint pen, nint path);
    [LibraryImport(Lib)] public static partial int GdipDrawLine(nint g, nint pen, float x1, float y1, float x2, float y2);
    [LibraryImport(Lib)] public static partial int GdipDrawImageRectI(nint g, nint img, int x, int y, int w, int h);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)] public static partial int GdipCreateFontFamilyFromName(string name, nint coll, out nint fam);
    [LibraryImport(Lib)] public static partial int GdipDeleteFontFamily(nint fam);
    [LibraryImport(Lib)] public static partial int GdipCreateFont(nint fam, float em, int style, int unit, out nint font);
    [LibraryImport(Lib)] public static partial int GdipDeleteFont(nint font);
    [LibraryImport(Lib)] public static partial int GdipCreateStringFormat(int flags, ushort lang, out nint fmt);
    [LibraryImport(Lib)] public static partial int GdipDeleteStringFormat(nint fmt);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatAlign(nint fmt, int align);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatLineAlign(nint fmt, int align);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatTrimming(nint fmt, int trim);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(nint g, string s, int len, nint font, in RectF rc, nint fmt, nint brush);
}
