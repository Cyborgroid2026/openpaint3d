using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace OpenPaint3D;

public enum Tool { Brush, Shape2D, Shape3D, Sticker, Text, Effects, Canvas, Library, Select }

public sealed class ToolState
{
    public Tool Tool = Tool.Brush;

    // brushes (each brush remembers its own thickness and opacity)
    public int Brush;
    public bool Bucket;
    public bool Picking;
    public readonly double[] BrushSizes = { 5, 10, 15, 25, 3, 3, 20, 12, 40 };
    public readonly double[] BrushOpacities = { 1, 1, 1, 0.5, 1, 1, 1, 1, 1 };
    public double BrushSize { get => BrushSizes[Brush]; set => BrushSizes[Brush] = value; }
    public double Opacity { get => BrushOpacities[Brush]; set => BrushOpacities[Brush] = value; }
    public int Tolerance = 32;

    // shared colour and material finish (0 matte, 1 gloss, 2 dull metal, 3 polished metal)
    public Color Color = Color.Parse("#1F1F1F");
    public int Finish;

    // 2D shapes; Line >= 0 selects straight line (0) or 3/4/5-point curves (1..3)
    public int Shape;
    public int Line = -1;
    public double ShapeThickness = 4;
    public int LineType;
    public int FillMode;
    public Color FillColor = Color.Parse("#4FC3F7");
    public Color FillColor2 = Color.Parse("#7E57C2");
    public double ShapeOpacity = 1;

    // 3D shapes: basic primitive, people/animal model, or doodle (0 sharp, 1 soft, 2 tube)
    public int Basic;
    public Library.Model? Model;
    public int Doodle = -1;

    // stickers
    public int Sticker;
    public int Texture = -1;
    public IImage? Custom;
    public double StickerOpacity = 1;

    // text
    public string Font = "";
    public double FontSize = 48;
    public bool Bold, Italic, Underline, Text3D;
    public bool TextBg;
    public Color TextBgColor = Colors.White;
    public TextAlignment Align = TextAlignment.Left;

    // canvas / view
    public bool CropMode;
    public bool View3D;
    public bool HideCanvas;
    public bool Perspective = true;

    // select: clicking adds/removes objects instead of replacing the selection
    public bool MultiSelect;
}

public sealed class PaintSurface : Canvas
{
    public Engine Engine { get; }
    public ToolState S { get; }
    public double Zoom { get; private set; } = 1;
    public Pending2D? Pending { get; private set; }
    private readonly List<uint> _sel = new();
    /// <summary>First selected 3D object (0 = none); <see cref="SelectedIds"/> has all of them.</summary>
    public uint Selected => _sel.Count > 0 ? _sel[0] : 0;
    public IReadOnlyList<uint> SelectedIds => _sel;
    public Rect Frame => _frame;

    public event Action? Changed;
    public event Action? ZoomChanged;
    public event Action? SelectionChanged;
    public event Action<Color>? ColorPicked;

    private enum Drag
    {
        None, Stroke, Pan, NewBox, Doodle, PendMove, PendResize, PendRotate, CurvePoint,
        ObjMove, ObjScale, ObjRotX, ObjRotY, ObjRotZ, ObjDepth, Frame, Orbit, Marquee,
    }

