using System;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>A decoded shell icon: pixels live in unmanaged memory and back a GDI+ bitmap.</summary>
internal sealed class IconImage : IDisposable
{
    public nint Bitmap;
    private nint _pixels;

    private IconImage(nint bitmap, nint pixels) { Bitmap = bitmap; _pixels = pixels; }

    private static readonly Guid IidImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>Asks the shell for the item's icon at <paramref name="px"/> pixels. Returns null when the path cannot be resolved.</summary>
    public static unsafe IconImage? Load(string path, int px)
    {
        if (Native.SHCreateItemFromParsingName(path, 0, IidImageFactory, out nint factory) < 0 || factory == 0) return null;
        nint hbm = 0;
        try
        {
            nint* vtbl = *(nint**)factory;
            var getImage = (delegate* unmanaged[Stdcall]<nint, long, uint, nint*, int>)vtbl[3];
            const uint SIIGBF_ICONONLY = 0x4;
            long size = (uint)px | ((long)px << 32);
            if (getImage(factory, size, SIIGBF_ICONONLY, &hbm) < 0 || hbm == 0) return null;
            return FromHBitmap(hbm);
        }
        finally
        {
            if (hbm != 0) Native.DeleteObject(hbm);
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)factory)[2])(factory);
        }
    }

    private static unsafe IconImage? FromHBitmap(nint hbm)
    {
        if (Native.GetObjectW(hbm, sizeof(Native.BITMAPHDR), out var bm) == 0 || bm.Width <= 0 || bm.Height <= 0) return null;
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = bm.Width, biHeight = -bm.Height, // negative: top-down rows
            biPlanes = 1, biBitCount = 32,
        };
        int stride = bm.Width * 4;
        nint pixels = Marshal.AllocHGlobal(stride * bm.Height);
        nint screen = Native.GetDC(0);
        int lines = Native.GetDIBits(screen, hbm, 0, (uint)bm.Height, pixels, ref bi, 0);
        Native.ReleaseDC(0, screen);
        if (lines == 0) { Marshal.FreeHGlobal(pixels); return null; }
        if (Gdip.GdipCreateBitmapFromScan0(bm.Width, bm.Height, stride, Gdip.PixelFormat32bppARGB, pixels, out nint bmp) != 0)
        {
            Marshal.FreeHGlobal(pixels);
            return null;
        }
        return new IconImage(bmp, pixels);
    }

    public void Dispose()
    {
        if (Bitmap != 0) { Gdip.GdipDisposeImage(Bitmap); Bitmap = 0; }
        if (_pixels != 0) { Marshal.FreeHGlobal(_pixels); _pixels = 0; }
    }
}
