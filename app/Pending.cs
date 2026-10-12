using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OpenPaint3D;

/// <summary>
/// A 2D object that stays editable (move / resize / rotate / flip) until it is
/// committed onto the canvas, like Paint 3D's selected shapes, stickers and text.
/// </summary>
public abstract class Pending2D
{
    /// <summary>Unrotated box in canvas coordinates.</summary>
    public Rect Box;
    public double Angle;
    public bool FlipX, FlipY;
    public double Opacity = 1;
    public int Finish;

    public virtual bool KeepAspect => false;
    public virtual bool HasBoxHandles => true;
    public virtual double Pad => 2;
    public abstract Label Label { get; }
    public abstract void Draw(DrawingContext dc, Rect box);

    public Point Center => Box.Center;

    /// <summary>Local (unrotated box space) to canvas transform.</summary>
    public Matrix Transform =>
        Matrix.CreateTranslation(-Center.X, -Center.Y) *
        Matrix.CreateScale(FlipX ? -1 : 1, FlipY ? -1 : 1) *
        Matrix.CreateRotation(Angle * Math.PI / 180) *
        Matrix.CreateTranslation(Center.X, Center.Y);

    public Point ToLocal(Point canvas) => Transform.Invert().Transform(canvas);
    public Point ToCanvas(Point local) => Transform.Transform(local);

    public bool Contains(Point canvas) => Box.Inflate(Pad).Contains(ToLocal(canvas));

    /// <summary>Render at canvas resolution; returns the bitmap and its canvas origin.</summary>
    public RenderTargetBitmap Rasterize(out PixelPoint origin)
    {
        var aabb = Box.Inflate(Pad + 2).TransformToAABB(Transform);
        int x0 = (int)Math.Floor(aabb.X), y0 = (int)Math.Floor(aabb.Y);
        int w = Math.Max(1, (int)Math.Ceiling(aabb.Right) - x0), h = Math.Max(1, (int)Math.Ceiling(aabb.Bottom) - y0);
        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        using (dc.PushTransform(Transform * Matrix.CreateTranslation(-x0, -y0)))
            Draw(dc, Box);
        origin = new PixelPoint(x0, y0);
        return rtb;
    }

    /// <summary>Unrotated alpha mask (longest side ≤ 320 px) used to turn the item into 3D.</summary>
    public unsafe byte[] Mask(out int mw, out int mh)
    {
        double k = Math.Min(1.0, 320 / Math.Max(1, Math.Max(Box.Width, Box.Height)));
        mw = Math.Max(2, (int)Math.Ceiling(Box.Width * k));
        mh = Math.Max(2, (int)Math.Ceiling(Box.Height * k));
        using var rtb = new RenderTargetBitmap(new PixelSize(mw, mh), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        using (dc.PushTransform(Matrix.CreateTranslation(-Box.X, -Box.Y) * Matrix.CreateScale(k, k)))
            Draw(dc, Box);
        var buf = new byte[mw * mh * 4];
        fixed (byte* p = buf)
            rtb.CopyPixels(new PixelRect(0, 0, mw, mh), (IntPtr)p, buf.Length, mw * 4);
        var mask = new byte[mw * mh];
        for (int i = 0; i < mask.Length; i++) mask[i] = buf[i * 4 + 3];
        return mask;
    }

    /// <summary>Copy shared style settings from the tool state (called when the panel changes).</summary>
    public virtual void Apply(ToolState s) { }
}

public sealed class ShapeItem : Pending2D
{
    public int Kind;
    public Color Stroke;
    public double Thickness;
    public int LineType;    // 0 solid, 1 dashed, 2 dotted, 3 none
    public int FillMode;    // 0 none, 1 solid, 2 gradient
    public Color Fill, Fill2;

    public override Label Label => Label.Shape2D;
    public override double Pad => LineType == 3 ? 2 : Thickness / 2 + 2;

    public override void Apply(ToolState s)
    {
        Stroke = s.Color;
        Thickness = s.ShapeThickness;
        LineType = s.LineType;
        FillMode = s.FillMode;
        Fill = s.FillColor;
        Fill2 = s.FillColor2;
        Opacity = s.ShapeOpacity;
        Finish = s.Finish;
    }

    public static IPen? MakePen(Color c, double thickness, int lineType) => lineType == 3 || thickness <= 0
        ? null
        : new Pen(new SolidColorBrush(c), thickness, lineType switch
        {
            1 => new DashStyle(new double[] { 3, 2 }, 0),
            2 => new DashStyle(new double[] { 0.01, 2 }, 0),
            _ => null,
        }, PenLineCap.Round, PenLineJoin.Round);

