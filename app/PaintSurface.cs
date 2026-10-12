using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpenPaint3D;

public enum Tool { Brush, Shape2D, Shape3D, Sticker, Text, Effects, Canvas, Select }

public sealed class ToolState
{
    public Tool Tool = Tool.Brush;
    public int Brush;            // engine brush id
    public bool Bucket;          // fill tool selected in the brush panel
    public bool Picking;         // eyedropper active
    public double BrushSize = 12;
    public double Opacity = 1;
    public int Tolerance = 32;
    public Color Color = Color.Parse("#1F1F1F");
    public int Shape = 1;
    public double ShapeThickness = 4;
    public bool ShapeFill;
    public Color FillColor = Color.Parse("#4FC3F7");
    public int Model;
    public int Sticker;
    public string Text = "안녕하세요";
    public string Font = "";
    public double FontSize = 48;
    public bool Bold, Italic;
}

public sealed class PaintSurface : Control
{
    public Engine Engine { get; }
    public ToolState S { get; }
    public double Zoom { get; private set; } = 1;
    public Rect? Selection { get; private set; }

    public event Action? Changed;
    public event Action? ZoomChanged;
    public event Action? SelectionChanged;
    public event Action<Color>? ColorPicked;

    private WriteableBitmap? _bmp;
    private Vector _pan;
    private Point? _dragStart, _dragCur, _hover, _panFrom;
    private bool _stroking, _fitted;

    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(38, 0, 0, 0));
    private static readonly IPen Dash = new Pen(new SolidColorBrush(Color.Parse("#0078D7")), 1.2, new DashStyle(new double[] { 4, 3 }, 0));
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)), 1);
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));

    public PaintSurface(Engine engine, ToolState state)
    {
        Engine = engine;
        S = state;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Refresh(false);
    }

    private int W => Engine.Width;
    private int H => Engine.Height;

    public void Refresh(bool changed = true)
    {
        if (_bmp == null || _bmp.PixelSize.Width != W || _bmp.PixelSize.Height != H)
        {
            _bmp?.Dispose();
            _bmp = Engine.CreateBitmap();
            if (Selection is { } s && (s.Right > W || s.Bottom > H)) ClearSelection();
        }
        Engine.CopyTo(_bmp);
        InvalidateVisual();
        if (changed) Changed?.Invoke();
    }

    public void ClearSelection()
    {
        Selection = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ------------------------------------------------------------ zoom / view

    private Point Origin => new(
        Math.Round((Bounds.Width - W * Zoom) / 2 + _pan.X),
        Math.Round((Bounds.Height - H * Zoom) / 2 + _pan.Y));

    private Point ToCanvas(Point p) => new((p.X - Origin.X) / Zoom, (p.Y - Origin.Y) / Zoom);
    private Point ToView(Point p) => new(Origin.X + p.X * Zoom, Origin.Y + p.Y * Zoom);

    public void SetZoom(double z)
    {
        Zoom = Math.Clamp(z, 0.05, 16);
        InvalidateVisual();
        ZoomChanged?.Invoke();
    }

    public void FitToView()
    {
        if (Bounds.Width < 10 || Bounds.Height < 10) return;
        _pan = default;
        SetZoom(Math.Min(1, Math.Min((Bounds.Width - 64) / W, (Bounds.Height - 64) / H)));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_fitted && e.NewSize.Width > 10)
        {
            _fitted = true;
            FitToView();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var pos = e.GetPosition(this);
            var before = ToCanvas(pos);
            SetZoom(Zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15));
            var after = ToView(before);
            _pan += pos - after;
        }
        else
        {
            _pan += new Vector(e.Delta.X, e.Delta.Y) * 40;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    // ----------------------------------------------------------------- render

    public override void Render(DrawingContext ctx)
    {
        // transparent fill so the whole workspace receives pointer input
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_bmp == null) return;
        var o = Origin;
        var dest = new Rect(o.X, o.Y, W * Zoom, H * Zoom);
        ctx.FillRectangle(Shadow, dest.Translate(new Vector(0, 3)).Inflate(2));
        using (ctx.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = Zoom >= 2 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality,
        }))
            ctx.DrawImage(_bmp, new Rect(0, 0, W, H), dest);

        if (Selection is { } sel)
        {
            var v = new Rect(ToView(sel.TopLeft), ToView(sel.BottomRight));
            using (ctx.PushGeometryClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(dest), new RectangleGeometry(v))))
                ctx.FillRectangle(Dim, dest);
            ctx.DrawRectangle(null, Dash, v);
        }

        if (_dragStart is { } a && _dragCur is { } b)
        {
            var (va, vb) = (ToView(a), ToView(b));
            var box = new Rect(va, vb).Normalize();
            if (S.Tool == Tool.Shape2D)
            {
                var pen = new Pen(new SolidColorBrush(S.Color), Math.Max(1, S.ShapeThickness * Zoom));
                ctx.DrawGeometry(S.ShapeFill ? new SolidColorBrush(S.FillColor) : null, pen, ShapeGeometry(S.Shape, va, vb));
                if (S.Shape != 0) ctx.DrawRectangle(null, Dash, box);
            }
            else if (S.Tool == Tool.Sticker)
            {
                Stickers.Draw(S.Sticker, ctx, box);
                ctx.DrawRectangle(null, Dash, box);
            }
            else
            {
                ctx.DrawRectangle(null, Dash, box);
                if (S.Tool == Tool.Shape3D) ctx.DrawEllipse(null, Dash, box.Center, box.Width / 2, box.Height / 2);
            }
        }

        if (_hover is { } h && S.Tool == Tool.Brush && !S.Bucket && !S.Picking)
        {
            var r = Math.Max(1, S.BrushSize * Zoom / 2);
            ctx.DrawEllipse(null, CursorPen, h, r, r);
        }
    }

    /// <summary>Preview outline for a 2D shape, mirrors the engine's shape_points().</summary>
    public static Geometry ShapeGeometry(int kind, Point a, Point b)
    {
        if (kind == 0) return new LineGeometry(a, b);
        var r = new Rect(a, b).Normalize();
        if (kind == 2) return new EllipseGeometry(r);
        double cx = r.Center.X, cy = r.Center.Y, rx = r.Width / 2, ry = r.Height / 2;
        IEnumerable<Point> Regular(int n, double inner) => Enumerable.Range(0, inner > 0 ? n * 2 : n).Select(i =>
        {
            int total = inner > 0 ? n * 2 : n;
            double ang = -Math.PI / 2 + i * 2 * Math.PI / total, k = inner > 0 && i % 2 == 1 ? inner : 1;
            return new Point(cx + Math.Cos(ang) * rx * k, cy + Math.Sin(ang) * ry * k);
        });
        var pts = (kind switch
        {
            3 => new[] { new Point(cx, r.Top), r.BottomRight, r.BottomLeft },
            4 => new[] { r.TopLeft, r.BottomRight, r.BottomLeft },
            5 => Regular(5, 0),
            6 => Regular(6, 0),
            7 => Regular(5, 0.45),
            8 => Enumerable.Range(0, 64).Select(i =>
            {
                double t = i * 2 * Math.PI / 64;
                double hx = 16 * Math.Pow(Math.Sin(t), 3);
                double hy = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                return new Point(cx + hx / 16 * rx, r.Top + (12 - hy) / 29 * r.Height);
            }),
            9 => new[]
            {
                new Point(r.Left, r.Top + r.Height * 0.3), new Point(r.Left + r.Width * 0.6, r.Top + r.Height * 0.3),
                new Point(r.Left + r.Width * 0.6, r.Top), new Point(r.Right, cy), new Point(r.Left + r.Width * 0.6, r.Bottom),
                new Point(r.Left + r.Width * 0.6, r.Top + r.Height * 0.7), new Point(r.Left, r.Top + r.Height * 0.7),
            },
            _ => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft },
        }).ToList();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        return g;
    }

    // ---------------------------------------------------------------- pointer

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsMiddleButtonPressed)
        {
            _panFrom = pt.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (!pt.Properties.IsLeftButtonPressed) return;
        var c = ToCanvas(pt.Position);

        if (S.Picking)
        {
            if (Engine.Pixel(c) is { } col) ColorPicked?.Invoke(col);
            return;
        }
        switch (S.Tool)
        {
            case Tool.Brush when S.Bucket:
                if (Engine.Fill(c, S.Color, S.Tolerance)) Refresh();
                break;
            case Tool.Brush:
                Engine.StrokeBegin(S.Brush, S.Color, S.BrushSize, S.Opacity, c);
                _stroking = true;
                e.Pointer.Capture(this);
                Refresh(false);
                break;
            case Tool.Text:
                StampText(c);
                break;
            case Tool.Shape2D or Tool.Shape3D or Tool.Sticker or Tool.Select:
                _dragStart = _dragCur = c;
                e.Pointer.Capture(this);
                InvalidateVisual();
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        _hover = pos;
        if (_panFrom is { } from)
        {
            _pan += pos - from;
            _panFrom = pos;
        }
        else if (_stroking)
        {
            foreach (var p in e.GetIntermediatePoints(this)) Engine.StrokeTo(ToCanvas(p.Position));
            Refresh(false);
            return;
        }
        else if (_dragStart is { } a)
        {
            var c = ToCanvas(pos);
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && S.Tool != Tool.Select)
            {
                var d = Math.Max(Math.Abs(c.X - a.X), Math.Abs(c.Y - a.Y));
                c = S.Tool == Tool.Shape2D && S.Shape == 0
                    ? SnapLine(a, c)
                    : new Point(a.X + Math.Sign(c.X - a.X) * d, a.Y + Math.Sign(c.Y - a.Y) * d);
            }
            _dragCur = c;
        }
        InvalidateVisual();
    }

    private static Point SnapLine(Point a, Point c)
    {
        var v = c - a;
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        var ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panFrom != null)
        {
            _panFrom = null;
            e.Pointer.Capture(null);
            return;
        }
        if (_stroking)
        {
            Engine.StrokeEnd();
            _stroking = false;
            e.Pointer.Capture(null);
            Refresh();
            return;
        }
        if (_dragStart is { } a && _dragCur is { } b)
        {
            _dragStart = _dragCur = null;
            e.Pointer.Capture(null);
            Commit(a, b);
            InvalidateVisual();
        }
    }

    private void Commit(Point a, Point b)
    {
        var box = new Rect(a, b).Normalize();
        bool tiny = box.Width < 2 && box.Height < 2;
        switch (S.Tool)
        {
            case Tool.Shape2D when !tiny:
                Engine.Shape(S.Shape, a, b, S.Color, S.ShapeThickness, S.ShapeFill ? S.FillColor : null);
                Refresh();
                break;
            case Tool.Shape3D when !tiny:
                Engine.Model(S.Model, a, b, S.Color);
                Refresh();
                break;
            case Tool.Sticker when !tiny:
                StampSticker(box);
                break;
            case Tool.Select:
                var clip = box.Intersect(new Rect(0, 0, W, H));
                Selection = tiny || clip.Width < 1 || clip.Height < 1
                    ? null
                    : new Rect(Math.Floor(clip.X), Math.Floor(clip.Y), Math.Ceiling(clip.Width), Math.Ceiling(clip.Height));
                SelectionChanged?.Invoke();
                break;
        }
    }

    private void StampSticker(Rect box)
    {
        int w = (int)Math.Ceiling(box.Width), h = (int)Math.Ceiling(box.Height);
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
            Stickers.Draw(S.Sticker, dc, new Rect(0, 0, w, h));
        Engine.Blend(rtb, (int)Math.Floor(box.X), (int)Math.Floor(box.Y));
        Refresh();
    }

    private void StampText(Point at)
    {
        if (string.IsNullOrWhiteSpace(S.Text)) return;
        var face = new Typeface(string.IsNullOrEmpty(S.Font) ? FontFamily.Default : new FontFamily(S.Font),
            S.Italic ? FontStyle.Italic : FontStyle.Normal, S.Bold ? FontWeight.Bold : FontWeight.Normal);
        var ft = new FormattedText(S.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            face, S.FontSize, new SolidColorBrush(S.Color));
        int w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 4, h = (int)Math.Ceiling(ft.Height) + 4;
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
            dc.DrawText(ft, new Point(2, 2));
        Engine.Blend(rtb, (int)at.X, (int)at.Y);
        Refresh();
    }
}
