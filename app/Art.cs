using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpenPaint3D;

/// <summary>Line icons (24x24 design grid, stroked).</summary>
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
    public const string Library = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M17,14 L20,17 L17,20 L14,17 Z";
    public const string Undo = "M9,5 L4,10 L9,15 M4,10 H15 A5,5 0 0 1 15,20 H11";
    public const string Redo = "M15,5 L20,10 L15,15 M20,10 H9 A5,5 0 0 0 9,20 H13";
    public const string History = "M4,12 A8,8 0 1 0 6.3,6.3 M4,4 V9 H9 M12,7 V12 L15,14";
    public const string Paste = "M8,4 H5 V21 H19 V4 H16 M8,2 H16 V6 H8 Z M9,11 H15 M9,15 H15";
    public const string Picker = "M15,3 L21,9 M18,6 L8,16 L5,19 M8,16 H5 V19 M13,5 L19,11";
    public const string Bucket = "M5,11 L12,4 L19,11 L12,18 Z M19,11 C21,14 21,16 20,17 C19,18 18,17 18,15 Z M5,11 H19";
    public const string View3D = "M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z M4,7.5 L12,12 L20,7.5 M12,12 V21 M1,12 C3,9 5,9 7,10 M17,10 C19,9 21,9 23,12";
    public const string Back = "M15,5 L8,12 L15,19";
    public const string New = "M6,2 H14 L19,7 V22 H6 Z M14,2 V7 H19 M12,11 V18 M8.5,14.5 H15.5";
    public const string Open = "M3,6 H10 L12,8 H21 V19 H3 Z M3,11 H21";
    public const string Insert = "M4,4 H20 V20 H4 Z M4,16 L9,11 L13,15 L16,12 L20,16 M15,8 A1.5,1.5 0 1 0 15,8.01";
    public const string Save = "M4,4 H17 L20,7 V20 H4 Z M8,4 V9 H15 V4 M7,20 V13 H17 V20";
    public const string SaveAs = "M4,4 H17 L20,7 V13 M4,4 V20 H11 M8,4 V9 H15 V4 M14,20 L20,14 L22,16 L16,22 H14 Z";
    public const string Info = "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z M12,10 V17 M12,7 V7.5";
    public const string Exit = "M14,4 H20 V20 H14 M10,8 L6,12 L10,16 M6,12 H16";
    public const string Print = "M7,9 V4 H17 V9 M6,17 H4 V10 H20 V17 H18 M7,14 H17 V20 H7 Z";
    public const string Print3D = "M4,20 H20 M6,20 V8 H18 V20 M9,8 V5 H15 V8 M12,11 L9,13 V17 L12,19 L15,17 V13 Z";
    public const string MakeSticker = "M5,5 H19 V13 L13,19 H5 Z M13,19 V13 H19";
    public const string Crop = "M6,2 V18 H22 M2,6 H18 V22";
    public const string Trash = "M4,6 H20 M9,6 V3 H15 V6 M6,6 L7,21 H17 L18,6 M10,10 V17 M14,10 V17";
    public const string Copy = "M8,8 H20 V20 H8 Z M4,16 V4 H16";
    public const string RotateLeft = "M4,4 V9 H9 M4.5,9 A8,8 0 1 1 6,16";
    public const string RotateRight = "M20,4 V9 H15 M19.5,9 A8,8 0 1 0 18,16";
    public const string FlipH = "M12,2 V22 M9,6 L3,18 H9 Z M15,6 L21,18 H15 Z";
    public const string FlipV = "M2,12 H22 M6,9 L18,3 V9 Z M6,15 L18,21 V15 Z";
    public const string Make3D = "M5,9 L12,5 L19,9 L12,13 Z M5,9 V15 L12,19 V13 M19,9 V15 L12,19";
    public const string Search = "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 Z M15,15 L21,21";
    public const string Select = "M6,3 L6,19 L10,15 L13,21 L15.5,20 L12.5,14 H18 Z";
    public const string MagicSelect = "M3,7 V3 H7 M17,3 H21 V7 M21,17 V21 H17 M7,21 H3 V17 M12,7 A2.5,2.5 0 1 0 12,12 A2.5,2.5 0 1 0 12,7 Z M7.5,18 C8,15 10,14 12,14 C14,14 16,15 16.5,18";
    public const string MixedReality = "M3,8 C3,6 5,5 7,5 H17 C19,5 21,6 21,8 V15 C21,17 19,18 17,18 H15 L12,15 L9,18 H7 C5,18 3,17 3,15 Z";
    public const string Plane3D = "M5,6 L19,3 V17 L5,20 Z";
    public const string Minus = "M5,12 H19";
    public const string Plus = "M5,12 H19 M12,5 V19";
    public const string More = "M5,12 H5.2 M12,12 H12.2 M19,12 H19.2";
    public const string ChevronUp = "M5,15 L12,8 L19,15";
    public const string ChevronRight = "M9,5 L16,12 L9,19";
    public const string Palette = "M12,3 C7,3 3,7 3,12 C3,17 7,21 11,21 C13,21 13,19 12.5,18 C12,17 12.5,15.5 14,15.5 H17 C19.5,15.5 21,14 21,11.5 C21,7 17,3 12,3 Z M7.5,12 V12.2 M9.5,7.5 V7.7 M14.5,7.5 V7.7";
    public const string SelectAll = "M4,4 H20 V20 H4 Z M8,8 H16 V16 H8 Z M12,8 V16 M8,12 H16";
    public const string MultiSelect = "M4,6 H15 V17 H4 Z M9,3 H20 V14 M13,13 L19,19 M13,13 L13,17 M13,13 H17";
    public const string Group = "M5,5 H19 V19 H5 Z M8,8 H12 V12 H8 Z M12,12 H16 V16 H12 Z";
    public const string Ungroup = "M3,3 H11 V11 H3 Z M13,13 H21 V21 H13 Z M6,6 H8 V8 H6 Z M16,16 H18 V18 H16 Z";
    public const string Cut = "M7,17 A3,3 0 1 0 7,23 A3,3 0 1 0 7,17 Z M17,17 A3,3 0 1 0 17,23 A3,3 0 1 0 17,17 Z M8.5,17.5 L16,3 M15.5,17.5 L8,3";
    public const string Minimize = "M5,12 H19";
    public const string Maximize = "M5,5 H19 V19 H5 Z";
    public const string Restore = "M5,8 H16 V19 H5 Z M8,8 V5 H19 V16 H16";
    public const string Close = "M5,5 L19,19 M19,5 L5,19";
    public const string Settings = "M12,8.5 A3.5,3.5 0 1 0 12,15.5 A3.5,3.5 0 1 0 12,8.5 Z M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9";
    public const string Text2D = "M4,4 H20 V20 H4 Z M8,8 H16 M12,8 V17";
    public const string Text3D = "M5,8 L9,4 H20 V16 L16,20 H5 Z M5,8 H16 V20 M16,8 L20,4 M8,11 H13 M10.5,11 V17";
    public const string StickerTab = "M4,4 H20 V14 L14,20 H4 Z M14,20 V14 H20 M8.5,9 V9.2 M15.5,9 V9.2 M8.5,13 Q12,16 15.5,13";
    public const string TextureTab = "M4,4 H20 V20 H4 Z M4,9.3 H20 M4,14.6 H20 M9.3,4 V20 M14.6,4 V20";
    public const string CustomTab = "M4,6 H20 V19 H4 Z M4,15 L9,10.5 L13,14 L15.5,12 L20,16 M15,8.5 A1.5,1.5 0 1 0 15,8.51 M18,2 V6 M16,4 H20";

    /// <summary>Icon drawn on its 24px grid and scaled to <paramref name="size"/> (keeps flat lines like "—" intact).</summary>
    public static Control Make(string data, double size = 24, IBrush? stroke = null)
    {
        double px = size <= 18 ? 1.3 : 1.5;
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke ?? new SolidColorBrush(Color.Parse("#262626")),
            StrokeThickness = px * 24 / size,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }
}

