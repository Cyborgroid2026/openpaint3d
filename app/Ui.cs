using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenPaint3D;

/// <summary>Colours of the Paint 3D-style chrome.</summary>
public static class P3
{
    public static readonly IBrush Bar = new SolidColorBrush(Color.Parse("#363941"));
    public static readonly IBrush BarHover = new SolidColorBrush(Color.Parse("#474B55"));
    public static readonly IBrush BarPressed = new SolidColorBrush(Color.Parse("#2A2D33"));
    public static readonly IBrush TabSelected = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0563B7"), 0), new GradientStop(Color.Parse("#604DDA"), 1) },
    };
    public static readonly IBrush Pane = new SolidColorBrush(Color.Parse("#F1F3F4"));
    public static readonly IBrush PaneHover = new SolidColorBrush(Color.Parse("#E2E5E8"));
    public static readonly IBrush PaneLine = new SolidColorBrush(Color.Parse("#DADDE0"));
    public static readonly IBrush Box = new SolidColorBrush(Color.Parse("#F7F8F9"));
    public static readonly IBrush BoxBorder = new SolidColorBrush(Color.Parse("#DCDFE2"));
    public static readonly IBrush SubSelected = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#D3E1F4"), 0), new GradientStop(Color.Parse("#E1DDF6"), 1) },
    };
    public static readonly IBrush TileSelected = new SolidColorBrush(Color.Parse("#2B5CC4"));
    public static readonly IBrush Title = new SolidColorBrush(Color.Parse("#1767BF"));
    public static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#2E3256"));
    public static readonly IBrush Text = new SolidColorBrush(Color.Parse("#1F1F1F"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6B6F76"));
}

/// <summary>A flat clickable area with its own hover/selected backgrounds (no Fluent button chrome).</summary>
public class FlatButton : Border
{
    public IBrush Normal { get; set; } = Brushes.Transparent;
    public IBrush Hover { get; set; } = P3.PaneHover;
    public IBrush? SelectedBrush { get; set; }
    public event Action? Click;
    private bool _over, _pressed, _selected;

    public FlatButton()
    {
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty)
            {
                Opacity = IsEnabled ? 1 : 0.38;
                Update();
            }
        };
    }

    public bool IsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Update();
        }
    }

    public FlatButton Init()
    {
        Update();
        return this;
    }

    private void Update() => Background = _selected && SelectedBrush != null ? SelectedBrush : _over && IsEnabled ? Hover : Normal;

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _over = true;
        Update();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _over = false;
        Update();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        var p = e.GetPosition(this);
        if (IsEnabled && p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height) Click?.Invoke();
        e.Handled = true;
    }
}

/// <summary>Flat, coloured tool icons drawn on a 24px grid (layers of fill + outline).</summary>
public static class ColorIcons
{
    private sealed record Layer(string Data, string? Fill, bool Outline = true);

    private static readonly Layer[][] Brushes =
    {
        // marker
        new[]
        {
            new Layer("M8.5,11 H15.5 V21.5 H8.5 Z", "#4F8DF0"),
            new Layer("M9,8 H15 L15.5,11 H8.5 Z", "#2E3256"),
            new Layer("M10.5,8 L11.2,3 H12.8 L13.5,8 Z", "#4F8DF0"),
            new Layer("M8.5,14 H15.5", null),
        },
        // calligraphy pen
        new[]
        {
            new Layer("M12,2.5 L17,10.5 L15,17 H9 L7,10.5 Z", "#3FB5A3"),
            new Layer("M12,2.5 V10 M12,11.3 V11.5", null),
            new Layer("M9,17 H15 V21.5 H9 Z", "#2E3256"),
        },
        // oil brush
        new[]
        {
            new Layer("M8,2.5 H16 V9 H8 Z", "#F2994A"),
            new Layer("M8,9 H16 V12 H8 Z", "#AEB6C2"),
            new Layer("M9.5,12 H14.5 L13.6,21.5 H10.4 Z", "#E5C590"),
        },
        // watercolour
        new[]
        {
            new Layer("M12,2.5 C15,5.5 15,9 14,11 H10 C9,9 9,5.5 12,2.5 Z", "#62B4F2"),
            new Layer("M10,11 H14 V13.5 H10 Z", "#AEB6C2"),
            new Layer("M10.6,13.5 H13.4 L12.8,21.5 H11.2 Z", "#C98D5B"),
        },
        // pixel pen
        new[]
        {
            new Layer("M12,2.5 L15,9 V21.5 H9 V9 Z", "#6E7FA6"),
            new Layer("M12,2.5 L13.4,5.5 H10.6 Z", "#2E3256"),
            new Layer("M9,9 H15", null),
        },
        // pencil
        new[]
        {
            new Layer("M9,8 H15 V19 H9 Z", "#F6C443"),
            new Layer("M9,8 L12,2.5 L15,8 Z", "#F3D9B1"),
            new Layer("M11,4.4 L12,2.5 L13,4.4 Z", "#2E3256"),
            new Layer("M9,19 H15 V21.5 H9 Z", "#F08AA8"),
        },
        // eraser
        new[]
        {
            new Layer("M8.5,6 H15.5 V21.5 H8.5 Z", "#F08AA8"),
            new Layer("M8.5,6 C8.5,3.5 15.5,3.5 15.5,6 Z", "#F8C2D2"),
            new Layer("M8.5,15 H15.5", null),
        },
        // crayon
        new[]
        {
            new Layer("M9,9 H15 V21.5 H9 Z", "#E5395B"),
            new Layer("M9.5,9 L11,3.5 H13 L14.5,9 Z", "#E5395B"),
            new Layer("M9,12 H15 M9,18 H15", null),
        },
        // spray can
        new[]
        {
            new Layer("M8,9 H16 V21.5 H8 Z", "#F57C23"),
            new Layer("M9.5,6 H14.5 V9 H9.5 Z", "#AEB6C2"),
            new Layer("M11,3.5 H13 V6 H11 Z", "#2E3256"),
            new Layer("M6,3 V3.2 M5,5 V5.2 M7,5.5 V5.7", null),
        },
        // fill bucket
        new[]
        {
            new Layer("M6,7 H18 L16.5,21.5 H7.5 Z", "#3A9FA3"),
            new Layer("M6,7 C6,5 18,5 18,7 C18,9 6,9 6,7 Z", "#7FD0D2"),
            new Layer("M15,8 C16,10.5 16,13 15,14 C14,13 14,10.5 15,8 Z", "#2B7C80", false),
        },
    };

