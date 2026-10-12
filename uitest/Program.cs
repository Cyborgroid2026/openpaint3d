// Headless UI walkthrough for OpenPaint 3D: drives the real MainWindow with
// simulated mouse/keyboard input and saves a screenshot after each step.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenPaint3D;

var outDir = args.Length > 0 ? args[0] : "shots";
Directory.CreateDirectory(outDir);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

var w = new MainWindow(Array.Empty<string>()) { Width = 1400, Height = 880 };
w.Show();
Pump();
var S = (ToolState)typeof(MainWindow).GetField("S", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
var surface = w.GetVisualDescendants().OfType<PaintSurface>().First();
var engine = surface.Engine;
int step = 0;
var failures = new List<string>();

void Pump(int n = 4)
{
    for (int i = 0; i < n; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}

void Shot(string name)
{
    Pump();
    var frame = w.CaptureRenderedFrame();
    frame?.Save(Path.Combine(outDir, $"{++step:00}_{name}.png"));
}

void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures.Add(what);
}

Point Text(string text)
{
    var t = w.GetVisualDescendants().OfType<TextBlock>().Where(x => x.Text == text && x.IsEffectivelyVisible).LastOrDefault()
            ?? throw new Exception($"text not found: {text}");
    return t.TranslatePoint(new Point(t.Bounds.Width / 2, t.Bounds.Height / 2), w)!.Value;
}

Point Canvas(double x, double y)
{
    // canvas pixel -> window point (pan is zero in this walkthrough)
    var o = new Point((surface.Bounds.Width - engine.Width * surface.Zoom) / 2, (surface.Bounds.Height - engine.Height * surface.Zoom) / 2);
    o = new Point(Math.Round(o.X), Math.Round(o.Y));
    return surface.TranslatePoint(new Point(o.X + x * surface.Zoom, o.Y + y * surface.Zoom), w)!.Value;
}

void Click(Point p)
{
    w.MouseMove(p);
    w.MouseDown(p, MouseButton.Left);
    w.MouseUp(p, MouseButton.Left);
    Pump();
}

void Drag(params Point[] pts)
{
    w.MouseMove(pts[0]);
    w.MouseDown(pts[0], MouseButton.Left);
    Pump(1);
    for (int i = 1; i < pts.Length; i++)
    {
        w.MouseMove(pts[i], RawInputModifiers.LeftMouseButton);
        Pump(1);
    }
    w.MouseUp(pts[^1], MouseButton.Left);
    Pump();
}

Point[] Line(Point a, Point b, int n = 12) => Enumerable.Range(0, n + 1).Select(i => a + (b - a) * (i / (double)n)).ToArray();

void Key(Key k) { w.KeyPress(k, RawInputModifiers.None, PhysicalKey.None, null); w.KeyRelease(k, RawInputModifiers.None, PhysicalKey.None, null); Pump(); }

Shot("start");

// --- brushes
Drag(Enumerable.Range(0, 40).Select(i => Canvas(150 + i * 20, 220 + Math.Sin(i / 6.0) * 80)).ToArray());
Check(engine.CanUndo, "marker stroke recorded in history");
Click(Text("유화 브러시"));
S.Finish = 1;
S.BrushSize = 40;
S.Color = Avalonia.Media.Color.Parse("#E53935");
Drag(Enumerable.Range(0, 40).Select(i => Canvas(150 + i * 20, 420 + Math.Sin(i / 6.0) * 60)).ToArray());
Click(Text("채우기"));
S.Finish = 0;
Shot("brushes_gloss");

// --- 2D shapes: star with gradient fill, then make it 3D
Click(Text("2D 셰이프"));
S.Shape = 13;
S.Line = -1;
S.FillMode = 2;
S.Color = Avalonia.Media.Color.Parse("#1F1F1F");
Drag(Line(Canvas(1050, 150), Canvas(1350, 450)));
Check(surface.Pending is ShapeItem, "dragging a 2D shape leaves an editable item");
Shot("shape_pending");
// rotate it with the rotate handle (30 px above the top edge)
var top = Canvas(1200, 150);
Drag(Line(top - new Vector(0, 30), top + new Vector(120, -10), 8));
Check(surface.Pending is { Angle: > 20 }, "rotate handle turns the shape");
Click(Text("3D로 만들기"));
Check(surface.Pending == null && surface.Selected != 0, "'3D로 만들기' turns the shape into a 3D object");
Shot("shape_made_3d");

// curve with 4 points, then bend one point
Click(Text("2D 셰이프"));
S.Line = 2;
Drag(Line(Canvas(150, 650), Canvas(750, 650)));
Check(surface.Pending is CurveItem { Points.Count: 4 }, "4-point curve has 4 control points");
Drag(Line(Canvas(350, 650), Canvas(350, 520), 6));
Key(Avalonia.Input.Key.Enter);
Check(surface.Pending == null, "Enter commits the curve");
Shot("curve");

// --- 3D shapes
Click(Text("3D 셰이프"));
Click(Text("구"));
Drag(Line(Canvas(850, 600), Canvas(1100, 850)));
var sphere = surface.Selected;
Check(sphere != 0 && engine.ObjectCount == 2, "dragging creates a selected sphere");
Shot("sphere_gizmo");
var info = engine.Info(sphere)!.Value;
var scaleBefore = info.Scale.X;
var corner = Canvas(info.Screen.Right, info.Screen.Bottom);
Drag(Line(corner, corner + new Vector(40, 40), 6));
Check(engine.Info(sphere)!.Value.Scale.X > scaleBefore * 1.05, "corner handle scales the object");
Click(Text("강아지"));
Drag(Line(Canvas(1250, 600), Canvas(1650, 950)));
var dog = surface.Selected;
info = engine.Info(dog)!.Value;
var left = Canvas(info.Screen.Left, info.Screen.Center.Y) - new Vector(26, 0);
Drag(Line(left, left + new Vector(80, 0), 8));
Shot("dog_rotated_y");
info = engine.Info(dog)!.Value;
var tz = info.Pos.Z;
var depth = Canvas(info.Screen.Right, info.Screen.Center.Y) + new Vector(26, 0);
Drag(Line(depth, depth + new Vector(0, -60), 6));
Check(engine.Info(dog)!.Value.Pos.Z < tz, "depth handle lifts the object toward the viewer");
Click(Text("부드러운 가장자리"));
Drag(Enumerable.Range(0, 30).Select(i => Canvas(1700 + Math.Cos(i * Math.PI / 14.5) * 120, 300 + Math.Sin(i * Math.PI / 14.5) * 90 * (1 + 0.3 * Math.Sin(i)))).ToArray());
Check(engine.ObjectCount == 4, "soft 3D doodle creates an object");
Shot("doodle");

// --- stickers
Click(Text("스티커"));
Drag(Line(Canvas(500, 80), Canvas(700, 280)));
Check(surface.Pending is ImageItem, "sticker is editable after placing");
Click(Canvas(30, 1050));
Check(surface.Pending == null, "clicking outside commits the sticker");
Click(Text("질감"));
Click(Text("나무"));
Drag(Line(Canvas(40, 860), Canvas(400, 1060)));
Click(Text("완료"));
Shot("stickers");

// --- text (2D then 3D)
Click(Text("텍스트"));
Drag(Line(Canvas(450, 880), Canvas(900, 960)));
w.KeyTextInput("안녕하세요 Paint");
Pump();
Click(Canvas(30, 30));
Click(Text("3D 텍스트"));
S.FontSize = 120;
S.Color = Avalonia.Media.Color.Parse("#7E57C2");
Drag(Line(Canvas(950, 880), Canvas(1300, 1040)));
w.KeyTextInput("3D");
Pump();
Click(Canvas(30, 30));
Check(engine.ObjectCount == 5, "3D text becomes an object");
Shot("text");

// --- effects
Click(Text("효과"));
Click(Text("노을"));
Check(engine.View.Filter == 0 && engine.View.Tint > 0.4, "preset changes the lighting");
Shot("effects_sunset");
Click(Text("없음"));

// --- canvas
Click(Text("캔버스"));
var sw = w.GetVisualDescendants().OfType<ToggleSwitch>().First();
Click(sw.TranslatePoint(new Point(sw.Bounds.Width / 2, sw.Bounds.Height / 2), w)!.Value);
Check(engine.View.ShowBg == 0, "canvas toggle hides the background");
Shot("canvas_transparent");
Click(sw.TranslatePoint(new Point(sw.Bounds.Width / 2, sw.Bounds.Height / 2), w)!.Value);
int w0 = engine.Width;
var edge = Canvas(engine.Width, engine.Height / 2.0);
Drag(Line(edge, edge + new Vector(60, 0), 6));
Check(engine.Width > w0, "dragging the canvas edge handle widens the canvas");

// --- 3D library
Click(Text("3D 라이브러리"));
Shot("library");
Click(Text("로켓"));
Check(engine.ObjectCount == 6, "library model is placed");
Shot("library_rocket");

// --- 3D view
var toggle3d = w.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Content is StackPanel sp && sp.Children.OfType<TextBlock>().Any(x => x.Text == "3D 보기"));
Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
Click(Center(toggle3d));
Check(S.View3D, "3D view turns on");
Shot("view3d");
var c = new Point(500, 450);
Drag(Line(c, c + new Vector(200, 60), 10));
Shot("view3d_orbit");
Click(Center(toggle3d));
Check(!S.View3D, "3D view turns off");

// --- history + menu
Click(Text("기록"));
Shot("history");
Key(Avalonia.Input.Key.Escape);
Click(Text("메뉴"));
Click(Text("다른 이름으로 저장"));
Shot("menu_save_as");
Key(Avalonia.Input.Key.Escape);

// --- exporters
foreach (var ext in new[] { "png", "jpg", "bmp", "gif", "tif" }) Export.SaveImage(engine, Path.Combine(outDir, $"export.{ext}"));
Export.SaveGlb(engine, Path.Combine(outDir, "export.glb"));
Export.Save3mf(engine, Path.Combine(outDir, "export.3mf"));
Export.SaveProject(engine, Path.Combine(outDir, "export.op3d"));
using (var e2 = new Engine(10, 10))
{
    Check(Export.LoadProject(e2, Path.Combine(outDir, "export.op3d")) && e2.ObjectCount == engine.ObjectCount && e2.Width == engine.Width,
        "project file round-trips canvas and objects");
}

// undo everything back to the start
int undos = 0;
while (engine.Undo()) undos++;
Check(undos > 15 && engine.ObjectCount == 0, $"undo walks back through history ({undos} steps)");

Console.WriteLine(failures.Count == 0 ? "ALL PASSED" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;