/// <summary>The 24 2D shapes of the 2D shapes panel, built for any box.</summary>
public static class Shapes2D
{
    public static readonly string[] Names =
    {
        "사각형", "둥근 사각형", "원", "캡슐", "삼각형", "직각 삼각형", "마름모", "평행사변형",
        "사다리꼴", "오각형", "육각형", "팔각형", "별(4개 꼭짓점)", "별(5개 꼭짓점)", "별(6개 꼭짓점)", "하트",
        "십자", "오른쪽 화살표", "위쪽 화살표", "양방향 화살표", "갈매기형", "사각형 말풍선", "타원형 말풍선", "번개",
    };

    public static readonly string[] LineNames = { "직선", "3점 곡선", "4점 곡선", "5점 곡선" };

    private static Geometry Poly(Rect r, params double[] xy)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i < xy.Length; i += 2)
            {
                var p = new Point(r.X + xy[i] * r.Width, r.Y + xy[i + 1] * r.Height);
                if (i == 0) c.BeginFigure(p, true);
                else c.LineTo(p);
            }
            c.EndFigure(true);
        }
        return g;
    }

    private static Geometry Star(Rect r, int n, double inner)
    {
        var xy = new List<double>();
        for (int i = 0; i < n * 2; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / n, k = i % 2 == 0 ? 0.5 : 0.5 * inner;
            xy.Add(0.5 + Math.Cos(a) * k);
            xy.Add(0.5 + Math.Sin(a) * k);
        }
        return Poly(r, xy.ToArray());
    }

    private static Geometry Regular(Rect r, int n)
    {
        var xy = new List<double>();
        for (int i = 0; i < n; i++)
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n + (n % 2 == 0 ? Math.PI / n : 0);
            xy.Add(0.5 + Math.Cos(a) * 0.5);
            xy.Add(0.5 + Math.Sin(a) * 0.5);
        }
        return Poly(r, xy.ToArray());
    }

    private static Geometry Svg(Rect r, string d)
    {
        // path authored on a 100x100 grid
        var g = Geometry.Parse(d);
        g.Transform = new MatrixTransform(Matrix.CreateScale(r.Width / 100, r.Height / 100) * Matrix.CreateTranslation(r.X, r.Y));
        return g;
    }

    public static Geometry Build(int kind, Rect r)
    {
        double rr = Math.Min(r.Width, r.Height);
        return kind switch
        {
            0 => new RectangleGeometry(r),
            1 => new RectangleGeometry(r, rr * 0.18, rr * 0.18),
            2 => new EllipseGeometry(r),
            3 => new RectangleGeometry(r, rr / 2, rr / 2),
            4 => Poly(r, 0.5, 0, 1, 1, 0, 1),
            5 => Poly(r, 0, 0, 1, 1, 0, 1),
            6 => Poly(r, 0.5, 0, 1, 0.5, 0.5, 1, 0, 0.5),
            7 => Poly(r, 0.25, 0, 1, 0, 0.75, 1, 0, 1),
            8 => Poly(r, 0.22, 0, 0.78, 0, 1, 1, 0, 1),
            9 => Regular(r, 5),
            10 => Regular(r, 6),
            11 => Regular(r, 8),
            12 => Star(r, 4, 0.4),
            13 => Star(r, 5, 0.45),
            14 => Star(r, 6, 0.55),
            15 => Svg(r, "M50,96 C18,72 0,54 0,32 C0,14 13,2 28,2 C39,2 46,8 50,16 C54,8 61,2 72,2 C87,2 100,14 100,32 C100,54 82,72 50,96 Z"),
            16 => Poly(r, 0.33, 0, 0.67, 0, 0.67, 0.33, 1, 0.33, 1, 0.67, 0.67, 0.67, 0.67, 1, 0.33, 1, 0.33, 0.67, 0, 0.67, 0, 0.33, 0.33, 0.33),
            17 => Poly(r, 0, 0.3, 0.6, 0.3, 0.6, 0, 1, 0.5, 0.6, 1, 0.6, 0.7, 0, 0.7),
            18 => Poly(r, 0.5, 0, 1, 0.4, 0.7, 0.4, 0.7, 1, 0.3, 1, 0.3, 0.4, 0, 0.4),
            19 => Poly(r, 0, 0.5, 0.25, 0, 0.25, 0.3, 0.75, 0.3, 0.75, 0, 1, 0.5, 0.75, 1, 0.75, 0.7, 0.25, 0.7, 0.25, 1),
            20 => Poly(r, 0, 0, 0.6, 0, 1, 0.5, 0.6, 1, 0, 1, 0.4, 0.5),
            21 => Svg(r, "M8,0 H92 Q100,0 100,8 V62 Q100,70 92,70 H42 L22,100 L26,70 H8 Q0,70 0,62 V8 Q0,0 8,0 Z"),
            22 => Svg(r, "M50,0 C78,0 100,16 100,36 C100,56 78,72 50,72 C44,72 38,71 33,70 L14,96 L19,64 C7,58 0,48 0,36 C0,16 22,0 50,0 Z"),
            _ => Poly(r, 0.6, 0, 0.15, 0.56, 0.45, 0.56, 0.35, 1, 0.85, 0.4, 0.55, 0.4),
        };
    }

    /// <summary>Smooth curve through the given points (Catmull-Rom as cubic Béziers).</summary>
    public static Geometry Curve(IReadOnlyList<Point> p)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(p[0], false);
        if (p.Count == 2)
        {
            c.LineTo(p[1]);
        }
        else
        {
            for (int i = 0; i < p.Count - 1; i++)
            {
                var p0 = p[Math.Max(i - 1, 0)];
                var p1 = p[i];
                var p2 = p[i + 1];
                var p3 = p[Math.Min(i + 2, p.Count - 1)];
                c.CubicBezierTo(p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2);
            }
        }
        c.EndFigure(false);
        return g;
    }
}