    public override void Draw(DrawingContext dc, Rect box)
    {
        IBrush? brush = FillMode switch
        {
            1 => new SolidColorBrush(Fill),
            2 => new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Fill, 0), new GradientStop(Fill2, 1) },
            },
            _ => null,
        };
        dc.DrawGeometry(brush, MakePen(Stroke, Thickness, LineType), Shapes2D.Build(Kind, box));
    }
}

/// <summary>Straight line or 3/4/5-point curve; edited through its control points.</summary>
public sealed class CurveItem : Pending2D
{
    public List<Point> Points = new();
    public Color Stroke;
    public double Thickness;
    public int LineType;

    public override Label Label => Label.Shape2D;
    public override bool HasBoxHandles => false;
    public override double Pad => Thickness / 2 + 2;

    public void UpdateBox()
    {
        double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
        // curves overshoot their control points a little
        double m = Points.Count > 2 ? Math.Max(x1 - x0, y1 - y0) * 0.15 : 0;
        Box = new Rect(x0 - m, y0 - m, Math.Max(1, x1 - x0 + 2 * m), Math.Max(1, y1 - y0 + 2 * m));
        Angle = 0;
        FlipX = FlipY = false;
    }

    public override void Apply(ToolState s)
    {
        Stroke = s.Color;
        Thickness = Math.Max(1, s.ShapeThickness);
        LineType = s.LineType == 3 ? 0 : s.LineType;
        Opacity = s.ShapeOpacity;
        Finish = s.Finish;
    }

    public void Translate(Vector d)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] += d;
        UpdateBox();
    }

    public override void Draw(DrawingContext dc, Rect box) =>
        dc.DrawGeometry(null, ShapeItem.MakePen(Stroke, Thickness, LineType), Shapes2D.Curve(Points));
}

/// <summary>Sticker, texture or picture.</summary>
public sealed class ImageItem : Pending2D
{
    public int Sticker = -1;
    public IImage? Image;
    public bool Tile;   // textures repeat instead of stretching

    public override Label Label => Image != null && Sticker < 0 && !Tile ? Label.Paste : Label.Sticker;
    public override bool KeepAspect => !Tile;

    public override void Apply(ToolState s) => Opacity = s.StickerOpacity;

    public override void Draw(DrawingContext dc, Rect box)
    {
        if (Sticker >= 0)
        {
            Stickers.Draw(Sticker, dc, box);
        }
        else if (Image != null && Tile)
        {
            var brush = new ImageBrush((IImageBrushSource)Image)
            {
                TileMode = TileMode.Tile,
                DestinationRect = new RelativeRect(0, 0, 256, 256, RelativeUnit.Absolute),
            };
            dc.DrawRectangle(brush, null, box);
        }
        else if (Image != null)
        {
            dc.DrawImage(Image, new Rect(Image.Size), box);
        }
    }
}

public sealed class TextItem : Pending2D
{
    public string Text = "";
    public string Font = "";
    public double Size = 48;
    public bool Bold, Italic, Underline;
    public TextAlignment Align = TextAlignment.Left;
    public Color Color;
    public bool Is3D;

    public override Label Label => Label.Text;

    public override void Apply(ToolState s)
    {
        Font = s.Font;
        Size = s.FontSize;
        Bold = s.Bold;
        Italic = s.Italic;
        Underline = s.Underline;
        Align = s.Align;
        Color = s.Color;
        Is3D = s.Text3D;
        Finish = s.Finish;
    }

    public Typeface Typeface => new(string.IsNullOrEmpty(Font) ? FontFamily.Default : new FontFamily(Font),
        Italic ? FontStyle.Italic : FontStyle.Normal, Bold ? FontWeight.Bold : FontWeight.Normal);

    public FormattedText Format(double maxWidth)
    {
        var ft = new FormattedText(Text.Length == 0 ? " " : Text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface, Size, new SolidColorBrush(Color))
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            TextAlignment = Align,
        };
        if (Underline) ft.SetTextDecorations(TextDecorations.Underline);
        return ft;
    }

    /// <summary>Grow the box so the text fits (width stays, height follows the text).</summary>
    public void Fit()
    {
        var ft = Format(Box.Width - 8);
        Box = new Rect(Box.X, Box.Y, Box.Width, Math.Max(Box.Height, ft.Height + 8));
    }

    public override void Draw(DrawingContext dc, Rect box) => dc.DrawText(Format(box.Width - 8), box.TopLeft + new Vector(4, 4));
}