    private WriteableBitmap? _bmp, _bmp3d;
    private Vector _pan;
    private bool _fitted;
    private Drag _drag;
    private Point _start, _last, _cur;          // view coordinates
    private Point _startCanvas;
    private Point? _hover;
    private int _handle;                         // which handle is being dragged
    private Rect _startBox;
    private double _startAngle;
    private ObjInfo _startObj;
    private Dictionary<uint, ObjInfo> _starts = new();
    private DateTime _lastNudge;
    private ulong _nudgeStep;
    private readonly List<Point> _doodle = new();
    private Rect _frame;
    private double _yaw = 25, _pitch = -18, _zoom3d = 1;
    private readonly TextBox _editor;
    private readonly DispatcherTimer _hq;
    private int _quality = 2;

    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#0078D7"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#2E3256"));
    private static readonly IPen Dash = new Pen(new SolidColorBrush(Color.Parse("#3A3A3A")), 1, new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly IPen Solid = new Pen(Ink, 1.2);
    private static readonly IPen GlyphPen = new Pen(Ink, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private const double KnobR = 17, KnobGap = 38;
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)), 1);
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
    private static readonly IBrush HandleFill = Brushes.White;
    private static readonly IBrush Checker = MakeChecker();

    public PaintSurface(Engine engine, ToolState state)
    {
        Engine = engine;
        S = state;
        ClipToBounds = true;
        Focusable = true;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Cross);
        _editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = EditorTint,
            BorderBrush = AccentBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 10,
            IsVisible = false,
        };
        _editor.TextChanged += (_, _) =>
        {
            if (Pending is TextItem t)
            {
                t.Text = _editor.Text ?? "";
                t.Fit();
                LayoutEditor();
                Redraw();
            }
        };
        _view.Owner = this;
        Children.Add(_view);
        Children.Add(_editor);
        _hq = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _hq.Tick += (_, _) =>
        {
            _hq.Stop();
            if (S.View3D) Render3D(2);
            else if (_quality != 2) { _quality = 2; RefreshDisplay(); }
        };
        _frame = new Rect(0, 0, W, H);
        Refresh(false);
    }

    private int W => Engine.Width;
    private int H => Engine.Height;

    private static IBrush MakeChecker()
    {
        var bmp = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        unsafe
        {
            using var fb = bmp.Lock();
            for (int y = 0; y < 16; y++)
            {
                var row = (byte*)fb.Address + y * fb.RowBytes;
                for (int x = 0; x < 16; x++)
                {
                    byte v = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 255 : 214);
                    row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = v;
                    row[x * 4 + 3] = 255;
                }
            }
        }
        return new ImageBrush(bmp) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute) };
    }

    // ---------------------------------------------------------------- refresh

    private void RefreshDisplay()
    {
        if (_bmp == null || _bmp.PixelSize.Width != W || _bmp.PixelSize.Height != H)
        {
            _bmp?.Dispose();
            _bmp = Engine.CreateBitmap();
        }
        if (S.HideCanvas)
        {
            // show only the 3D objects: draw with the background switched off, then restore it
            var v = Engine.View;
            var hidden = v;
            hidden.ShowBg = 0;
            Engine.SetView(hidden);
            Engine.CopyDisplay(_bmp, Engine.ObjectCount > 0 ? _quality : 2);
            Engine.SetView(v);
        }
        else Engine.CopyDisplay(_bmp, Engine.ObjectCount > 0 ? _quality : 2);
        Redraw();
    }

    /// <summary>Re-read the engine image. <paramref name="changed"/> also notifies listeners.</summary>
    public void Refresh(bool changed = true, bool interactive = false)
    {
        _quality = interactive ? 1 : 2;
        if (_frame.Width != W || _frame.Height != H) _frame = new Rect(0, 0, W, H);
        if (_sel.Any(id => Engine.Info(id) == null)) SelectMany(_sel.Where(id => Engine.Info(id) != null).ToList());
        RefreshDisplay();
        if (interactive) { _hq.Stop(); _hq.Start(); }
        if (S.View3D) Render3D(1);
        if (changed) Changed?.Invoke();
    }

    // ------------------------------------------------------------ zoom / view

    private Point Origin => new(
        Math.Round((Bounds.Width - W * Zoom) / 2 + _pan.X),
        Math.Round((Bounds.Height - H * Zoom) / 2 + _pan.Y));

    private Matrix CanvasToView => Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Origin.X, Origin.Y);
    private Point ToCanvas(Point p) => new((p.X - Origin.X) / Zoom, (p.Y - Origin.Y) / Zoom);
    private Point ToView(Point p) => new(Origin.X + p.X * Zoom, Origin.Y + p.Y * Zoom);
    private Rect ToView(Rect r) => new(ToView(r.TopLeft), ToView(r.BottomRight));

    public void SetZoom(double z)
    {
        Zoom = Math.Clamp(z, 0.05, 16);
        LayoutEditor();
        Redraw();
        ZoomChanged?.Invoke();
    }

    public void FitToView()
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) return;
        _pan = default;
        SetZoom(Math.Min(1, Math.Min((Bounds.Width - 80) / W, (Bounds.Height - 90) / H)));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _view.Width = e.NewSize.Width;
        _view.Height = e.NewSize.Height;
        if (!_fitted && e.NewSize.Width > 10)
        {
            _fitted = true;
            FitToView();
        }
        if (S.View3D) Render3D(1);
        LayoutEditor();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (S.View3D)
        {
            _zoom3d = Math.Clamp(_zoom3d * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.2, 6);
            Render3D(1);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Shift + wheel scrolls sideways
            _pan += new Vector(e.Delta.Y + e.Delta.X, 0) * 40;
        }
        else if (e.Delta.Y != 0)
        {
            // like Paint 3D, the wheel zooms around the pointer
            var pos = e.GetPosition(this);
            var before = ToCanvas(pos);
            SetZoom(Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
            _pan += pos - ToView(before);
        }
        LayoutEditor();
        Redraw();
        e.Handled = true;
    }

    // ------------------------------------------------------------- 3D view

    public void SetView3D(bool on)
    {
        CommitPending();
        Select(0);
        S.View3D = on;
        if (on) Render3D(1);
        Cursor = new Cursor(on ? StandardCursorType.SizeAll : StandardCursorType.Cross);
        Redraw();
    }

    private void Render3D(int ss)
    {
        int w = Math.Max(1, (int)Bounds.Width), h = Math.Max(1, (int)Bounds.Height);
        if (_bmp3d == null || _bmp3d.PixelSize.Width != w || _bmp3d.PixelSize.Height != h)
        {
            _bmp3d?.Dispose();
            _bmp3d = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        }
        Engine.Render3D(_bmp3d, _yaw, _pitch, _zoom3d, ss);
        if (ss == 1) { _hq.Stop(); _hq.Start(); }
        Redraw();
    }

    public void Reset3DView()
    {
        (_yaw, _pitch, _zoom3d) = (25, -18, 1);
        Render3D(1);
    }

    // ----------------------------------------------------------------- render

    /// <summary>Panels cannot override Render, so drawing happens in this child.</summary>
    private sealed class SurfaceView : Control
    {
        public PaintSurface Owner = null!;
        public override void Render(DrawingContext ctx) => Owner.RenderSurface(ctx);
    }

    private readonly SurfaceView _view = new();

    /// <summary>Repaint the canvas view.</summary>
    public void Redraw() => _view.InvalidateVisual();

    private void RenderSurface(DrawingContext ctx)
    {
        if (S.View3D)
        {
            if (_bmp3d != null) ctx.DrawImage(_bmp3d, new Rect(_bmp3d.Size));
            return;
        }
        if (_bmp == null) return;
        var o = Origin;
        var dest = new Rect(o.X, o.Y, W * Zoom, H * Zoom);
        DrawWorkspace(ctx, dest);
        if (!S.HideCanvas)
        {
            ctx.FillRectangle(Shadow, dest.Translate(new Vector(0, 2)).Inflate(1));
            if (Engine.View.ShowBg == 0) ctx.FillRectangle(Checker, dest);
        }
        using (ctx.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality,
        }))
            ctx.DrawImage(_bmp, new Rect(0, 0, W, H), dest);

        // editable 2D item
        if (Pending is { } p && !(_editor.IsVisible && p is TextItem))
        {
            using (ctx.PushTransform(p.Transform * CanvasToView))
            using (ctx.PushOpacity(p.Opacity))
                p.Draw(ctx, p.Box);
        }
        if (Pending is { } q) DrawPendingHandles(ctx, q);

        if (_drag == Drag.Doodle && _doodle.Count > 1)
        {
            bool tube = S.Doodle == 2;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(ToView(_doodle[0]), !tube);
                foreach (var pt in _doodle.Skip(1)) c.LineTo(ToView(pt));
                c.EndFigure(!tube);
            }
            if (tube)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(S.Color, 0.5), TubeWidth * Zoom, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
            else
                ctx.DrawGeometry(new SolidColorBrush(S.Color, 0.35), new Pen(new SolidColorBrush(S.Color), 2), g);
        }

        if (_drag is Drag.NewBox or Drag.Marquee)
        {
            var box = new Rect(_start, _cur).Normalize();
            if (S.Tool == Tool.Shape3D)
            {
                ctx.DrawRectangle(null, Dash, box);
                ctx.DrawEllipse(null, Dash, box.Center, box.Width / 2, box.Height / 2);
            }
            else ctx.DrawRectangle(null, Dash, box);
        }

        if (SelectionInfo() is { } info) DrawGizmo(ctx, info);

        if (S.Tool == Tool.Canvas) DrawFrame(ctx, dest);

        if (_hover is { } h && S.Tool == Tool.Brush && !S.Bucket && !S.Picking && _drag is Drag.None or Drag.Stroke)
        {
            var r = Math.Max(1, S.BrushSize * Zoom / 2);
            ctx.DrawEllipse(null, CursorPen, h, r, r);
        }
    }

    private static readonly IBrush Wall = new SolidColorBrush(Color.Parse("#B4B6B6"));

    /// <summary>Grey studio backdrop with a lighter "floor" band under the canvas.</summary>
    private void DrawWorkspace(DrawingContext ctx, Rect dest)
    {
        var all = new Rect(Bounds.Size);
        ctx.FillRectangle(Wall, all);
        double top = Math.Min(Math.Max(dest.Bottom - 6, all.Height * 0.35), all.Height - 20);
        var floor = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#B4B6B6"), 0),
                new GradientStop(Color.Parse("#D3D5D5"), 0.12),
                new GradientStop(Color.Parse("#C2C4C3"), 0.45),
                new GradientStop(Color.Parse("#B9BBBA"), 1),
            },
        };
        ctx.FillRectangle(floor, new Rect(0, top, all.Width, Math.Max(1, all.Height - top)));
    }

    private static void Handle(DrawingContext ctx, Point p, bool round = false)
    {
        if (round) ctx.DrawEllipse(HandleFill, Solid, p, 5, 5);
        else ctx.DrawRectangle(HandleFill, Solid, new Rect(p.X - 4, p.Y - 4, 8, 8));
    }

    /// <summary>Round action knob (white disc, ink outline, glyph drawn on a 24px grid).</summary>
    private static void Knob(DrawingContext ctx, Point p, string glyph)
    {
        ctx.DrawEllipse(HandleFill, Solid, p, KnobR, KnobR);
        var g = Geometry.Parse(glyph);
        using (ctx.PushTransform(Matrix.CreateTranslation(p.X - 12, p.Y - 12)))
            ctx.DrawGeometry(null, GlyphPen, g);
    }

    private static readonly string RotateGlyph = "M7,9 A6,6 0 1 1 8,16 M7,5 V9 H11 M12,12 V12.2";
    private static readonly string StampGlyph = "M9,5 H15 V10 C15,11 18,12 18,14 H6 C6,12 9,11 9,10 Z M5,17 H19 V19 H5 Z";
    private static readonly string CheckGlyph = "M6,12.5 L10.5,17 L18,8";

    // ---------------------------------------------------- pending item handles

    private Point[] PendingCorners(Pending2D p)
    {
        var b = p.Box;
        var local = new[]
        {
            b.TopLeft, b.TopRight, b.BottomRight, b.BottomLeft,
            new Point(b.Center.X, b.Top), new Point(b.Right, b.Center.Y), new Point(b.Center.X, b.Bottom), new Point(b.Left, b.Center.Y),
        };
        return local.Select(l => ToView(p.ToCanvas(l))).ToArray();
    }

    /// <summary>Knob beyond the middle of an edge: 0 top (rotate), 1 left (stamp), 2 right (done).</summary>
    private Point PendingKnob(Pending2D p, int which)
    {
        var b = p.Box;
        var local = which switch { 0 => new Point(b.Center.X, b.Top), 1 => new Point(b.Left, b.Center.Y), _ => new Point(b.Right, b.Center.Y) };
        var edge = ToView(p.ToCanvas(local));
        var center = ToView(p.Center);
        var dir = edge - center;
        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        var fallback = which switch { 0 => new Vector(0, -1), 1 => new Vector(-1, 0), _ => new Vector(1, 0) };
        return edge + (len < 1 ? fallback : dir / len) * KnobGap;
    }

    private Point RotateHandle(Pending2D p) => PendingKnob(p, 0);

    private void DrawPendingHandles(DrawingContext ctx, Pending2D p)
    {
        if (p is CurveItem c)
        {
            ctx.DrawGeometry(null, Dash, new PolylineGeometry(c.Points.Select(ToView), false));
            foreach (var pt in c.Points) Handle(ctx, ToView(pt), true);
            return;
        }
        var k = PendingCorners(p);
        ctx.DrawGeometry(null, Dash, new PolylineGeometry(new[] { k[0], k[1], k[2], k[3] }, true));
        if (_editor.IsVisible) return;
        for (int i = 0; i < 8; i++) Handle(ctx, k[i]);
        string[] glyphs = { RotateGlyph, StampGlyph, CheckGlyph };
        int[] edges = { 4, 7, 5 };
        for (int i = 0; i < 3; i++)
        {
            var kp = PendingKnob(p, i);
            var from = k[edges[i]];
            var dir = kp - from;
            var len = Math.Max(1, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            ctx.DrawLine(Solid, from, kp - dir / len * KnobR);
            Knob(ctx, kp, glyphs[i]);
        }
    }

    // ------------------------------------------------------------ 3D gizmo

    private (Rect box, Point[] handles) GizmoLayout(ObjInfo info)
    {
        var r = ToView(info.Screen);
        if (r.Width < 24) r = r.Inflate(new Thickness((24 - r.Width) / 2, 0));
        if (r.Height < 24) r = r.Inflate(new Thickness(0, (24 - r.Height) / 2));
        var c = r.Center;
        return (r, new[]
        {
            r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft,           // 0-3 scale
            new Point(r.Right + KnobGap, c.Y),                              // 4 rotate around X (right)
            new Point(c.X, r.Bottom + KnobGap),                             // 5 rotate around Y (bottom)
            new Point(c.X, r.Top - KnobGap),                                // 6 rotate around Z (top)
            new Point(r.Left - KnobGap, c.Y),                               // 7 depth (left)
        });
    }

    private static readonly string[] GizmoGlyphs =
    {
        "M14,6 A3,6 0 1 0 14,18 M12,4 L14,6 L11,8 M12,12 V12.2",      // X rotation (vertical tumble)
        "M6,10 A6,3 0 1 0 18,10 M4,12 L6,10 L8,13 M12,12 V12.2",      // Y rotation (spin)
        RotateGlyph,                                                   // Z rotation
        "M8,7 H16 L18,17 H6 Z M12,9 V15 M10,13 L12,15 L14,13",        // depth (push back / pull forward)
    };

    private void DrawGizmo(DrawingContext ctx, ObjInfo info)
    {
        var (r, hs) = GizmoLayout(info);
        ctx.DrawRectangle(null, Dash, r);
        for (int i = 0; i < 4; i++) Handle(ctx, hs[i]);
        var c = r.Center;
        Point[] edges = { new(r.Right, c.Y), new(c.X, r.Bottom), new(c.X, r.Top), new(r.Left, c.Y) };
        for (int i = 0; i < 4; i++)
        {
            var p = hs[4 + i];
            var dir = p - edges[i];
            var len = Math.Max(1, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            Handle(ctx, edges[i]);
            ctx.DrawLine(Solid, edges[i], p - dir / len * KnobR);
            Knob(ctx, p, GizmoGlyphs[i]);
        }
    }

    // --------------------------------------------------------- canvas frame

    private Point[] FrameHandles()
    {
        var r = ToView(_frame);
        return new[]
        {
            r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft,
            new Point(r.Center.X, r.Top), new Point(r.Right, r.Center.Y), new Point(r.Center.X, r.Bottom), new Point(r.Left, r.Center.Y),
        };
    }

    private void DrawFrame(DrawingContext ctx, Rect dest)
    {
        var r = ToView(_frame);
        if (S.CropMode)
        {
            using (ctx.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(dest), new RectangleGeometry(r))))
                ctx.FillRectangle(Dim, dest);
        }
        if (S.CropMode || _drag == Drag.Frame) ctx.DrawRectangle(null, Dash, r);
        foreach (var h in FrameHandles()) Handle(ctx, h);
        if (_drag == Drag.Frame)
        {
            var txt = new FormattedText($"{(int)Math.Round(_frame.Width)} × {(int)Math.Round(_frame.Height)}",
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
            var at = new Point(r.Right - txt.Width - 8, r.Bottom + 8);
            ctx.FillRectangle(AccentBrush, new Rect(at.X - 4, at.Y - 2, txt.Width + 8, txt.Height + 4), 3);
            ctx.DrawText(txt, at);
        }
    }

    public void BeginCrop()
    {
        CommitPending();
        S.CropMode = true;
        _frame = new Rect(0, 0, W, H);
        Redraw();
    }

    public void EndCrop(bool apply)
    {
        if (apply && S.CropMode)
        {
            var r = _frame;
            Engine.Reframe(new PixelRect((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Max(1, Math.Round(r.Width)), (int)Math.Max(1, Math.Round(r.Height))));
            _pan = default;
        }
        S.CropMode = false;
        _frame = new Rect(0, 0, W, H);
        Refresh(apply);
        if (apply) FitToView();
    }

    // ---------------------------------------------------------- hit testing

    private static int HitList(Point p, Point[] pts, double r = 9)
    {
        for (int i = pts.Length - 1; i >= 0; i--)
            if (Math.Abs(pts[i].X - p.X) <= r && Math.Abs(pts[i].Y - p.Y) <= r) return i;
        return -1;
    }

    // ---------------------------------------------------------------- pointer

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Source is TextBox) return;
        Focus();
        var pt = e.GetCurrentPoint(this);
        var pos = pt.Position;
        _start = _last = _cur = pos;
        _startCanvas = ToCanvas(pos);
        if (pt.Properties.IsMiddleButtonPressed)
        {
            Begin(Drag.Pan, e);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        if (S.View3D)
        {
            Begin(Drag.Orbit, e);
            return;
        }
        var c = _startCanvas;

        if (S.Picking)
        {
            if (Engine.Pick(c) is { } col) ColorPicked?.Invoke(col);
            return;
        }

        // canvas frame handles (canvas tab)
        if (S.Tool == Tool.Canvas)
        {
            _handle = HitList(pos, FrameHandles());
            if (_handle >= 0)
            {
                _startBox = _frame;
                Begin(Drag.Frame, e);
                return;
            }
            if (S.CropMode && ToView(_frame).Contains(pos))
            {
                _handle = 8;
                _startBox = _frame;
                Begin(Drag.Frame, e);
            }
            return;
        }

        // editable 2D item
        if (Pending is { } p)
        {
            if (p is CurveItem curve)
            {
                _handle = HitList(pos, curve.Points.Select(ToView).ToArray());
                if (_handle >= 0) { Begin(Drag.CurvePoint, e); return; }
            }
            else if (!_editor.IsVisible)
            {
                if (Distance(pos, RotateHandle(p)) <= KnobR)
                {
                    _startAngle = p.Angle;
                    Begin(Drag.PendRotate, e);
                    return;
                }
                if (Distance(pos, PendingKnob(p, 1)) <= KnobR)
                {
                    StampPending();
                    e.Handled = true;
                    return;
                }
                if (Distance(pos, PendingKnob(p, 2)) <= KnobR)
                {
                    CommitPending();
                    e.Handled = true;
                    return;
                }
                _handle = HitList(pos, PendingCorners(p));
                if (_handle >= 0)
                {
                    _startBox = p.Box;
                    Begin(Drag.PendResize, e);
                    return;
                }
            }
            if (p.Contains(c) && !(_editor.IsVisible && p is TextItem))
            {
                _startBox = p.Box;
                Begin(Drag.PendMove, e);
                return;
            }
            if (_editor.IsVisible && p is TextItem && ToView(p.Box).Contains(pos)) return;
            // clicking elsewhere only confirms the item (like Paint 3D); it does not start a new one
            CommitPending();
            e.Handled = true;
            return;
        }

        // 3D object gizmo / selection
        bool adding = S.MultiSelect || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (SelectionInfo() is { } info && !adding)
        {
            var (r, hs) = GizmoLayout(info);
            int hi = HitList(pos, hs[..4], 7);
            if (hi < 0)
                for (int i = 4; i < 8; i++)
                    if (Distance(pos, hs[i]) <= KnobR) hi = i;
            if (hi >= 0)
            {
                _handle = hi;
                StartObjects(info);
                Engine.Checkpoint(Label.EditObject);
                Begin(hi < 4 ? Drag.ObjScale : hi == 4 ? Drag.ObjRotX : hi == 5 ? Drag.ObjRotY : hi == 6 ? Drag.ObjRotZ : Drag.ObjDepth, e);
                return;
            }
            if (_sel.Contains(Engine.PickObject(c)) || (r.Contains(pos) && S.Tool is Tool.Shape3D or Tool.Library or Tool.Select))
            {
                StartObjects(info);
                Engine.Checkpoint(Label.EditObject);
                Begin(Drag.ObjMove, e);
                return;
            }
        }
        if (S.Tool is Tool.Shape3D or Tool.Library or Tool.Effects or Tool.Select && Engine.PickObject(c) is var hit and > 0)
        {
            if (adding)
            {
                ToggleSelect(hit);
                e.Handled = true;
                return;
            }
            Select(hit);
            StartObjects(SelectionInfo()!.Value);
            Engine.Checkpoint(Label.EditObject);
            Begin(Drag.ObjMove, e);
            return;
        }
        if (Selected != 0 && !adding) Select(0);

        switch (S.Tool)
        {
            case Tool.Brush when S.Bucket:
                if (Engine.Fill(c, S.Color, S.Tolerance)) Refresh();
                break;
            case Tool.Brush:
                Engine.StrokeBegin(S.Brush, S.Color, S.BrushSize, S.Opacity, S.Finish, c);
                Begin(Drag.Stroke, e);
                Refresh(false);
                break;
            case Tool.Shape3D when S.Doodle >= 0:
                _doodle.Clear();
                _doodle.Add(c);
                Begin(Drag.Doodle, e);
                break;
            case Tool.Shape2D or Tool.Shape3D or Tool.Sticker or Tool.Text:
                Begin(Drag.NewBox, e);
                break;
            case Tool.Select when !adding:
                Begin(Drag.Marquee, e);
                break;
        }
        e.Handled = true;
    }

    private void Begin(Drag d, PointerPressedEventArgs e)
    {
        _drag = d;
        e.Pointer.Capture(this);
        e.Handled = true;
        Redraw();
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _hover = pos;
        var delta = pos - _last;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var c = ToCanvas(pos);
        switch (_drag)
        {
            case Drag.Pan:
                _pan += delta;
                LayoutEditor();
                break;
            case Drag.Orbit:
                _yaw += delta.X * 0.4;
                _pitch = Math.Clamp(_pitch + delta.Y * 0.4, -85, 85);
                Render3D(1);
                break;
            case Drag.Stroke:
                foreach (var p in e.GetIntermediatePoints(this)) Engine.StrokeTo(ToCanvas(p.Position));
                Refresh(false);
                break;
            case Drag.Doodle:
                if (Distance(ToView(_doodle[^1]), pos) > 3) _doodle.Add(c);
                break;
            case Drag.NewBox:
                _cur = shift ? SquareFrom(_start, pos) : pos;
                break;
            case Drag.Marquee:
                _cur = pos;
                break;
            case Drag.PendMove when Pending is CurveItem curve:
                curve.Translate(delta / Zoom);
                break;
            case Drag.PendMove when Pending is { } p:
                p.Box = _startBox.Translate(c - _startCanvas);
                LayoutEditor();
                break;
            case Drag.PendResize when Pending is { } p:
                ResizePending(p, c, shift);
                LayoutEditor();
                break;
            case Drag.PendRotate when Pending is { } p:
                var center = ToView(p.Center);
                double a0 = Math.Atan2(_start.Y - center.Y, _start.X - center.X), a1 = Math.Atan2(pos.Y - center.Y, pos.X - center.X);
                double ang = _startAngle + (a1 - a0) * 180 / Math.PI;
                p.Angle = shift ? Math.Round(ang / 15) * 15 : ang;
                break;
            case Drag.CurvePoint when Pending is CurveItem curve:
                curve.Points[_handle] = c;
                curve.UpdateBox();
                break;
            case Drag.ObjMove:
                var d = c - _startCanvas;
                foreach (var (id, o) in _starts)
                    Engine.SetObject(id, new Vector3D(o.Pos.X + d.X, o.Pos.Y + d.Y, o.Pos.Z), o.Scale);
                Refresh(false, true);
                break;
            case Drag.ObjScale:
                // every selected object grows about the centre of the whole selection
                var oc = ToView(_startObj.Screen.Center);
                double k = Math.Max(0.05, Distance(pos, oc) / Math.Max(1, Distance(_start, oc)));
                var mid = _startObj.Pos;
                foreach (var (id, o) in _starts)
                    Engine.SetObject(id, new Vector3D(mid.X + (o.Pos.X - mid.X) * k, mid.Y + (o.Pos.Y - mid.Y) * k, mid.Z + (o.Pos.Z - mid.Z) * k),
                        new Vector3D(o.Scale.X * k, o.Scale.Y * k, o.Scale.Z * k));
                Refresh(false, true);
                break;
            case Drag.ObjRotX:
                RotateSelection(0, -delta.Y * 0.6);
                break;
            case Drag.ObjRotY:
                RotateSelection(1, -delta.X * 0.6);
                break;
            case Drag.ObjRotZ:
                var zc = ToView(_startObj.Screen.Center);
                double b0 = Math.Atan2(_last.Y - zc.Y, _last.X - zc.X), b1 = Math.Atan2(pos.Y - zc.Y, pos.X - zc.X);
                RotateSelection(2, (b1 - b0) * 180 / Math.PI);
                break;
            case Drag.ObjDepth:
                foreach (var id in _sel)
                    if (Engine.Info(id) is { } i2)
                        Engine.SetObject(id, new Vector3D(i2.Pos.X, i2.Pos.Y, i2.Pos.Z + delta.Y / Zoom), i2.Scale);
                Refresh(false, true);
                break;
            case Drag.Frame:
                _frame = DragFrame(_startBox, _handle, c - _startCanvas, shift);
                break;
        }
        _last = pos;
        Redraw();
    }

    private static Point SquareFrom(Point a, Point b)
    {
        var d = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        return new Point(a.X + Math.Sign(b.X - a.X) * d, a.Y + Math.Sign(b.Y - a.Y) * d);
    }

    private Rect DragFrame(Rect r, int handle, Vector d, bool keepAspect)
    {
        double x0 = r.Left, y0 = r.Top, x1 = r.Right, y1 = r.Bottom;
        switch (handle)
        {
            case 0: x0 += d.X; y0 += d.Y; break;
            case 1: x1 += d.X; y0 += d.Y; break;
            case 2: x1 += d.X; y1 += d.Y; break;
            case 3: x0 += d.X; y1 += d.Y; break;
            case 4: y0 += d.Y; break;
            case 5: x1 += d.X; break;
            case 6: y1 += d.Y; break;
            case 7: x0 += d.X; break;
            default: x0 += d.X; x1 += d.X; y0 += d.Y; y1 += d.Y; break;
        }
        if (keepAspect && handle < 4)
        {
            double ar = r.Width / Math.Max(1, r.Height), w = Math.Abs(x1 - x0), h = w / ar;
            if (handle is 0 or 1) y0 = y1 - h; else y1 = y0 + h;
        }
        var res = new Rect(new Point(Math.Round(x0), Math.Round(y0)), new Point(Math.Round(x1), Math.Round(y1))).Normalize();
        if (S.CropMode) res = res.Intersect(new Rect(0, 0, W, H));
        return new Rect(res.X, res.Y, Math.Max(1, res.Width), Math.Max(1, res.Height));
    }

    private void ResizePending(Pending2D p, Point canvas, bool shift)
    {
        // work in the frame of the box as it was when the drag started
        var b = _startBox;
        var c0 = b.Center;
        var t = Matrix.CreateTranslation(-c0.X, -c0.Y) * Matrix.CreateScale(p.FlipX ? -1 : 1, p.FlipY ? -1 : 1) *
                Matrix.CreateRotation(p.Angle * Math.PI / 180) * Matrix.CreateTranslation(c0.X, c0.Y);
        var local = t.Invert().Transform(canvas);
        double x0 = b.Left, y0 = b.Top, x1 = b.Right, y1 = b.Bottom;
        switch (_handle)
        {
            case 0: x0 = local.X; y0 = local.Y; break;
            case 1: x1 = local.X; y0 = local.Y; break;
            case 2: x1 = local.X; y1 = local.Y; break;
            case 3: x0 = local.X; y1 = local.Y; break;
            case 4: y0 = local.Y; break;
            case 5: x1 = local.X; break;
            case 6: y1 = local.Y; break;
            case 7: x0 = local.X; break;
        }
        if ((p.KeepAspect ^ shift) && _handle < 4)
        {
            double ar = b.Width / Math.Max(1, b.Height);
            double w = Math.Max(4, Math.Abs(x1 - x0)), h = w / ar;
            if (_handle is 0 or 1) y0 = y1 - h; else y1 = y0 + h;
            if (_handle is 0 or 3) x0 = x1 - w; else x1 = x0 + w;
        }
        var nb = new Rect(new Point(x0, y0), new Point(x1, y1)).Normalize();
        if (nb.Width < 4 || nb.Height < 4) return;
        // a box rotated about its own centre lands exactly where nb sits in the start frame
        var nc = t.Transform(nb.Center);
        p.Box = new Rect(nc.X - nb.Width / 2, nc.Y - nb.Height / 2, nb.Width, nb.Height);
        if (p is TextItem tx) tx.Fit();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var d = _drag;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        switch (d)
        {
            case Drag.Stroke:
                Engine.StrokeEnd();
                Refresh();
                break;
            case Drag.Orbit:
                Render3D(2);
                break;
            case Drag.NewBox:
                CreateFromBox(new Rect(ToCanvas(_start), ToCanvas(_cur)).Normalize());
                break;
            case Drag.Marquee:
                LiftSelection(new Rect(ToCanvas(_start), ToCanvas(_cur)).Normalize());
                break;
            case Drag.Doodle:
                CreateDoodle();
                break;
            case Drag.ObjMove or Drag.ObjScale or Drag.ObjRotX or Drag.ObjRotY or Drag.ObjRotZ or Drag.ObjDepth:
                Refresh();
                SelectionChanged?.Invoke();
                break;
            case Drag.Frame when !S.CropMode:
                var r = _frame;
                if (r.X != 0 || r.Y != 0 || (int)r.Width != W || (int)r.Height != H)
                {
                    Engine.Reframe(new PixelRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
                    Refresh();
                }
                _frame = new Rect(0, 0, W, H);
                break;
        }
        Redraw();
    }

    // -------------------------------------------------------------- creation

    private void CreateFromBox(Rect box)
    {
        bool tiny = box.Width < 4 && box.Height < 4;
        switch (S.Tool)
        {
            case Tool.Shape2D when S.Line >= 0:
                if (tiny) return;
                var a = ToCanvas(_start);
                var b = ToCanvas(_cur);
                var curve = new CurveItem();
                int n = S.Line == 0 ? 2 : S.Line + 2;
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)(n - 1);
                    curve.Points.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                }
                curve.Apply(S);
                curve.UpdateBox();
                SetPending(curve);
                break;
            case Tool.Shape2D:
                if (tiny) return;
                var shape = new ShapeItem { Kind = S.Shape, Box = box };
                shape.Apply(S);
                SetPending(shape);
                break;
            case Tool.Sticker:
                if (tiny) box = new Rect(box.X - 100, box.Y - 100, 200, 200);
                var item = new ImageItem { Box = box };
                if (S.Texture >= 0) { item.Image = Textures.Get(S.Texture); item.Tile = true; }
                else if (S.Custom != null)
                {
                    item.Image = S.Custom;
                    var ar = S.Custom.Size.Width / Math.Max(1, S.Custom.Size.Height);
                    item.Box = new Rect(box.X, box.Y, box.Width, box.Width / ar);
                }
                else item.Sticker = S.Sticker;
                item.Apply(S);
                SetPending(item);
                break;
            case Tool.Text:
                if (tiny) box = new Rect(box.X, box.Y, 400, S.FontSize * 1.6);
                var text = new TextItem { Box = new Rect(box.X, box.Y, Math.Max(box.Width, 60), Math.Max(box.Height, S.FontSize * 1.4)) };
                text.Apply(S);
                SetPending(text);
                OpenEditor();
                break;
            case Tool.Shape3D:
                if (tiny) box = new Rect(box.X - 110, box.Y - 110, 220, 220);
                Create3D(box);
                break;
        }
    }

    private void Create3D(Rect box)
    {
        uint id;
        if (S.Model is { } m)
        {
            double s = Math.Max(box.Width, box.Height);
            id = Engine.AddObject(m.Parts, new Vector3D(box.Center.X, box.Center.Y, -s / 2 - 1), new Vector3D(box.Width, box.Height, s),
                0, S.Color, S.Finish);
        }
        else
        {
            double depth = Math.Min(box.Width, box.Height);
            var part = new ModelPart(Library.BasicPrims[S.Basic], 0, 0, 0, 1, 1, 1);
            id = Engine.AddObject(new[] { part }, new Vector3D(box.Center.X, box.Center.Y, -depth / 2 - 1),
                new Vector3D(box.Width, box.Height, depth), 0, S.Color, S.Finish);
        }
        Refresh();
        Select(id);
    }

    /// <summary>Insert a library model in the middle of the visible canvas.</summary>
    public void PlaceModel(Library.Model m)
    {
        CommitPending();
        var c = new Point(W / 2.0, H / 2.0);
        double s = Math.Min(320, Math.Min(W, H) * 0.6);
        var id = Engine.AddObject(m.Parts, new Vector3D(c.X, c.Y, -s / 2 - 1), new Vector3D(s, s, s), 0,
            Color.Parse(m.Color), m.Finish);
        Refresh();
        Select(id);
    }

    private const double TubeWidth = 36;

    private void CreateDoodle()
    {
        var pts = _doodle.ToList();
        _doodle.Clear();
        bool tube = S.Doodle == 2;
        if (pts.Count < (tube ? 2 : 3)) return;
        double x0 = pts.Min(p => p.X), y0 = pts.Min(p => p.Y), x1 = pts.Max(p => p.X), y1 = pts.Max(p => p.Y);
        if (tube)
        {
            // a tube is the stroke itself, inflated round
            double r = TubeWidth / 2;
            (x0, y0, x1, y1) = (x0 - r, y0 - r, x1 + r, y1 + r);
        }
        else if (x1 - x0 < 8 || y1 - y0 < 8) return;
        double k = Math.Min(1.0, 260 / Math.Max(x1 - x0, y1 - y0));
        int mw = Math.Max(2, (int)Math.Ceiling((x1 - x0) * k)), mh = Math.Max(2, (int)Math.Ceiling((y1 - y0) * k));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point((pts[0].X - x0) * k, (pts[0].Y - y0) * k), !tube);
            foreach (var p in pts.Skip(1)) c.LineTo(new Point((p.X - x0) * k, (p.Y - y0) * k));
            c.EndFigure(!tube);
        }
        var mask = tube
            ? RenderMask(mw, mh, dc => dc.DrawGeometry(null, new Pen(Brushes.White, TubeWidth * k, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g))
            : RenderMask(mw, mh, dc => dc.DrawGeometry(Brushes.White, null, g));
        bool soft = S.Doodle >= 1;
        double w = x1 - x0, h = y1 - y0, depth = tube ? TubeWidth : Math.Min(w, h) * (soft ? 0.5 : 0.3);
        var id = Engine.AddMaskObject(mask, mw, mh, soft, new Vector3D((x0 + x1) / 2, (y0 + y1) / 2, -depth / 2 - 1),
            new Vector3D(w, h, depth), 0, S.Color, S.Finish, Label.AddObject);
        Refresh();
        Select(id);
    }

    private static unsafe byte[] RenderMask(int mw, int mh, Action<DrawingContext> draw)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(mw, mh), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext()) draw(dc);
        var buf = new byte[mw * mh * 4];
        fixed (byte* p = buf) rtb.CopyPixels(new PixelRect(0, 0, mw, mh), (IntPtr)p, buf.Length, mw * 4);
        var mask = new byte[mw * mh];
        for (int i = 0; i < mask.Length; i++) mask[i] = buf[i * 4 + 3];
        return mask;
    }

    // --------------------------------------------------------- pending items

    public void SetPending(Pending2D? p)
    {
        Pending = p;
        if (p is not TextItem) CloseEditor();
        SelectionChanged?.Invoke();
        Redraw();
    }

    /// <summary>Re-apply panel settings to the item being edited.</summary>
    public void SyncPending()
    {
        if (Pending is { } p)
        {
            if (p is TextItem t)
            {
                t.Apply(S);
                t.Fit();
                StyleEditor();
                LayoutEditor();
            }
            else p.Apply(S);
            Redraw();
        }
    }

    public void CommitPending()
    {
        var p = Pending;
        if (p == null) return;
        CloseEditor();
        Pending = null;
        if (p is TextItem t && string.IsNullOrWhiteSpace(t.Text))
        {
            SelectionChanged?.Invoke();
            Redraw();
            return;
        }
        if (p is TextItem { Is3D: true } t3)
        {
            Make3D(t3);
            return;
        }
        using (var rtb = p.Rasterize(out var origin))
            Engine.Blend(rtb, origin.X, origin.Y, p.Opacity, p.Finish, p.Label);
        Refresh();
        SelectionChanged?.Invoke();
    }

    /// <summary>Stamp a copy of the item onto the canvas and keep editing it (Paint 3D's stamp knob).</summary>
    public void StampPending()
    {
        if (Pending is not { } p || p is TextItem) return;
        using (var rtb = p.Rasterize(out var origin))
            Engine.Blend(rtb, origin.X, origin.Y, p.Opacity, p.Finish, p.Label == Label.None ? Label.Selection : p.Label);
        if (p is CurveItem c) c.Translate(new Vector(24, 24));
        else p.Box = p.Box.Translate(new Vector(24, 24));
        Refresh();
    }

    /// <summary>
    /// Drop the item being edited. A lifted selection goes back where it came from when
    /// <paramref name="restore"/> is set (undo); otherwise its pixels are gone (delete).
    /// </summary>
    public void CancelPending(bool restore = true)
    {
        if (Pending is SelectionItem { Lifted: true } sel && restore)
        {
            if (Engine.HistorySerial != sel.LiftedAt)
            {
                // stamped since lifting: take back the last stamp and keep the selection floating
                Engine.Undo();
                Refresh();
                Redraw();
                return;
            }
            // nothing else happened: forget the lift, the pixels go back where they were
            Engine.UndoDiscard();
            Refresh();
        }
        CloseEditor();
        Pending = null;
        SelectionChanged?.Invoke();
        Redraw();
    }

    /// <summary>Pick up a rectangle of the canvas as an editable selection (select tool).</summary>
    public void LiftSelection(Rect r)
    {
        CommitPending();
        int x0 = (int)Math.Floor(Math.Max(0, r.X)), y0 = (int)Math.Floor(Math.Max(0, r.Y));
        int x1 = (int)Math.Ceiling(Math.Min(W, r.Right)), y1 = (int)Math.Ceiling(Math.Min(H, r.Bottom));
        if (x1 - x0 < 2 || y1 - y0 < 2) return;
        var pr = new PixelRect(x0, y0, x1 - x0, y1 - y0);
        if (Engine.CopyPixels(pr) is not { } img) return;
        Engine.ClearRect(pr, Label.Selection);
        Refresh();
        SetPending(new SelectionItem { Image = img, Lifted = true, LiftedAt = Engine.HistorySerial, Box = new Rect(x0, y0, pr.Width, pr.Height) });
    }

    /// <summary>Paste pixels as a floating selection in the middle of the canvas.</summary>
    public void BeginSelection(IImage img)
    {
        CommitPending();
        Select(0);
        double k = Math.Min(1, Math.Min(W * 0.9 / img.Size.Width, H * 0.9 / img.Size.Height));
        var size = new Size(img.Size.Width * k, img.Size.Height * k);
        SetPending(new SelectionItem { Image = img, Box = new Rect(new Point(Math.Round((W - size.Width) / 2), Math.Round((H - size.Height) / 2)), size) });
    }

    /// <summary>The selection as it looks now (rotation, flips and scale applied).</summary>
    public RenderTargetBitmap? RenderPending() => Pending?.Rasterize(out _);

    /// <summary>Move the edited item or the selected objects by a few canvas pixels (arrow keys).</summary>
    public bool Nudge(Vector d)
    {
        if (Pending is CurveItem c) c.Translate(d);
        else if (Pending is { } p)
        {
            if (_editor.IsVisible) return false;
            p.Box = p.Box.Translate(d);
        }
        else if (_sel.Count > 0)
        {
            // a run of key presses is one history step
            // a quick run of key presses is one history step, unless anything else touched the history
            if (DateTime.Now - _lastNudge > TimeSpan.FromSeconds(1) || Engine.HistorySerial != _nudgeStep)
            {
                Engine.Checkpoint(Label.EditObject);
                _nudgeStep = Engine.HistorySerial;
            }
            _lastNudge = DateTime.Now;
            foreach (var id in _sel)
                if (Engine.Info(id) is { } i)
                    Engine.SetObject(id, new Vector3D(i.Pos.X + d.X, i.Pos.Y + d.Y, i.Pos.Z), i.Scale);
            Refresh();
        }
        else return false;
        Redraw();
        return true;
    }

    public void SetPerspective(bool on)
    {
        S.Perspective = on;
        Engine.Perspective = on;
        if (S.View3D) Render3D(2);
    }

    /// <summary>Turn the editable 2D item into an extruded 3D object.</summary>
    public void Make3D(Pending2D? item = null)
    {
        var p = item ?? Pending;
        if (p == null) return;
        CloseEditor();
        Pending = null;
        var mask = p.Mask(out int mw, out int mh);
        double depth = p is TextItem t ? t.Size * 0.4 : Math.Min(p.Box.Width, p.Box.Height) * 0.25;
        var color = p switch
        {
            ShapeItem s when s.FillMode > 0 => s.Fill,
            ShapeItem s => s.Stroke,
            CurveItem c => c.Stroke,
            TextItem tt => tt.Color,
            _ => S.Color,
        };
        var id = Engine.AddMaskObject(mask, mw, mh, false,
            new Vector3D(p.Center.X, p.Center.Y, -depth / 2 - 1),
            new Vector3D(p.Box.Width * (p.FlipX ? -1 : 1), p.Box.Height * (p.FlipY ? -1 : 1), depth),
            p.Angle, color, p.Finish, Label.AddObject);
        Refresh();
        Select(id);
    }

    public void RotatePending(double deg)
    {
        if (Pending is CurveItem c)
        {
            var center = c.Box.Center;
            var m = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(deg * Math.PI / 180) * Matrix.CreateTranslation(center.X, center.Y);
            for (int i = 0; i < c.Points.Count; i++) c.Points[i] = m.Transform(c.Points[i]);
            c.UpdateBox();
        }
        else if (Pending is { } p) p.Angle += deg;
        Redraw();
    }

    public void FlipPending(bool horizontal)
    {
        if (Pending is CurveItem c)
        {
            var b = c.Box;
            for (int i = 0; i < c.Points.Count; i++)
                c.Points[i] = horizontal ? new Point(b.Left + b.Right - c.Points[i].X, c.Points[i].Y) : new Point(c.Points[i].X, b.Top + b.Bottom - c.Points[i].Y);
            c.UpdateBox();
        }
        else if (Pending is { } p)
        {
            if (horizontal) p.FlipX = !p.FlipX;
            else p.FlipY = !p.FlipY;
        }
        Redraw();
    }

    /// <summary>Show an image (paste / insert / custom sticker) as an editable item.</summary>
    public void BeginImage(IImage img, Label kind = Label.Paste)
    {
        CommitPending();
        Select(0);
        double k = Math.Min(1, Math.Min(W * 0.8 / img.Size.Width, H * 0.8 / img.Size.Height));
        var size = new Size(img.Size.Width * k, img.Size.Height * k);
        var item = new ImageItem { Image = img, Box = new Rect(new Point((W - size.Width) / 2, (H - size.Height) / 2), size) };
        SetPending(item);
    }

    // ------------------------------------------------------------ text editor

    private void OpenEditor()
    {
        if (Pending is not TextItem) return;
        StyleEditor();
        _editor.Text = ((TextItem)Pending).Text;
        _editor.IsVisible = true;
        LayoutEditor();
        Dispatcher.UIThread.Post(() => _editor.Focus(), DispatcherPriority.Input);
    }

    private void StyleEditor()
    {
        if (Pending is not TextItem t) return;
        _editor.FontFamily = string.IsNullOrEmpty(t.Font) ? FontFamily.Default : new FontFamily(t.Font);
        _editor.FontWeight = t.Bold ? FontWeight.Bold : FontWeight.Normal;
        _editor.FontStyle = t.Italic ? FontStyle.Italic : FontStyle.Normal;
        _editor.Foreground = new SolidColorBrush(t.Color);
        _editor.TextAlignment = t.Align;
        _editor.Background = t.FillBg ? new SolidColorBrush(t.BgColor) : EditorTint;
        _editor.Resources["TextControlBackgroundFocused"] = _editor.Background;
        _editor.Resources["TextControlBackgroundPointerOver"] = _editor.Background;
    }

    private static readonly IBrush EditorTint = new SolidColorBrush(Color.FromArgb(20, 0, 120, 215));

    private void CloseEditor()
    {
        _editor.IsVisible = false;
    }

    private void LayoutEditor()
    {
        if (!_editor.IsVisible || Pending is not TextItem t) return;
        var r = ToView(t.Box);
        Canvas.SetLeft(_editor, r.X);
        Canvas.SetTop(_editor, r.Y);
        _editor.Width = Math.Max(10, r.Width);
        _editor.MinHeight = r.Height;
        _editor.FontSize = Math.Max(1, t.Size * Zoom);
        _editor.Padding = new Thickness(4 * Zoom);
    }

    // ------------------------------------------------------------- selection

    /// <summary>Select one object together with the rest of its group (0 clears the selection).</summary>
    public void Select(uint id) => SelectMany(id == 0 ? Array.Empty<uint>() : WithGroup(id));

    public void SelectMany(IEnumerable<uint> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.SequenceEqual(_sel)) return;
        _sel.Clear();
        _sel.AddRange(list);
        SelectionChanged?.Invoke();
        Redraw();
    }

    /// <summary>Add an object (and its group) to the selection, or take it out again.</summary>
    public void ToggleSelect(uint id)
    {
        var g = WithGroup(id).ToList();
        SelectMany(_sel.Contains(id) ? _sel.Except(g).ToList() : _sel.Concat(g).ToList());
    }

    public void SelectAllObjects() => SelectMany(Engine.ObjectIds());

    private IEnumerable<uint> WithGroup(uint id)
    {
        uint g = Engine.Group(id);
        return g == 0 ? new[] { id } : new[] { id }.Concat(Engine.ObjectIds().Where(o => o != id && Engine.Group(o) == g));
    }

    /// <summary>Combined box of all selected objects; Pos is their average position.</summary>
    public ObjInfo? SelectionInfo()
    {
        var infos = _sel.Select(id => Engine.Info(id)).OfType<ObjInfo>().ToList();
        if (infos.Count == 0) return null;
        if (infos.Count == 1) return infos[0];
        var box = infos.Skip(1).Aggregate(infos[0].Screen, (r, i) => r.Union(i.Screen));
        var pos = new Vector3D(infos.Average(i => i.Pos.X), infos.Average(i => i.Pos.Y), infos.Average(i => i.Pos.Z));
        return infos[0] with { Pos = pos, Screen = box };
    }

    private void StartObjects(ObjInfo combined)
    {
        _startObj = combined;
        _starts = _sel.Where(id => Engine.Info(id) != null).ToDictionary(id => id, id => Engine.Info(id)!.Value);
    }

    private void RotateSelection(int axis, double deg)
    {
        if (_sel.Count == 1) Engine.RotateObject(_sel[0], axis, deg);
        else
            foreach (var id in _sel) Engine.RotateObjectAbout(id, axis, deg, _startObj.Pos);
        Refresh(false, true);
    }

    public void DeleteSelection()
    {
        if (Pending != null) CancelPending(restore: false);
        else if (_sel.Count > 0)
        {
            Engine.Checkpoint(Label.DeleteObject);
            foreach (var id in _sel) Engine.RemoveObject(id);
            _sel.Clear();
            Refresh();
            SelectionChanged?.Invoke();
        }
    }
}
