using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace OpenPaint3D;

/// <summary>Line icons for the toolbar (24x24 design grid, stroked).</summary>
public static class Icons
{
    public const string Menu = "M3,6 H10 L12,8 H21 V19 H3 Z";
    public const string Brushes = "M4,20 C4,17 6,15 8,15 C10,15 11,16.5 11,18 C11,20.5 8.5,21.5 4,20 Z M10.5,14.5 L19,4.5 C20,3.5 21.5,5 20.5,6 L12,15.5";
    public const string Shapes2D = "M3,3 H13 V13 H3 Z M9,16 A5,5 0 1 0 19,16 A5,5 0 1 0 9,16 Z";
    public const string Shapes3D = "M12,2 L21,7 V17 L12,22 L3,17 V7 Z M3,7 L12,12 L21,7 M12,12 V22";
    public const string Stickers = "M4,4 H20 V14 L14,20 H4 Z M14,20 V14 H20 M8,9 H9 M15,9 H16 M8,13 Q12,16 16,12";
    public const string Text = "M5,5 H19 M12,5 V20 M9,20 H15";
    public const string Effects = "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M12,1 V4 M12,20 V23 M1,12 H4 M20,12 H23 M4.2,4.2 L6.3,6.3 M17.7,17.7 L19.8,19.8 M4.2,19.8 L6.3,17.7 M17.7,6.3 L19.8,4.2";
    public const string Canvas = "M6,2 V18 H22 M2,6 H18 V22";
    public const string Select = "M3,21 L14,10 M15,3 L16,6 L19,7 L16,8 L15,11 L14,8 L11,7 L14,6 Z M20,12 L21,13 M19,2 L20,1";
    public const string Undo = "M9,5 L4,10 L9,15 M4,10 H15 A5,5 0 0 1 15,20 H11";
    public const string Redo = "M15,5 L20,10 L15,15 M20,10 H9 A5,5 0 0 0 9,20 H13";
    public const string Picker = "M15,3 L21,9 M18,6 L8,16 L5,19 M8,16 H5 V19 M13,5 L19,11";
    public const string Bucket = "M5,11 L12,4 L19,11 L12,18 Z M19,11 C21,14 21,16 20,17 C19,18 18,17 18,15 Z M5,11 H19";

    public static Avalonia.Controls.Shapes.Path Make(string data, double size = 24, IBrush? stroke = null) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke ?? new SolidColorBrush(Color.Parse("#262626")),
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
    };
}

/// <summary>Original vector stickers, drawn on a 100x100 grid.</summary>
public static class Stickers
{
    public static readonly string[] Names = { "별", "하트", "스마일", "해", "구름", "번개", "꽃", "말풍선", "무지개" };

    private static IBrush B(string c) => new SolidColorBrush(Color.Parse(c));
    private static IPen P(string c, double w) => new Pen(B(c), w, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static Geometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        return g;
    }

    public static void Draw(int kind, DrawingContext dc, Rect r)
    {
        using var _ = dc.PushTransform(Matrix.CreateScale(r.Width / 100, r.Height / 100) * Matrix.CreateTranslation(r.X, r.Y));
        switch (kind)
        {
            case 0:
                var star = Enumerable.Range(0, 10).Select(i =>
                {
                    var a = -Math.PI / 2 + i * Math.PI / 5;
                    var k = i % 2 == 0 ? 46 : 20;
                    return new Point(50 + Math.Cos(a) * k, 53 + Math.Sin(a) * k);
                }).ToArray();
                dc.DrawGeometry(B("#FFC83D"), P("#E09B00", 3), Poly(star));
                break;
            case 1:
                dc.DrawGeometry(B("#F25C6E"), P("#C93347", 3), Geometry.Parse(
                    "M50,90 C20,67 5,50 5,31 C5,16 17,6 30,6 C40,6 47,12 50,19 C53,12 60,6 70,6 C83,6 95,16 95,31 C95,50 80,67 50,90 Z"));
                dc.DrawEllipse(B("#80FFFFFF"), null, new Point(28, 28), 8, 6);
                break;
            case 2:
                dc.DrawEllipse(B("#FFD54A"), P("#E0A800", 3), new Point(50, 50), 45, 45);
                dc.DrawEllipse(B("#3B2F1E"), null, new Point(35, 40), 5, 8);
                dc.DrawEllipse(B("#3B2F1E"), null, new Point(65, 40), 5, 8);
                dc.DrawGeometry(null, P("#3B2F1E", 5), Geometry.Parse("M28,60 Q50,82 72,60"));
                break;
            case 3:
                for (int i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    dc.DrawLine(P("#FF8F00", 6), new Point(50 + Math.Cos(a) * 32, 50 + Math.Sin(a) * 32), new Point(50 + Math.Cos(a) * 46, 50 + Math.Sin(a) * 46));
                }
                dc.DrawEllipse(B("#FFB300"), P("#FF8F00", 3), new Point(50, 50), 24, 24);
                break;
            case 4:
                var puffs = new[] { (30.0, 60.0, 20.0), (50, 45, 26), (72, 58, 20), (50, 66, 20) };
                foreach (var (x, y, rr) in puffs) dc.DrawEllipse(null, P("#8FA3B8", 6), new Point(x, y), rr, rr);
                foreach (var (x, y, rr) in puffs) dc.DrawEllipse(B("#FFFFFF"), null, new Point(x, y), rr, rr);
                break;
            case 5:
                dc.DrawGeometry(B("#FFE14D"), P("#F2A900", 3), Geometry.Parse("M60,3 L18,56 H45 L36,97 L82,40 H54 Z"));
                break;
            case 6:
                for (int i = 0; i < 5; i++)
                {
                    var a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                    dc.DrawEllipse(B("#FF8FB1"), P("#E5607F", 2), new Point(50 + Math.Cos(a) * 25, 50 + Math.Sin(a) * 25), 20, 20);
                }
                dc.DrawEllipse(B("#FFD54A"), P("#E0A800", 2), new Point(50, 50), 15, 15);
                break;
            case 7:
                dc.DrawGeometry(B("#FFFFFF"), P("#333333", 3),
                    Geometry.Parse("M14,8 H86 Q94,8 94,16 V60 Q94,68 86,68 H40 L22,90 L26,68 H14 Q6,68 6,60 V16 Q6,8 14,8 Z"));
                break;
            default:
                var cols = new[] { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA" };
                for (int i = 0; i < cols.Length; i++)
                {
                    double rr = 44 - i * 6;
                    dc.DrawGeometry(null, new Pen(B(cols[i]), 6.5), Geometry.Parse(
                        FormattableString.Invariant($"M{50 - rr},80 A{rr},{rr} 0 0 1 {50 + rr},80")));
                }
                break;
        }
    }
}

/// <summary>Displays a sticker thumbnail.</summary>
public sealed class StickerView : Control
{
    public int Kind { get; init; }
    public override void Render(DrawingContext ctx) => Stickers.Draw(Kind, ctx, new Rect(Bounds.Size).Deflate(4));
}
