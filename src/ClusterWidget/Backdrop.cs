using System;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>
/// The blurred wallpaper behind the widget. The wallpaper does not change while the widget sits on the desktop, so
/// the blur is computed once (at reduced resolution) and reused for every frame instead of asking DWM to re-blur
/// live. That is what makes blur strength, corner radius and opacity freely adjustable.
/// </summary>
internal sealed unsafe class Backdrop : IDisposable
{
    public RECT Monitor;
    public int Factor;
    public nint Brush;
    private nint _bitmap, _pixels;

    public static Backdrop Build(RECT monitor, int blurPx, float opacity)
    {
        var bd = new Backdrop { Monitor = monitor };
        int mw = monitor.Right - monitor.Left, mh = monitor.Bottom - monitor.Top;
        int f = Math.Clamp(blurPx / 3, 1, 4);
        int w = Math.Max(1, (mw + f - 1) / f), h = Math.Max(1, (mh + f - 1) / f);
        bd.Factor = f;
        bd._pixels = Marshal.AllocHGlobal(w * h * 4);
        new Span<byte>((void*)bd._pixels, w * h * 4).Clear();
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, bd._pixels, out bd._bitmap);

        Gdip.GdipGetImageGraphicsContext(bd._bitmap, out nint g);
        uint desk = Native.GetSysColor(1 /* COLOR_DESKTOP */);
        Gdip.GdipGraphicsClear(g, 0xFF000000u | ((desk & 0xFF) << 16) | (desk & 0xFF00) | ((desk >> 16) & 0xFF));
        Gdip.GdipSetInterpolationMode(g, 7);
        Gdip.GdipSetPixelOffsetMode(g, 2);
        DrawWallpaper(g, w, h, f, mw, mh);
        Gdip.GdipDeleteGraphics(g);

        int r = (int)MathF.Round(blurPx / (float)f / 2f);
        if (r > 0) BoxBlur((byte*)bd._pixels, w, h, r);
        bd._w = w; bd._h = h;
        bd.SetOpacity(opacity);
        return bd;
    }

    private int _w, _h;

    /// <summary>Re-creates the texture brush with a new overall alpha (cheap: the blur is not redone).</summary>
    public void SetOpacity(float opacity)
    {
        if (Brush != 0) { Gdip.GdipDeleteBrush(Brush); Brush = 0; }
        float* m = stackalloc float[25];
        for (int k = 0; k < 25; k++) m[k] = 0;
        m[0] = m[6] = m[12] = m[24] = 1f; m[18] = Math.Clamp(opacity, 0f, 1f);
        Gdip.GdipCreateImageAttributes(out nint attrs);
        Gdip.GdipSetImageAttributesColorMatrix(attrs, 1, 1, m, null, 0);
        Gdip.GdipCreateTextureIA(_bitmap, attrs, 0, 0, _w, _h, out Brush);
        Gdip.GdipDisposeImageAttributes(attrs);
    }

    /// <summary>Points the texture so the monitor's top-left lands at its position on a canvas whose origin is the given screen point.</summary>
    public void Place(float canvasOriginX, float canvasOriginY)
    {
        Gdip.GdipResetTextureTransform(Brush);
        Gdip.GdipScaleTextureTransform(Brush, Factor, Factor, 0);
        Gdip.GdipTranslateTextureTransform(Brush, Monitor.Left - canvasOriginX, Monitor.Top - canvasOriginY, 1);
    }

    /// <summary>Draws the wallpaper the way Windows places it on a monitor, shrunk by <paramref name="f"/>.</summary>
    private static void DrawWallpaper(nint g, int w, int h, int f, int mw, int mh)
    {
        char* buf = stackalloc char[260];
        buf[0] = '\0';
        Native.SystemParametersInfo(0x73 /* SPI_GETDESKWALLPAPER */, 260, buf, 0);
        string path = new string(buf);
        if (path.Length == 0 || Gdip.GdipLoadImageFromFile(path, out nint img) != 0 || img == 0) return;

        Gdip.GdipGetImageWidth(img, out uint iwU); Gdip.GdipGetImageHeight(img, out uint ihU);
        int iw = (int)iwU, ih = (int)ihU;
        if (iw > 0 && ih > 0)
        {
            string style = Native.RegString(@"Control Panel\Desktop", "WallpaperStyle") ?? "10";
            switch (style)
            {
                case "2": // stretch
                    Gdip.GdipDrawImageRectRectI(g, img, 0, 0, w, h, 0, 0, iw, ih, 2, 0, 0, 0);
                    break;
                case "6": // fit
                case "0": // center / tile
                {
                    double s = style == "0" ? 1.0 : Math.Min(mw / (double)iw, mh / (double)ih);
                    int dw = (int)(iw * s / f), dh = (int)(ih * s / f);
                    Gdip.GdipDrawImageRectRectI(g, img, (w - dw) / 2, (h - dh) / 2, dw, dh, 0, 0, iw, ih, 2, 0, 0, 0);
                    break;
                }
                default: // fill / span: cover the monitor, crop the overflow
                {
                    double s = Math.Max(mw / (double)iw, mh / (double)ih);
                    int sw = (int)Math.Round(mw / s), sh = (int)Math.Round(mh / s);
                    Gdip.GdipDrawImageRectRectI(g, img, 0, 0, w, h, (iw - sw) / 2, (ih - sh) / 2, sw, sh, 2, 0, 0, 0);
                    break;
                }
            }
        }
        Gdip.GdipDisposeImage(img);
    }

    /// <summary>Three-pass box blur (close to a gaussian) on a premultiplied 32-bit buffer.</summary>
    private static void BoxBlur(byte* px, int w, int h, int r)
    {
        byte* tmp = (byte*)NativeMemory.Alloc((nuint)(w * h * 4));
        for (int pass = 0; pass < 3; pass++)
        {
            Blur1D(px, tmp, w, h, 4, w * 4, r);   // horizontal: step along x
            Blur1D(tmp, px, h, w, w * 4, 4, r);   // vertical: step along y
        }
        NativeMemory.Free(tmp);
    }

    /// <summary>Blurs <paramref name="lines"/> lines of <paramref name="len"/> pixels; <paramref name="step"/>/<paramref name="lineStep"/> are byte strides.</summary>
    private static void Blur1D(byte* src, byte* dst, int len, int lines, int step, int lineStep, int r)
    {
        int win = 2 * r + 1;
        for (int line = 0; line < lines; line++)
        {
            byte* s = src + (long)line * lineStep, d = dst + (long)line * lineStep;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += s[Math.Clamp(i, 0, len - 1) * step + c];
                for (int x = 0; x < len; x++)
                {
                    d[x * step + c] = (byte)(sum / win);
                    sum += s[Math.Min(x + r + 1, len - 1) * step + c] - s[Math.Max(x - r, 0) * step + c];
                }
            }
        }
    }

    public void Dispose()
    {
        if (Brush != 0) { Gdip.GdipDeleteBrush(Brush); Brush = 0; }
        if (_bitmap != 0) { Gdip.GdipDisposeImage(_bitmap); _bitmap = 0; }
        if (_pixels != 0) { Marshal.FreeHGlobal(_pixels); _pixels = 0; }
    }
}