/// <summary>Original vector stickers, drawn on a 100x100 grid.</summary>
public static class Stickers
{
    public static readonly string[] Names =
    {
        "별", "하트", "스마일", "해", "구름", "번개", "꽃", "말풍선", "무지개", "체크", "엑스", "물음표", "느낌표", "음표", "왕관",
    };

    private static IBrush B(string c) => new SolidColorBrush(Color.Parse(c));
    private static IPen P(string c, double w) => new Pen(B(c), w, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static void Glyph(DrawingContext dc, string text, string fill)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Black), 64, B(fill));
        dc.DrawText(ft, new Point(50 - ft.Width / 2, 50 - ft.Height / 2));
    }

    public static void Draw(int kind, DrawingContext dc, Rect r)
    {
        using var _ = dc.PushTransform(Matrix.CreateScale(r.Width / 100, r.Height / 100) * Matrix.CreateTranslation(r.X, r.Y));
        var box = new Rect(0, 0, 100, 100);
        switch (kind)
        {
            case 0:
                dc.DrawGeometry(B("#FFC83D"), P("#E09B00", 3), Shapes2D.Build(13, box.Deflate(4)));
                break;
            case 1:
                dc.DrawGeometry(B("#F25C6E"), P("#C93347", 3), Shapes2D.Build(15, box.Deflate(5)));
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
                dc.DrawGeometry(B("#FFE14D"), P("#F2A900", 3), Shapes2D.Build(23, box.Deflate(6)));
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
                dc.DrawGeometry(B("#FFFFFF"), P("#333333", 3), Shapes2D.Build(21, box.Deflate(6)));
                break;
            case 8:
                var cols = new[] { "#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA" };
                for (int i = 0; i < cols.Length; i++)
                {
                    double rr = 44 - i * 6;
                    dc.DrawGeometry(null, new Pen(B(cols[i]), 6.5), Geometry.Parse(
                        FormattableString.Invariant($"M{50 - rr},80 A{rr},{rr} 0 0 1 {50 + rr},80")));
                }
                break;
            case 9:
                dc.DrawEllipse(B("#43A047"), null, new Point(50, 50), 45, 45);
                dc.DrawGeometry(null, P("#FFFFFF", 10), Geometry.Parse("M27,52 L43,68 L74,34"));
                break;
            case 10:
                dc.DrawEllipse(B("#E53935"), null, new Point(50, 50), 45, 45);
                dc.DrawGeometry(null, P("#FFFFFF", 10), Geometry.Parse("M33,33 L67,67 M67,33 L33,67"));
                break;
            case 11:
                dc.DrawEllipse(B("#1E88E5"), null, new Point(50, 50), 45, 45);
                Glyph(dc, "?", "#FFFFFF");
                break;
            case 12:
                dc.DrawGeometry(B("#FDD835"), P("#F2A900", 3), Geometry.Parse("M50,6 L96,90 H4 Z"));
                Glyph(dc, "!", "#3B2F1E");
                break;
            case 13:
                dc.DrawGeometry(B("#5E35B1"), null, Geometry.Parse("M38,18 L84,8 V68 A12,10 0 1 1 76,59 V28 L46,35 V78 A12,10 0 1 1 38,69 Z"));
                break;
            default:
                dc.DrawGeometry(B("#FFC83D"), P("#C98A00", 3), Geometry.Parse("M10,78 L4,26 L30,48 L50,14 L70,48 L96,26 L90,78 Z"));
                dc.DrawRectangle(B("#E0A800"), null, new Rect(10, 78, 80, 12));
                foreach (var x in new[] { 30.0, 50, 70 }) dc.DrawEllipse(B("#E53935"), null, new Point(x, 64), 5, 5);
                break;
        }
    }
}

