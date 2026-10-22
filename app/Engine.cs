using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace OpenPaint3D;

/// <summary>Non-destructive view settings, mirrors the Rust <c>View</c> struct.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ViewSettings
{
    public int ShowBg;
    public uint Bg;
    public uint LightColor;
    public float Brightness;
    public float Ambient;
    public float LightAngle;
    public uint Filter;
    public float Tint;
}

/// <summary>P/Invoke surface of the Rust engine (libopenpaint3d_engine.so).</summary>
internal static unsafe class Native
{
    private const string Lib = "openpaint3d_engine";

    [DllImport(Lib)] public static extern IntPtr op3d_new(int w, int h);
    [DllImport(Lib)] public static extern void op3d_free(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_reset(IntPtr c, int w, int h);
    [DllImport(Lib)] public static extern int op3d_width(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_height(IntPtr c);
    [DllImport(Lib)] public static extern IntPtr op3d_display(IntPtr c, int ss);
    [DllImport(Lib)] public static extern IntPtr op3d_pixels(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_get_view(IntPtr c, out ViewSettings v);
    [DllImport(Lib)] public static extern void op3d_set_view(IntPtr c, in ViewSettings v, uint label);
    [DllImport(Lib)] public static extern void op3d_checkpoint(IntPtr c, uint label);
    [DllImport(Lib)] public static extern void op3d_stroke_begin(IntPtr c, uint tool, uint color, float size, float opacity, uint finish, float x, float y);
    [DllImport(Lib)] public static extern void op3d_stroke_to(IntPtr c, float x, float y);
    [DllImport(Lib)] public static extern void op3d_stroke_end(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_fill(IntPtr c, int x, int y, uint color, uint tol);
    [DllImport(Lib)] public static extern uint op3d_pick(IntPtr c, int x, int y);
    [DllImport(Lib)] public static extern void op3d_blend_bgra(IntPtr c, IntPtr src, int sw, int sh, int dx, int dy, float opacity, uint finish, uint label);
    [DllImport(Lib)] public static extern void op3d_load_rgba(IntPtr c, IntPtr src, int w, int h);
    [DllImport(Lib)] public static extern void op3d_resize(IntPtr c, int w, int h, int scale);
    [DllImport(Lib)] public static extern int op3d_crop(IntPtr c, int x, int y, int w, int h);
    [DllImport(Lib)] public static extern void op3d_rotate(IntPtr c, int cw);
    [DllImport(Lib)] public static extern void op3d_flip(IntPtr c, int horizontal);
    [DllImport(Lib)] public static extern int op3d_undo(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_redo(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_undo_count(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_undo_discard(IntPtr c);
    [DllImport(Lib)] public static extern ulong op3d_history_serial(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_redo_count(IntPtr c);
    [DllImport(Lib)] public static extern uint op3d_history_label(IntPtr c, int redo, int i);
    [DllImport(Lib)] public static extern void op3d_obj_begin(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_obj_part(IntPtr c, uint prim, float* transform, uint color);
    [DllImport(Lib)] public static extern void op3d_obj_part_mask(IntPtr c, byte* mask, int mw, int mh, uint mode, int step, float* transform, uint color);
    [DllImport(Lib)] public static extern uint op3d_obj_commit(IntPtr c, float* t, uint color, uint finish, uint label);
    [DllImport(Lib)] public static extern void op3d_obj_set(IntPtr c, uint id, float px, float py, float pz, float sx, float sy, float sz);
    [DllImport(Lib)] public static extern void op3d_obj_rotate(IntPtr c, uint id, uint axis, float deg);
    [DllImport(Lib)] public static extern void op3d_obj_material(IntPtr c, uint id, uint color, uint finish);
    [DllImport(Lib)] public static extern int op3d_obj_delete(IntPtr c, uint id);
    [DllImport(Lib)] public static extern uint op3d_obj_duplicate(IntPtr c, uint id);
    [DllImport(Lib)] public static extern int op3d_obj_info(IntPtr c, uint id, float* out14);
    [DllImport(Lib)] public static extern uint op3d_obj_pick(IntPtr c, int x, int y);
    [DllImport(Lib)] public static extern int op3d_obj_count(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_obj_rotate_about(IntPtr c, uint id, uint axis, float deg, float cx, float cy, float cz);
    [DllImport(Lib)] public static extern uint op3d_obj_clone(IntPtr c, uint id, float dx, float dy, uint group);
    [DllImport(Lib)] public static extern int op3d_obj_remove(IntPtr c, uint id);
    [DllImport(Lib)] public static extern void op3d_obj_set_group(IntPtr c, uint id, uint group);
    [DllImport(Lib)] public static extern uint op3d_obj_group(IntPtr c, uint id);
    [DllImport(Lib)] public static extern int op3d_obj_ids(IntPtr c, uint* out_, int max);
    [DllImport(Lib)] public static extern void op3d_clear_rect(IntPtr c, int x, int y, int w, int h, uint label);
    [DllImport(Lib)] public static extern void op3d_clip_clear(IntPtr c);
    [DllImport(Lib)] public static extern void op3d_clip_add(IntPtr c, uint id);
    [DllImport(Lib)] public static extern int op3d_clip_count(IntPtr c);
    [DllImport(Lib)] public static extern int op3d_clip_paste(IntPtr c, float dx, float dy);
    [DllImport(Lib)] public static extern void op3d_set_perspective(IntPtr c, int on);
    [DllImport(Lib)] public static extern void op3d_render_3d(IntPtr c, byte* out_, int ow, int oh, float yaw, float pitch, float zoom, int ss);
    [DllImport(Lib)] public static extern float* op3d_export_tris(IntPtr c, out int count);
    [DllImport(Lib)] public static extern byte* op3d_save(IntPtr c, out int len);
    [DllImport(Lib)] public static extern int op3d_load(IntPtr c, byte* data, int len);
}

/// <summary>Geometry of a 3D object as reported by the engine.</summary>
public readonly record struct ObjInfo(Vector3D Pos, Vector3D Scale, Rect Screen, Color Color, int Finish);

public readonly record struct Vector3D(double X, double Y, double Z);

/// <summary>Part of a 3D model: primitive or mask, placed inside the object's unit box.</summary>
public sealed record ModelPart(int Prim, double X, double Y, double Z, double Sx, double Sy, double Sz,
    double Rx = 0, double Ry = 0, double Rz = 0, string? Color = null);

public sealed unsafe class Engine : IDisposable
{
    private IntPtr _h;

    public Engine(int w, int h) => _h = Native.op3d_new(w, h);

    public int Width => Native.op3d_width(_h);
    public int Height => Native.op3d_height(_h);
    public int UndoCount => Native.op3d_undo_count(_h);
    public int RedoCount => Native.op3d_redo_count(_h);
    public bool CanUndo => UndoCount > 0;
    public bool CanRedo => RedoCount > 0;
    public int ObjectCount => Native.op3d_obj_count(_h);

    public static uint Pack(Color c) => ((uint)c.R << 24) | ((uint)c.G << 16) | ((uint)c.B << 8) | c.A;
    public static Color Unpack(uint v) => Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));

    public void Reset(int w, int h) => Native.op3d_reset(_h, w, h);

    public ViewSettings View
    {
        get
        {
            Native.op3d_get_view(_h, out var v);
            return v;
        }
    }

    public void SetView(ViewSettings v, Label label = Label.None) => Native.op3d_set_view(_h, v, (uint)label);
    public void Checkpoint(Label label) => Native.op3d_checkpoint(_h, (uint)label);

    public void StrokeBegin(int tool, Color c, double size, double opacity, int finish, Point p) =>
        Native.op3d_stroke_begin(_h, (uint)tool, Pack(c), (float)size, (float)opacity, (uint)finish, (float)p.X, (float)p.Y);
    public void StrokeTo(Point p) => Native.op3d_stroke_to(_h, (float)p.X, (float)p.Y);
    public void StrokeEnd() => Native.op3d_stroke_end(_h);
    public bool Fill(Point p, Color c, int tolerance) => Native.op3d_fill(_h, (int)Math.Floor(p.X), (int)Math.Floor(p.Y), Pack(c), (uint)tolerance) != 0;

    public Color? Pick(Point p)
    {
        var v = Native.op3d_pick(_h, (int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        return v == 0 ? null : Color.FromRgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
    }

    public void Resize(int w, int h, bool scale) => Native.op3d_resize(_h, w, h, scale ? 1 : 0);
    public bool Reframe(PixelRect r) => Native.op3d_crop(_h, r.X, r.Y, r.Width, r.Height) != 0;
    public void Rotate(bool clockwise) => Native.op3d_rotate(_h, clockwise ? 1 : 0);
    public void Flip(bool horizontal) => Native.op3d_flip(_h, horizontal ? 1 : 0);
    public bool Undo() => Native.op3d_undo(_h) != 0;
    public bool Redo() => Native.op3d_redo(_h) != 0;
    /// <summary>Identity of the newest undo step (changes with every new step, even when history is full).</summary>
    public ulong HistorySerial => Native.op3d_history_serial(_h);
    public bool UndoDiscard() => Native.op3d_undo_discard(_h) != 0;
    public Label HistoryLabel(bool redo, int i) => (Label)Native.op3d_history_label(_h, redo ? 1 : 0, i);

    /// <summary>Composite a premultiplied BGRA bitmap rendered by Avalonia.</summary>
    public void Blend(RenderTargetBitmap bmp, int dx, int dy, double opacity, int finish, Label label)
    {
        var (w, h) = (bmp.PixelSize.Width, bmp.PixelSize.Height);
        var buf = new byte[w * h * 4];
        fixed (byte* p = buf)
        {
            bmp.CopyPixels(new PixelRect(0, 0, w, h), (IntPtr)p, buf.Length, w * 4);
            Native.op3d_blend_bgra(_h, (IntPtr)p, w, h, dx, dy, (float)opacity, (uint)finish, (uint)label);
        }
    }

    // ------------------------------------------------------------- 3D objects

    private static void Fill9(float* t, double a, double b, double c, double d, double e, double f, double g, double h, double i)
    {
        t[0] = (float)a; t[1] = (float)b; t[2] = (float)c; t[3] = (float)d; t[4] = (float)e;
        t[5] = (float)f; t[6] = (float)g; t[7] = (float)h; t[8] = (float)i;
    }

    /// <summary>Add a model made of primitive / mask parts. Returns the new object id.</summary>
    public uint AddObject(IEnumerable<ModelPart> parts, Vector3D pos, Vector3D scale, double rotZ, Color color, int finish,
        Label label = Label.AddObject, double rotX = 0, double rotY = 0)
    {
        Native.op3d_obj_begin(_h);
        float* t = stackalloc float[9];
        foreach (var p in parts)
        {
            Fill9(t, p.X, p.Y, p.Z, p.Sx, p.Sy, p.Sz, p.Rx, p.Ry, p.Rz);
            Native.op3d_obj_part(_h, (uint)p.Prim, t, p.Color is { } c ? Pack(Color.Parse(c)) : 0);
        }
        Fill9(t, pos.X, pos.Y, pos.Z, scale.X, scale.Y, scale.Z, rotX, rotY, rotZ);
        return Native.op3d_obj_commit(_h, t, Pack(color), (uint)finish, (uint)label);
    }

    /// <summary>Add an object built from an alpha mask (extruded = sharp, inflated = soft).</summary>
    public uint AddMaskObject(byte[] mask, int mw, int mh, bool soft, Vector3D pos, Vector3D scale, double rotZ, Color color, int finish, Label label)
    {
        Native.op3d_obj_begin(_h);
        float* t = stackalloc float[9];
        Fill9(t, 0, 0, 0, 1, 1, 1, 0, 0, 0);
        int step = Math.Max(1, Math.Max(mw, mh) / 180);
        fixed (byte* m = mask)
            Native.op3d_obj_part_mask(_h, m, mw, mh, soft ? 1u : 0u, step, t, 0);
        Fill9(t, pos.X, pos.Y, pos.Z, scale.X, scale.Y, scale.Z, 0, 0, rotZ);
        return Native.op3d_obj_commit(_h, t, Pack(color), (uint)finish, (uint)label);
    }

    public void SetObject(uint id, Vector3D pos, Vector3D scale) =>
        Native.op3d_obj_set(_h, id, (float)pos.X, (float)pos.Y, (float)pos.Z, (float)scale.X, (float)scale.Y, (float)scale.Z);
    public void RotateObject(uint id, int axis, double deg) => Native.op3d_obj_rotate(_h, id, (uint)axis, (float)deg);
    public void SetMaterial(uint id, Color c, int finish) => Native.op3d_obj_material(_h, id, Pack(c), (uint)finish);
    public bool DeleteObject(uint id) => Native.op3d_obj_delete(_h, id) != 0;
    public uint DuplicateObject(uint id) => Native.op3d_obj_duplicate(_h, id);
    public uint PickObject(Point p) => Native.op3d_obj_pick(_h, (int)Math.Floor(p.X), (int)Math.Floor(p.Y));
    public void RotateObjectAbout(uint id, int axis, double deg, Vector3D c) =>
        Native.op3d_obj_rotate_about(_h, id, (uint)axis, (float)deg, (float)c.X, (float)c.Y, (float)c.Z);
    public uint CloneObject(uint id, double dx, double dy, uint group) => Native.op3d_obj_clone(_h, id, (float)dx, (float)dy, group);
    public bool RemoveObject(uint id) => Native.op3d_obj_remove(_h, id) != 0;
    public uint Group(uint id) => Native.op3d_obj_group(_h, id);
    public void SetGroup(uint id, uint group) => Native.op3d_obj_set_group(_h, id, group);

    /// <summary>All object ids, oldest first.</summary>
    public uint[] ObjectIds()
    {
        var ids = new uint[ObjectCount];
        fixed (uint* p = ids) Native.op3d_obj_ids(_h, p, ids.Length);
        return ids;
    }

    /// <summary>Make a canvas rectangle transparent (a lifted 2D selection leaves a hole).</summary>
    public void ClearRect(PixelRect r, Label label) => Native.op3d_clear_rect(_h, r.X, r.Y, r.Width, r.Height, (uint)label);

    /// <summary>Straight-alpha RGBA copy of a canvas rectangle (clipped to the canvas).</summary>
    public WriteableBitmap? CopyPixels(PixelRect r)
    {
        r = r.Intersect(new PixelRect(0, 0, Width, Height));
        if (r.Width <= 0 || r.Height <= 0) return null;
        var src = (byte*)Native.op3d_pixels(_h);
        var bmp = new WriteableBitmap(new PixelSize(r.Width, r.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        for (int y = 0; y < r.Height; y++)
            Buffer.MemoryCopy(src + ((r.Y + y) * Width + r.X) * 4, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, r.Width * 4);
        return bmp;
    }

    // engine-side 3D clipboard: copies survive deleting the originals ("cut")
    public void CopyObjects(IEnumerable<uint> ids)
    {
        Native.op3d_clip_clear(_h);
        foreach (var id in ids) Native.op3d_clip_add(_h, id);
    }

    public int ClipboardObjects => Native.op3d_clip_count(_h);

    /// <summary>Paste the copied objects (no history step of its own); returns the new ids.</summary>
    public uint[] PasteObjects(double dx, double dy)
    {
        int n = Native.op3d_clip_paste(_h, (float)dx, (float)dy);
        var all = ObjectIds();
        return all[^Math.Min(n, all.Length)..];
    }

    public bool Perspective { set => Native.op3d_set_perspective(_h, value ? 1 : 0); }

    public ObjInfo? Info(uint id)
    {
        float* o = stackalloc float[14];
        if (id == 0 || Native.op3d_obj_info(_h, id, o) == 0) return null;
        return new ObjInfo(new Vector3D(o[0], o[1], o[2]), new Vector3D(o[3], o[4], o[5]),
            new Rect(new Point(o[6], o[7]), new Point(o[8], o[9])),
            Color.FromRgb((byte)Math.Round(o[10] * 255), (byte)Math.Round(o[11] * 255), (byte)Math.Round(o[12] * 255)), (int)o[13]);
    }

    // ---------------------------------------------------------------- output

    public WriteableBitmap CreateBitmap() =>
        new(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

    /// <summary>Compose the display image and copy it into <paramref name="bmp"/>.</summary>
    public void CopyDisplay(WriteableBitmap bmp, int ss = 1)
    {
        var src = (byte*)Native.op3d_display(_h, ss);
        using var fb = bmp.Lock();
        int row = Width * 4;
        for (int y = 0; y < Height; y++)
            Buffer.MemoryCopy(src + y * row, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, row);
    }

    public void Render3D(WriteableBitmap bmp, double yaw, double pitch, double zoom, int ss)
    {
        var (w, h) = (bmp.PixelSize.Width, bmp.PixelSize.Height);
        var buf = new byte[w * h * 4];
        fixed (byte* p = buf)
        {
            Native.op3d_render_3d(_h, p, w, h, (float)yaw, (float)pitch, (float)zoom, ss);
            using var fb = bmp.Lock();
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(p + y * w * 4, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, w * 4);
        }
    }

    /// <summary>Final composed image (2x supersampled 3D) as a Skia image.</summary>
    public SKImage Snapshot(bool forceOpaque = false)
    {
        var src = Native.op3d_display(_h, 2);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var img = SKImage.FromPixelCopy(info, src, Width * 4);
        if (!forceOpaque) return img;
        using var _ = img;
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawImage(img, 0, 0);
        return surface.Snapshot();
    }

    public void LoadImage(string path)
    {
        using var src = SKBitmap.Decode(path) ?? throw new InvalidDataException("지원하지 않는 이미지 형식입니다.");
        LoadBitmap(src);
    }

    public void LoadBitmap(SKBitmap src)
    {
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var dst = new SKBitmap(info);
        using (var cv = new SKCanvas(dst))
        {
            cv.Clear(SKColors.Transparent);
            cv.DrawBitmap(src, 0, 0);
        }
        Native.op3d_load_rgba(_h, dst.GetPixels(), src.Width, src.Height);
    }

    /// <summary>World-space triangles: 9 floats per vertex (position, normal, rgb).</summary>
    public float[] ExportTriangles()
    {
        var p = Native.op3d_export_tris(_h, out int n);
        var arr = new float[n * 9];
        if (n > 0) new ReadOnlySpan<float>(p, n * 9).CopyTo(arr);
        return arr;
    }

    public byte[] SaveProject()
    {
        var p = Native.op3d_save(_h, out int len);
        return new ReadOnlySpan<byte>(p, len).ToArray();
    }

    public bool LoadProject(byte[] data)
    {
        fixed (byte* p = data) return Native.op3d_load(_h, p, data.Length) != 0;
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) Native.op3d_free(_h);
        _h = IntPtr.Zero;
    }
}

/// <summary>Undo history labels shared with the engine.</summary>
public enum Label : uint
{
    None = 0, Brush = 1, Eraser = 2, Fill = 3, Shape2D = 4, Sticker = 5, Text = 6, AddObject = 7, EditObject = 8,
    DeleteObject = 9, CanvasSize = 10, Crop = 11, Rotate = 12, Flip = 13, Paste = 14, Material = 15, Effect = 16, CanvasView = 17,
    Selection = 18, Group = 19, Ungroup = 20, Cut = 21,
}

public static class Labels
{
    public static string Name(Label l) => l switch
    {
        Label.Brush => "브러시",
        Label.Eraser => "지우개",
        Label.Fill => "채우기",
        Label.Shape2D => "2D 셰이프",
        Label.Sticker => "스티커",
        Label.Text => "텍스트",
        Label.AddObject => "3D 개체 추가",
        Label.EditObject => "3D 개체 변형",
        Label.DeleteObject => "3D 개체 삭제",
        Label.CanvasSize => "캔버스 크기",
        Label.Crop => "자르기",
        Label.Rotate => "회전",
        Label.Flip => "대칭",
        Label.Paste => "붙여넣기",
        Label.Material => "색 및 재질",
        Label.Effect => "효과",
        Label.CanvasView => "캔버스 설정",
        Label.Selection => "선택 영역",
        Label.Group => "그룹",
        Label.Ungroup => "그룹 해제",
        Label.Cut => "잘라내기",
        _ => "편집",
    };
}