    private static readonly IBrush OutlineDark = new SolidColorBrush(Color.Parse("#2E3256"));

    public static Control Brush(int i, double size = 30, bool selected = false)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var l in Brushes[i])
        {
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(l.Data),
                Fill = l.Fill == null ? null : new SolidColorBrush(Color.Parse(l.Fill)),
                Stroke = l.Outline ? (selected ? Avalonia.Media.Brushes.White : OutlineDark) : null,
                StrokeThickness = 1.1,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>Generic multi-colour icon from (path, fill) pairs on the 24px grid.</summary>
    public static Control Make(double size, params (string data, string? fill)[] layers)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var (d, f) in layers)
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(d),
                Fill = f == null ? null : new SolidColorBrush(Color.Parse(f)),
                Stroke = OutlineDark,
                StrokeThickness = 1.1,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}

/// <summary>
/// Light wheel: a ring with a sun knob that sets the light direction, and a disc in the
/// middle showing the current light colour.
/// </summary>
public sealed class LightWheel : Control
{
    private double _angle;
    private Color _color = Colors.White;
    public event Action<double, bool>? AngleChanged;

    public double Angle
    {
        get => _angle;
        set
        {
            _angle = value;
            InvalidateVisual();
        }
    }

    public Color DiscColor
    {
        get => _color;
        set
        {
            _color = value;
            InvalidateVisual();
        }
    }

    private (Point c, double r) Geometry2()
    {
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 16;
        return (new Point(Bounds.Width / 2, Bounds.Height / 2), r);
    }

    private Point Knob()
    {
        var (c, r) = Geometry2();
        double a = (_angle - 180) * Math.PI / 180;   // 0° = light from the left, like the sun icon's rest position
        return new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
    }

    public override void Render(DrawingContext ctx)
    {
        var (c, r) = Geometry2();
        var ink = new Pen(P3.Ink, 1.6);
        ctx.DrawEllipse(Brushes.Transparent, ink, c, r, r);
        ctx.DrawEllipse(new SolidColorBrush(_color), new Pen(new SolidColorBrush(Color.Parse("#C8CBD0")), 1), c, r * 0.55, r * 0.55);
        var k = Knob();
        ctx.DrawEllipse(P3.Pane, null, k, 14, 14);
        ctx.DrawEllipse(null, new Pen(P3.Ink, 1.4), k, 5, 5);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            ctx.DrawLine(new Pen(P3.Ink, 1.4, lineCap: PenLineCap.Round),
                new Point(k.X + Math.Cos(a) * 8, k.Y + Math.Sin(a) * 8), new Point(k.X + Math.Cos(a) * 11, k.Y + Math.Sin(a) * 11));
        }
    }

    private void Set(Point p, bool final)
    {
        var (c, _) = Geometry2();
        _angle = (Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 180 + 360) % 360;
        InvalidateVisual();
        AngleChanged?.Invoke(_angle, final);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Set(e.GetPosition(this), false);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Set(e.GetPosition(this), false);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Set(e.GetPosition(this), true);
    }
}

/// <summary>Saturation/value square for one hue (colour dialog).</summary>
public sealed class SvSquare : Control
{
    public double Hue, Sat = 1, Val = 1;
    public event Action? Changed;

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        ctx.FillRectangle(new SolidColorBrush(HueWheel.FromHsv(Hue, 1, 1)), r);
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        }, r);
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
        }, r);
        var p = new Point(Sat * r.Width, (1 - Val) * r.Height);
        ctx.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(p.X - 9, p.Y - 9, 18, 18));
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1), new Rect(p.X - 10, p.Y - 10, 20, 20));
    }

    private void Set(Point p)
    {
        Sat = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1);
        Val = 1 - Math.Clamp(p.Y / Math.Max(1, Bounds.Height), 0, 1);
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Set(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Set(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }
}

