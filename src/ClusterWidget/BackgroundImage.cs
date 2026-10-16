using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>
/// An optional picture used as the box background. It is shrunk once at load (longest side capped) so it costs a
/// couple of MB at most, then drawn through a texture brush that is re-aimed each frame for "cover + zoom + pan".
/// </summary>
internal sealed unsafe class BackgroundImage : IDisposable
{
    private const int MaxSide = 900;

    public string Path = "";
    public int Width, Height;
    public nint Brush;
    private nint _bitmap, _pixels;

    public static BackgroundImage? Load(string path, float opacity)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (Gdip.GdipLoadImageFromFile(path, out nint img) != 0 || img == 0) return null;
        Gdip.GdipGetImageWidth(img, out uint iw); Gdip.GdipGetImageHeight(img, out uint ih);
        if (iw == 0 || ih == 0) { Gdip.GdipDisposeImage(img); return null; }

        double scale = Math.Min(1.0, MaxSide / (double)Math.Max(iw, ih));
        int w = Math.Max(1, (int)(iw * scale)), h = Math.Max(1, (int)(ih * scale));
        var bg = new BackgroundImage { Path = path, Width = w, Height = h };
        bg._pixels = Marshal.AllocHGlobal(w * h * 4);
        new Span<byte>((void*)bg._pixels, w * h * 4).Clear();
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, bg._pixels, out bg._bitmap);
        Gdip.GdipGetImageGraphicsContext(bg._bitmap, out nint g);
        Gdip.GdipSetInterpolationMode(g, 7);
        Gdip.GdipSetPixelOffsetMode(g, 2);
        Gdip.GdipDrawImageRectRectI(g, img, 0, 0, w, h, 0, 0, (int)iw, (int)ih, 2, 0, 0, 0);
        Gdip.GdipDeleteGraphics(g);
        Gdip.GdipDisposeImage(img);
        bg.SetOpacity(opacity);
        return bg;
    }

    /// <summary>Re-creates the brush with a new overall alpha.</summary>
    public void SetOpacity(float opacity)
    {
        if (Brush != 0) { Gdip.GdipDeleteBrush(Brush); Brush = 0; }
        float* m = stackalloc float[25];
        for (int k = 0; k < 25; k++) m[k] = 0;
        m[0] = m[6] = m[12] = m[24] = 1f; m[18] = Math.Clamp(opacity, 0f, 1f);
        Gdip.GdipCreateImageAttributes(out nint attrs);
        Gdip.GdipSetImageAttributesColorMatrix(attrs, 1, 1, m, null, 0);
        Gdip.GdipCreateTextureIA(_bitmap, attrs, 0, 0, Width, Height, out Brush);
        Gdip.GdipDisposeImageAttributes(attrs);
    }

    /// <summary>Aims the brush so the picture covers the rectangle, scaled by <paramref name="zoom"/> and panned.</summary>
    public void Place(float x, float y, float w, float h, float zoom, float panX, float panY)
    {
        float s = MathF.Max(w / Width, h / Height) * MathF.Max(1f, zoom);
        float left = x + (w - Width * s) * panX, top = y + (h - Height * s) * panY;
        Gdip.GdipResetTextureTransform(Brush);
        Gdip.GdipScaleTextureTransform(Brush, s, s, 0);
        Gdip.GdipTranslateTextureTransform(Brush, left, top, 1);
    }

    public void Dispose()
    {
        if (Brush != 0) { Gdip.GdipDeleteBrush(Brush); Brush = 0; }
        if (_bitmap != 0) { Gdip.GdipDisposeImage(_bitmap); _bitmap = 0; }
        if (_pixels != 0) { Marshal.FreeHGlobal(_pixels); _pixels = 0; }
    }
}