/// <summary>Procedural texture stickers (wood, grass, marble, metal, brick, water).</summary>
public static class Textures
{
    public static readonly string[] Names = { "나무", "잔디", "대리석", "금속", "벽돌", "물" };
    private static readonly Dictionary<int, WriteableBitmap> Cache = new();

    private static double Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535.0;
        }
    }

    /// <summary>Value noise that repeats every (px, py) cells so textures tile seamlessly.</summary>
    private static double Noise(double x, double y, int s, int px, int py)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
        int W(int a, int p) => ((a % p) + p) % p;
        double a = Hash(W(xi, px), W(yi, py), s), b = Hash(W(xi + 1, px), W(yi, py), s);
        double c = Hash(W(xi, px), W(yi + 1, py), s), d = Hash(W(xi + 1, px), W(yi + 1, py), s);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    /// <summary>Fractal noise over the unit square with integer base frequencies (tiles seamlessly).</summary>
    private static double Fbm(double u, double v, int fx, int fy, int s)
    {
        double sum = 0, amp = 0.5;
        for (int i = 0, f = 1; i < 5; i++, f *= 2)
        {
            sum += amp * Noise(u * fx * f, v * fy * f, s + i, fx * f, fy * f);
            amp *= 0.5;
        }
        return sum;
    }

    private static (double, double, double) Mix((double, double, double) a, (double, double, double) b, double t) =>
        (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);

    public static unsafe WriteableBitmap Get(int kind)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        const int n = 256;
        var bmp = new WriteableBitmap(new PixelSize(n, n), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            for (int y = 0; y < n; y++)
            {
                var row = (byte*)fb.Address + y * fb.RowBytes;
                for (int x = 0; x < n; x++)
                {
                    double u = x / (double)n, v = y / (double)n;
                    (double r, double g, double b) c = kind switch
                    {
                        0 => Mix((0.55, 0.33, 0.16), (0.78, 0.55, 0.3), 0.5 + 0.5 * Math.Sin((v * 18 + Fbm(u, v, 4, 4, 1) * 6) * Math.PI)),
                        1 => Mix((0.18, 0.45, 0.12), (0.45, 0.75, 0.25), Fbm(u, v, 10, 30, 2) * 0.7 + Noise(u * 80, v * 12, 3, 80, 12) * 0.5),
                        2 => Mix((0.93, 0.93, 0.92), (0.45, 0.47, 0.5), Math.Pow(1 - Math.Abs(Math.Sin((u * 3 + v * 2 + Fbm(u, v, 3, 3, 4) * 3) * Math.PI)), 6)),
                        3 => Mix((0.55, 0.57, 0.6), (0.85, 0.87, 0.9), Noise(u * 3, v * 200, 5, 3, 200) * 0.5 + Fbm(u, v, 2, 2, 6) * 0.6),
                        4 => Brick(u, v),
                        _ => Mix((0.05, 0.35, 0.65), (0.45, 0.8, 0.95), Math.Pow(0.5 + 0.5 * Math.Sin((u * 6 + Fbm(u, v, 3, 3, 7) * 4 + v * 2) * Math.PI), 3)),
                    };
                    row[x * 4 + 0] = (byte)(Math.Clamp(c.b, 0, 1) * 255);
                    row[x * 4 + 1] = (byte)(Math.Clamp(c.g, 0, 1) * 255);
                    row[x * 4 + 2] = (byte)(Math.Clamp(c.r, 0, 1) * 255);
                    row[x * 4 + 3] = 255;
                }
            }
        }
        Cache[kind] = bmp;
        return bmp;

        static (double, double, double) Brick(double u, double v)
        {
            double rows = 8, cols = 4;
            double ry = v * rows, row = Math.Floor(ry);
            double rx = u * cols + (row % 2) * 0.5;
            double fx = rx - Math.Floor(rx), fy = ry - row;
            bool mortar = fx < 0.05 || fy < 0.1;
            if (mortar) return (0.82, 0.8, 0.75);
            double k = Hash((int)Math.Floor(rx) % 4, (int)row, 9) * 0.25 + Noise(u * 60, v * 60, 10, 60, 60) * 0.15;
            return (0.62 + k, 0.25 + k * 0.4, 0.18 + k * 0.3);
        }
    }
}

/// <summary>Displays a sticker or texture thumbnail.</summary>
public sealed class StickerView : Control
{
    public int Kind { get; init; }
    public override void Render(DrawingContext ctx) => Stickers.Draw(Kind, ctx, new Rect(Bounds.Size).Deflate(4));
}