/// <summary>Horizontal rainbow hue bar (colour dialog).</summary>
public sealed class HueBar : Control
{
    public double Hue;
    public event Action? Changed;

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size);
        var g = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(HueWheel.FromHsv(i * 60, 1, 1), i / 6.0));
        ctx.FillRectangle(g, r);
        double x = Math.Clamp(Hue / 360 * r.Width, 11, r.Width - 11);
        ctx.DrawRectangle(new SolidColorBrush(HueWheel.FromHsv(Hue, 1, 1)), new Pen(Brushes.White, 2), new Rect(x - 11, 1, 22, r.Height - 2));
    }

    private void Set(Point p)
    {
        Hue = Math.Clamp(p.X / Math.Max(1, Bounds.Width), 0, 1) * 359.9;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Set(e.GetPosition(this));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Set(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }
}

/// <summary>"새 색 선택" card: SV square, hue bar, preview and R/G/B/Hex fields.</summary>
public sealed class ColorDialog : Border
{
    public event Action<Color?>? Done;
    private readonly SvSquare _sv = new() { Width = 262, Height = 228 };
    private readonly HueBar _hue = new() { Height = 26 };
    private readonly Border _preview = new() { Width = 78, Height = 78 };
    private readonly TextBox _r = Field(), _g = Field(), _b = Field(), _hex = Field();
    private bool _sync;

    private static TextBox Field() => new() { Width = 78, HorizontalContentAlignment = HorizontalAlignment.Right, Background = Brushes.White };

    public ColorDialog(Color start)
    {
        Width = 406;
        Background = P3.Pane;
        BorderBrush = new SolidColorBrush(Color.Parse("#4A6FD8"));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(22, 20);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        BoxShadow = BoxShadows.Parse("0 8 30 0 #50000000");

        var (h, s, v) = HueWheel.ToHsv(start);
        (_sv.Hue, _sv.Sat, _sv.Val, _hue.Hue) = (h, s, v, h);
        _sv.Changed += FromPicker;
        _hue.Changed += () =>
        {
            _sv.Hue = _hue.Hue;
            _sv.InvalidateVisual();
            FromPicker();
        };
        foreach (var t in new[] { _r, _g, _b, _hex })
        {
            t.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromFields(); };
            t.LostFocus += (_, _) => FromFields();
        }

        static TextBlock L(string t) => new() { Text = t, FontSize = 12, Margin = new Thickness(0, 6, 0, 2), Foreground = P3.Text };
        var fields = new StackPanel { Margin = new Thickness(18, 0, 0, 0), Children = { _preview, L("빨강"), _r, L("초록"), _g, L("파랑"), _b, L("16진수"), _hex } };
        _sv.VerticalAlignment = VerticalAlignment.Top;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Children = { _sv, fields } };
        var ok = DialogButton("확인", () => Done?.Invoke(Current()));
        var cancel = DialogButton("취소", () => Done?.Invoke(null));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 22, 0, 0), Children = { ok } };
        Grid.SetColumn(cancel, 2);
        buttons.Children.Add(cancel);
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "새 색 선택", FontSize = 15, Margin = new Thickness(0, 0, 0, 14), Foreground = P3.Text },
                top,
                new Border { Child = _hue, Width = 262, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) },
                buttons,
            },
        };
        Show();
    }

    public static FlatButton DialogButton(string text, Action click)
    {
        var b = new FlatButton
        {
            Normal = new SolidColorBrush(Color.Parse("#CDD0D4")),
            Hover = new SolidColorBrush(Color.Parse("#BFC3C8")),
            Height = 34,
            Child = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = P3.Text },
        }.Init();
        b.Click += click;
        return b;
    }

    private Color Current() => HueWheel.FromHsv(_sv.Hue, _sv.Sat, _sv.Val);

    private void FromPicker()
    {
        if (_sync) return;
        Show();
    }

    private void Show()
    {
        _sync = true;
        var c = Current();
        _preview.Background = new SolidColorBrush(c);
        _r.Text = c.R.ToString();
        _g.Text = c.G.ToString();
        _b.Text = c.B.ToString();
        _hex.Text = $"{c.R:x2}{c.G:x2}{c.B:x2}";
        _sync = false;
    }

    private void FromFields()
    {
        if (_sync) return;
        Color c;
        var hx = (_hex.Text ?? "").Trim().TrimStart('#');
        var cur = Current();
        if (hx.Length == 6 && hx != $"{cur.R:x2}{cur.G:x2}{cur.B:x2}" && Color.TryParse("#" + hx, out var parsed)) c = parsed;
        else if (byte.TryParse(_r.Text, out var r) && byte.TryParse(_g.Text, out var g) && byte.TryParse(_b.Text, out var b)) c = Color.FromRgb(r, g, b);
        else return;
        var (h, s, v) = HueWheel.ToHsv(c);
        if (s > 0) _sv.Hue = _hue.Hue = h;
        (_sv.Sat, _sv.Val) = (s, v);
        _sv.InvalidateVisual();
        _hue.InvalidateVisual();
        Show();
    }
}
