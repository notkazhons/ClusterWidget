using System;
using System.Runtime.InteropServices;

namespace ClusterWidget;

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE { public int cx, cy; }

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO { public int fIcon; public uint xHotspot, yHotspot; public nint hbmMask, hbmColor; }

[StructLayout(LayoutKind.Sequential)]
internal struct RectI { public int X, Y, Width, Height; }

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapData { public uint Width, Height; public int Stride, PixelFormat; public nint Scan0, Reserved; }

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public nint extra; }

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint extra; }

[StructLayout(LayoutKind.Sequential)]
internal struct DRAWITEMSTRUCT
{
    public uint CtlType, CtlID, itemID, itemAction, itemState; public nint hwndItem, hDC; public RECT rcItem; public nint itemData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CHOOSECOLOR
{
    public uint lStructSize; public nint hwndOwner, hInstance; public uint rgbResult; public nint lpCustColors;
    public uint Flags; public nint lCustData, lpfnHook, lpTemplateName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct SHFILEINFO
{
    public nint hIcon; public int iIcon; public uint dwAttributes;
    public fixed char szDisplayName[260];
    public fixed char szTypeName[80];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NOTIFYICONDATA
{
    public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
    public fixed char szTip[128];
    public uint dwState, dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

internal static unsafe partial class Native
{
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string path, uint attrs, SHFILEINFO* info, uint size, uint flags);
    [LibraryImport("shell32.dll")] public static partial int SHGetImageList(int list, in Guid riid, out nint ppv);
    [LibraryImport("user32.dll")] public static partial int DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] public static partial int GetIconInfo(nint icon, out ICONINFO info);
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] public static partial int ShellNotifyIcon(uint msg, NOTIFYICONDATA* data);
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessage(string s);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int UpdateLayeredWindow(nint h, nint hdcDst, POINT* pptDst, SIZE* size, nint hdcSrc, POINT* pptSrc, uint key, BLENDFUNCTION* blend, uint flags);
    [LibraryImport("user32.dll")] public static partial int MapWindowPoints(nint from, nint to, POINT* pts, uint count);
    [LibraryImport("user32.dll")] public static partial nint GetShellWindow();
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")] public static partial nint SetWindowsHookEx(int id, nint proc, nint mod, uint thread);
    [LibraryImport("user32.dll")] public static partial int UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] public static partial nint CallNextHookEx(nint hook, int code, nint wp, nint lp);
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static partial int SystemParametersInfo(uint action, uint param, char* buf, uint ini);
    [LibraryImport("user32.dll")] public static partial uint GetSysColor(int index);
    [LibraryImport("user32.dll")] public static partial int DestroyWindow(nint h);
    [LibraryImport("user32.dll")] public static partial int FillRect(nint dc, in RECT rc, nint brush);
    [LibraryImport("user32.dll")] public static partial int FrameRect(nint dc, in RECT rc, nint brush);
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")] public static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint h, int index, nint value);
    [LibraryImport("user32.dll")] public static partial nint GetWindowDpiAwarenessContext(nint h);
    [LibraryImport("user32.dll")] public static partial nint SetThreadDpiAwarenessContext(nint ctx);
    [LibraryImport("user32.dll")] public static partial int GetAwarenessFromDpiAwarenessContext(nint ctx);
    [LibraryImport("user32.dll")] public static partial nint GetDlgItem(nint h, int id);
    [LibraryImport("user32.dll")] public static partial nint SetFocus(nint h);
    [LibraryImport("user32.dll")] public static partial int IsWindow(nint h);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)] public static partial int SetWindowText(nint h, string text);
    [LibraryImport("user32.dll")] public static partial int GetClientRect(nint h, out RECT r);

    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint colorRef);
    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(int h, int w, int esc, int ori, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);

    [LibraryImport("comctl32.dll")] public static partial int InitCommonControlsEx(in INITCOMMONCONTROLSEX icc);
    [LibraryImport("comdlg32.dll", EntryPoint = "ChooseColorW")] public static partial int ChooseColor(ref CHOOSECOLOR cc);

    [LibraryImport("advapi32.dll", EntryPoint = "RegGetValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegGetValue(nint hkey, string subKey, string value, uint flags, out uint type, void* data, ref uint size);

    private static readonly nint HKEY_CURRENT_USER = unchecked((nint)0x80000001);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetKeyValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetKeyValue(nint hkey, string subKey, string value, uint type, char* data, uint bytes);
    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteKeyValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteKeyValue(nint hkey, string subKey, string value);
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint owner, string text, string caption, uint type);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Whether the per-user Run entry for this app exists.</summary>
    public static bool StartsWithWindows() => RegString(RunKey, "ClusterWidget") != null;

    /// <summary>Adds or removes the per-user Run entry that launches this exe at sign-in.</summary>
    public static void SetStartsWithWindows(bool on)
    {
        if (!on) { RegDeleteKeyValue(HKEY_CURRENT_USER, RunKey, "ClusterWidget"); return; }
        string cmd = "\"" + Environment.ProcessPath + "\"";
        fixed (char* p = cmd) RegSetKeyValue(HKEY_CURRENT_USER, RunKey, "ClusterWidget", 1 /* REG_SZ */, p, (uint)((cmd.Length + 1) * sizeof(char)));
    }

    public static bool RegDword(string subKey, string value, out uint result)
    {
        uint v = 0, size = 4;
        int rc = RegGetValue(HKEY_CURRENT_USER, subKey, value, 0x10 /* RRF_RT_REG_DWORD */, out _, &v, ref size);
        result = v;
        return rc == 0;
    }

    public static string? RegString(string subKey, string value)
    {
        char* buf = stackalloc char[520];
        uint size = 520 * sizeof(char);
        int rc = RegGetValue(HKEY_CURRENT_USER, subKey, value, 0x2 /* RRF_RT_REG_SZ */, out _, buf, ref size);
        return rc == 0 ? new string(buf) : null;
    }
}

