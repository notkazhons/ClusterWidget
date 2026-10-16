using System;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>A decoded shell icon: pixels live in unmanaged memory and back a GDI+ bitmap.</summary>
internal sealed class IconImage : IDisposable
{
    public nint Bitmap;
    public readonly int Width, Height;
    private nint _pixels;

    private IconImage(nint bitmap, nint pixels, int w, int h) { Bitmap = bitmap; _pixels = pixels; Width = w; Height = h; }

    private static readonly Guid IidImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    private static readonly Guid IidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>
    /// The item's icon at <paramref name="px"/> pixels. Uses the shell's system image list (the same source as desktop
    /// icons, so shortcuts get no thumbnail frame) and falls back to the shell item image factory for paths the image
    /// list cannot resolve, such as shell:AppsFolder entries.
    /// </summary>
    public static IconImage? Load(string path, int px) => FromImageList(path, px) ?? FromFactory(path, px);

    private static unsafe IconImage? FromImageList(string path, int px)
    {
        SHFILEINFO sfi = default;
        if (Native.SHGetFileInfo(path, 0, &sfi, (uint)sizeof(SHFILEINFO), 0x4000 /* SHGFI_SYSICONINDEX */) == 0) return null;
        if (Native.SHGetImageList(px <= 48 ? 2 /* EXTRALARGE */ : 4 /* JUMBO */, IidImageList, out nint list) < 0 || list == 0) return null;
        nint hicon = 0;
        try
        {
            nint* vtbl = *(nint**)list;
            var getIcon = (delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)vtbl[10]; // IImageList::GetIcon
            if (getIcon(list, sfi.iIcon, 1 /* ILD_TRANSPARENT */, &hicon) < 0 || hicon == 0) return null;
            nint big = BitmapFromHIcon(hicon, out nint bigPixels, out int bw, out int bh);
            if (big == 0) return null;

            // shrink the (up to 256 px) icon into a bitmap of exactly the size we draw, so only that stays in memory
            int srcSize = ArtSize(big, bw, bh);
            nint pixels = Marshal.AllocHGlobal(px * px * 4);
            new Span<byte>((void*)pixels, px * px * 4).Clear();
            Gdip.GdipCreateBitmapFromScan0(px, px, px * 4, Gdip.PixelFormat32bppPARGB, pixels, out nint bmp);
            Gdip.GdipGetImageGraphicsContext(bmp, out nint g);
            Gdip.GdipSetInterpolationMode(g, 6); // high-quality bilinear: nearly as good as bicubic for a 256 -> 60 shrink, and noticeably cheaper
            Gdip.GdipSetPixelOffsetMode(g, 2);
            Gdip.GdipDrawImageRectRectI(g, big, 0, 0, px, px, 0, 0, srcSize, srcSize, 2, 0, 0, 0);
            Gdip.GdipDeleteGraphics(g);
            Gdip.GdipDisposeImage(big);
            Marshal.FreeHGlobal(bigPixels);
            return new IconImage(bmp, pixels, px, px);
        }
        finally
        {
            if (hicon != 0) Native.DestroyIcon(hicon);
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)list)[2])(list);
        }
    }

    /// <summary>
    /// Reads an icon's real pixels (GetDIBits keeps the alpha channel; GdipCreateBitmapFromHICON drops it for some
    /// icons and leaves black corners). Icons without alpha get their transparency from the AND mask.
    /// </summary>
    private static unsafe nint BitmapFromHIcon(nint hicon, out nint pixels, out int w, out int h)
    {
        pixels = 0; w = h = 0;
        if (Native.GetIconInfo(hicon, out ICONINFO ii) == 0) return 0;
        try
        {
            if (ii.hbmColor == 0 || Native.GetObjectW(ii.hbmColor, sizeof(Native.BITMAPHDR), out var bm) == 0 || bm.Width <= 0 || bm.Height <= 0) return 0;
            w = bm.Width; h = bm.Height;
            int stride = w * 4;
            var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            pixels = Marshal.AllocHGlobal(stride * h);
            nint screen = Native.GetDC(0);
            int got = Native.GetDIBits(screen, ii.hbmColor, 0, (uint)h, pixels, ref bi, 0);
            uint* px = (uint*)pixels;
            bool hasAlpha = false;
            for (int i = 0; i < w * h && !hasAlpha; i++) hasAlpha = (px[i] >> 24) != 0;
            if (got != 0 && !hasAlpha && ii.hbmMask != 0)
            {
                nint mask = Marshal.AllocHGlobal(stride * h);
                if (Native.GetDIBits(screen, ii.hbmMask, 0, (uint)h, mask, ref bi, 0) != 0)
                    for (int i = 0; i < w * h; i++) px[i] = (((uint*)mask)[i] & 0xFFFFFF) == 0 ? px[i] | 0xFF000000 : 0;
                Marshal.FreeHGlobal(mask);
            }
            Native.ReleaseDC(0, screen);
            if (got == 0) { Marshal.FreeHGlobal(pixels); pixels = 0; return 0; }
            if (Gdip.GdipCreateBitmapFromScan0(w, h, stride, Gdip.PixelFormat32bppARGB, pixels, out nint bmp) != 0)
            {
                Marshal.FreeHGlobal(pixels); pixels = 0;
                return 0;
            }
            return bmp;
        }
        finally
        {
            if (ii.hbmColor != 0) Native.DeleteObject(ii.hbmColor);
            if (ii.hbmMask != 0) Native.DeleteObject(ii.hbmMask);
        }
    }

    /// <summary>
    /// The shell's big icon lists draw an icon that has no big variant at its native size in the top-left corner of the
    /// canvas instead of scaling it. Detect that (art confined to the top-left, at a standard icon size) and return the
    /// size of the region that holds the art; otherwise the whole canvas.
    /// </summary>
    private static unsafe int ArtSize(nint bmp, int w, int h)
    {
        var rect = new RectI { X = 0, Y = 0, Width = w, Height = h };
        if (Gdip.GdipBitmapLockBits(bmp, rect, 1 /* read */, Gdip.PixelFormat32bppARGB, out BitmapData data) != 0) return Math.Min(w, h);
        int maxX = 0, maxY = 0;
        for (int y = 0; y < h; y++)
        {
            byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
            for (int x = 0; x < w; x++)
                if (row[x * 4 + 3] > 8) { if (x > maxX) maxX = x; if (y > maxY) maxY = y; }
        }
        Gdip.GdipBitmapUnlockBits(bmp, data);
        int extent = Math.Max(maxX, maxY) + 1;
        if (extent > w * 0.7) return Math.Min(w, h); // the art fills the canvas: nothing to crop
        foreach (int size in new[] { 16, 24, 32, 48, 64, 96, 128 })
            if (extent <= size) return size;
        return Math.Min(w, h);
    }

    private static unsafe IconImage? FromFactory(string path, int px)
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
        return new IconImage(bmp, pixels, bm.Width, bm.Height);
    }

    public void Dispose()
    {
        if (Bitmap != 0) { Gdip.GdipDisposeImage(Bitmap); Bitmap = 0; }
        if (_pixels != 0) { Marshal.FreeHGlobal(_pixels); _pixels = 0; }
    }
}
