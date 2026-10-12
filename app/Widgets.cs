using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenPaint3D;

/// <summary>Draws a 2D shape / line preview for the shapes panel.</summary>
public sealed class GeometryView : Control
{
    public Func<Rect, Geometry> Build { get; init; } = r => new RectangleGeometry(r);
    public bool Filled { get; init; }
    public IBrush Ink { get; set; } = new SolidColorBrush(Color.Parse("#2E3256"));
    public double Inset { get; init; } = 9;

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(Inset);
        ctx.DrawGeometry(Filled ? new SolidColorBrush(Color.Parse("#C9E3F8")) : null,
            new Pen(Ink, 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Build(r));
    }
}

/// <summary>Circular colour wheel (hue around, saturation outwards) used for lighting colour.</summary>
public sealed class HueWheel : Control
{
    private Color _value = Colors.White;
    public event Action<Color, bool>? ValueChanged;

    public Color Value
    {
        get => _value;
        set
        {
            _value = value;
            InvalidateVisual();
        }
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }

    public override void Render(DrawingContext ctx)
    {
        double rad = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var hue = new ConicGradientBrush { Center = RelativePoint.Center, Angle = 90 };
        for (int i = 0; i <= 6; i++) hue.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        ctx.DrawEllipse(hue, null, c, rad, rad);
        var white = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        ctx.DrawEllipse(white, new Pen(new SolidColorBrush(Color.Parse("#D0D0D0")), 1), c, rad, rad);
        var (h, s, _) = ToHsv(_value);
        double a = h * Math.PI / 180;
        var p = new Point(c.X + Math.Cos(a) * s * rad, c.Y + Math.Sin(a) * s * rad);
        ctx.DrawEllipse(new SolidColorBrush(_value), new Pen(Brushes.White, 2), p, 7, 7);
        ctx.DrawEllipse(null, new Pen(Brushes.Black, 1), p, 8, 8);
    }

    private void Pick(Point p, bool final)
    {
        double rad = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var d = p - c;
        double h = Math.Atan2(d.Y, d.X) * 180 / Math.PI;
        double s = Math.Min(1, Math.Sqrt(d.X * d.X + d.Y * d.Y) / rad);
        Value = FromHsv(h, s, 1);
        ValueChanged?.Invoke(_value, final);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Pick(e.GetPosition(this), false);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Pick(e.GetPosition(this), false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Pick(e.GetPosition(this), true);
    }
}