internal static unsafe partial class Gdip
{
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial int GdipLoadImageFromFile(string path, out nint img);
    [LibraryImport("gdiplus.dll")] public static partial int GdipBitmapLockBits(nint bmp, in RectI rect, uint mode, int format, out BitmapData data);
    [LibraryImport("gdiplus.dll")] public static partial int GdipBitmapUnlockBits(nint bmp, in BitmapData data);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateBitmapFromHICON(nint icon, out nint bmp);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateHICONFromBitmap(nint bmp, out nint icon);
    [LibraryImport("gdiplus.dll")] public static partial int GdipFillRectangle(nint g, nint brush, float x, float y, float w, float h);
    [LibraryImport("gdiplus.dll")] public static partial int GdipGetImageWidth(nint img, out uint w);
    [LibraryImport("gdiplus.dll")] public static partial int GdipGetImageHeight(nint img, out uint h);
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawImageRectRectI(nint g, nint img, int dx, int dy, int dw, int dh, int sx, int sy, int sw, int sh, int unit, nint attrs, nint cb, nint cbData);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateTexture(nint img, int wrap, out nint brush);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateTextureIA(nint img, nint attrs, float x, float y, float w, float h, out nint brush);
    [LibraryImport("gdiplus.dll")] public static partial int GdipResetTextureTransform(nint brush);
    [LibraryImport("gdiplus.dll")] public static partial int GdipScaleTextureTransform(nint brush, float sx, float sy, int order);
    [LibraryImport("gdiplus.dll")] public static partial int GdipTranslateTextureTransform(nint brush, float dx, float dy, int order);
    [LibraryImport("gdiplus.dll")] public static partial int GdipCreateImageAttributes(out nint attrs);
    [LibraryImport("gdiplus.dll")] public static partial int GdipDisposeImageAttributes(nint attrs);
    [LibraryImport("gdiplus.dll")] public static partial int GdipSetImageAttributesColorMatrix(nint attrs, int type, int enable, float* matrix, float* gray, int flags);
}
