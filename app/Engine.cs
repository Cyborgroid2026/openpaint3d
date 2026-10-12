using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace OpenPaint3D;

/// <summary>P/Invoke surface of the Rust raster engine (libopenpaint3d_engine.so).</summary>
internal static class Native
{
    private const string Lib = "openpaint3d_engine";

    [DllImport(Lib)] public static extern IntPtr op3d_new(int w, int h);
    [DllImport(Lib)] public static extern void op3d_free(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_reset(IntPtr c, int w, int h);
    [DllImport(Lib)] public static extern int op3d_width(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_height(IntPtr c);
    [DllImport(Lib)] public static extern IntPtr op3d_data(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_stroke_begin(IntPtr c, uint tool, uint color, float size, float opacity, float x, float y);
    [DllImport(Lib)] public static extern void op3d_stroke_to(IntPtr c, float x, float y);
    [DllImport(Lib)] public static extern void op3d_stroke_end(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_shape(IntPtr c, uint kind, float x0, float y0, float x1, float y1, uint stroke, float thick, uint fill);
    [DllImport(Lib)] public static extern void op3d_model(IntPtr c, uint kind, float x0, float y0, float x1, float y1, uint color);
    [DllImport(Lib)] public static extern int op3d_fill(IntPtr c, int x, int y, uint color, uint tol);
    [DllImport(Lib)] public static extern uint op3d_pixel(IntPtr c, int x, int y);
    [DllImport(Lib)] public static extern void op3d_effect(IntPtr c, uint kind);
    [DllImport(Lib)] public static extern void op3d_resize(IntPtr c, int w, int h, int scale);
    [DllImport(Lib)] public static extern int op3d_crop(IntPtr c, int x, int y, int w, int h);
    [DllImport(Lib)] public static extern void op3d_rotate(IntPtr c, int cw);
    [DllImport(Lib)] public static extern void op3d_flip(IntPtr c, int horizontal);
    [DllImport(Lib)] public static extern void op3d_blend_bgra(IntPtr c, IntPtr src, int sw, int sh, int dx, int dy);
    [DllImport(Lib)] public static extern void op3d_load_rgba(IntPtr c, IntPtr src, int w, int h);
    [DllImport(Lib)] public static extern int op3d_undo(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_redo(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_can_undo(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_can_redo(IntPtr c);
}

public sealed class Engine : IDisposable
{
    private IntPtr _h;

    public Engine(int w, int h) => _h = Native.op3d_new(w, h);

    public int Width => Native.op3d_width(_h);
    public int Height => Native.op3d_height(_h);
    public bool CanUndo => Native.op3d_can_undo(_h) != 0;
    public bool CanRedo => Native.op3d_can_redo(_h) != 0;

    public static uint Pack(Color c) => ((uint)c.R << 24) | ((uint)c.G << 16) | ((uint)c.B << 8) | c.A;

    public void Reset(int w, int h) => Native.op3d_reset(_h, w, h);
    public void StrokeBegin(int tool, Color c, double size, double opacity, Point p) =>
        Native.op3d_stroke_begin(_h, (uint)tool, Pack(c), (float)size, (float)opacity, (float)p.X, (float)p.Y);
    public void StrokeTo(Point p) => Native.op3d_stroke_to(_h, (float)p.X, (float)p.Y);
    public void StrokeEnd() => Native.op3d_stroke_end(_h);
    public void Shape(int kind, Point a, Point b, Color stroke, double thick, Color? fill) =>
        Native.op3d_shape(_h, (uint)kind, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y, Pack(stroke), (float)thick, fill is { } f ? Pack(f) : 0);
    public void Model(int kind, Point a, Point b, Color c) =>
        Native.op3d_model(_h, (uint)kind, (float)a.X, (float)a.Y, (float)b.X, (float)b.Y, Pack(c));
    public bool Fill(Point p, Color c, int tolerance) => Native.op3d_fill(_h, (int)p.X, (int)p.Y, Pack(c), (uint)tolerance) != 0;
    public void Effect(int kind) => Native.op3d_effect(_h, (uint)kind);
    public void Resize(int w, int h, bool scale) => Native.op3d_resize(_h, w, h, scale ? 1 : 0);
    public bool Crop(PixelRect r) => Native.op3d_crop(_h, r.X, r.Y, r.Width, r.Height) != 0;
    public void Rotate(bool clockwise) => Native.op3d_rotate(_h, clockwise ? 1 : 0);
    public void Flip(bool horizontal) => Native.op3d_flip(_h, horizontal ? 1 : 0);
    public bool Undo() => Native.op3d_undo(_h) != 0;
    public bool Redo() => Native.op3d_redo(_h) != 0;

    public Color? Pixel(Point p)
    {
        var v = Native.op3d_pixel(_h, (int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        if (v == 0) return null;
        return Color.FromRgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
    }

    /// <summary>Composite a premultiplied BGRA bitmap rendered by Avalonia (text, stickers).</summary>
    public unsafe void Blend(RenderTargetBitmap bmp, int dx, int dy)
    {
        var (w, h) = (bmp.PixelSize.Width, bmp.PixelSize.Height);
        var buf = new byte[w * h * 4];
        fixed (byte* p = buf)
        {
            bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, buf.Length, w * 4);
            Native.op3d_blend_bgra(_h, (IntPtr)p, w, h, dx, dy);
        }
    }

    public void Load(string path)
    {
        using var src = SKBitmap.Decode(path) ?? throw new InvalidDataException("지원하지 않는 이미지 형식입니다.");
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        using (var cv = new SKCanvas(dst))
        {
            cv.Clear(SKColors.White);
            cv.DrawBitmap(src, 0, 0);
        }
        Native.op3d_load_rgba(_h, dst.GetPixels(), src.Width, src.Height);
    }

    public SKImage Snapshot() =>
        SKImage.FromPixelCopy(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque), Native.op3d_data(_h), Width * 4);

    public void Save(string path)
    {
        var fmt = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using var img = Snapshot();
        using var data = img.Encode(fmt, 92);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    public WriteableBitmap CreateBitmap() =>
        new(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);

    public unsafe void CopyTo(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var src = (byte*)Native.op3d_data(_h);
        int row = Width * 4;
        for (int y = 0; y < Height; y++)
            Buffer.MemoryCopy(src + y * row, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, row);
    }

    /// <summary>Render a small preview with a throwaway engine (used for panel thumbnails).</summary>
    public static WriteableBitmap Thumbnail(int w, int h, Action<Engine> draw)
    {
        using var e = new Engine(w, h);
        draw(e);
        var bmp = e.CreateBitmap();
        e.CopyTo(bmp);
        return bmp;
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) Native.op3d_free(_h);
        _h = IntPtr.Zero;
    }
}
