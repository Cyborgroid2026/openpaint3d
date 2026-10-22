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
using Vector3D = OpenPaint3D.Vector3D;

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

Point Tip(string tip)
{
    var c = w.GetVisualDescendants().OfType<Control>().Where(x => ToolTip.GetTip(x) as string == tip && x.IsEffectivelyVisible).LastOrDefault()
            ?? throw new Exception($"tooltip not found: {tip}");
    return c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
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

Shot("welcome");
Check(w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "OpenPaint 3D 시작" && t.IsEffectivelyVisible), "welcome screen is shown at start");
Click(Text("새로 만들기"));
Check(!w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "OpenPaint 3D 시작" && t.IsEffectivelyVisible), "'새로 만들기' closes the welcome screen");
Shot("start");

// colour dialog: '색 추가' opens it, '확인' applies the colour and keeps it as a custom colour
Click(Text("색 추가"));
Check(w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "새 색 선택" && t.IsEffectivelyVisible), "'색 추가' opens the colour dialog");
Shot("color_dialog");
Click(Text("확인"));
Check(!w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "새 색 선택" && t.IsEffectivelyVisible), "'확인' closes the colour dialog");

// --- brushes
Drag(Enumerable.Range(0, 40).Select(i => Canvas(150 + i * 20, 220 + Math.Sin(i / 6.0) * 80)).ToArray());
Check(engine.CanUndo, "marker stroke recorded in history");
Click(Tip("유화 브러시"));
S.Finish = 1;
S.BrushSize = 40;
S.Color = Avalonia.Media.Color.Parse("#E53935");
Drag(Enumerable.Range(0, 40).Select(i => Canvas(150 + i * 20, 420 + Math.Sin(i / 6.0) * 60)).ToArray());
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
Drag(Line(top - new Vector(0, 38), top + new Vector(120, -10), 8));
Check(surface.Pending is { Angle: > 20 }, "rotate knob turns the shape");
int undoBefore = engine.UndoCount;
surface.StampPending();
Check(engine.UndoCount == undoBefore + 1 && surface.Pending != null, "stamp knob leaves a copy and keeps editing");
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
Shot("shapes3d_panel");
Click(Tip("구"));
Drag(Line(Canvas(850, 600), Canvas(1100, 850)));
var sphere = surface.Selected;
Check(sphere != 0 && engine.ObjectCount == 2, "dragging creates a selected sphere");
Shot("sphere_gizmo");
var info = engine.Info(sphere)!.Value;
var scaleBefore = info.Scale.X;
var corner = Canvas(info.Screen.Right, info.Screen.Bottom);
Drag(Line(corner, corner + new Vector(40, 40), 6));
Check(engine.Info(sphere)!.Value.Scale.X > scaleBefore * 1.05, "corner handle scales the object");
Key(Avalonia.Input.Key.Escape);
Click(Tip("강아지"));
Drag(Line(Canvas(1250, 600), Canvas(1650, 950)));
var dog = surface.Selected;
Shot("selection_panel");
info = engine.Info(dog)!.Value;
var bottom = Canvas(info.Screen.Center.X, info.Screen.Bottom) + new Vector(0, 38);
Drag(Line(bottom, bottom + new Vector(80, 0), 8));
Shot("dog_rotated_y");
info = engine.Info(dog)!.Value;
var tz = info.Pos.Z;
var depth = Canvas(info.Screen.Left, info.Screen.Center.Y) - new Vector(38, 0);
Drag(Line(depth, depth + new Vector(0, -60), 6));
Check(engine.Info(dog)!.Value.Pos.Z < tz, "depth handle lifts the object toward the viewer");
Key(Avalonia.Input.Key.Escape);
Click(Tip("부드러운 가장자리"));
Drag(Enumerable.Range(0, 30).Select(i => Canvas(1700 + Math.Cos(i * Math.PI / 14.5) * 120, 300 + Math.Sin(i * Math.PI / 14.5) * 90 * (1 + 0.3 * Math.Sin(i)))).ToArray());
Check(engine.ObjectCount == 4, "soft 3D doodle creates an object");
Shot("doodle");

// --- stickers
Click(Text("스티커"));
Drag(Line(Canvas(500, 80), Canvas(700, 280)));
Check(surface.Pending is ImageItem, "sticker is editable after placing");
Click(Canvas(30, 1050));
Check(surface.Pending == null, "clicking outside commits the sticker");
Click(Tip("질감"));
Click(Tip("나무"));
Drag(Line(Canvas(40, 860), Canvas(400, 1060)));
Key(Avalonia.Input.Key.Enter);
Shot("stickers");

// --- text (2D then 3D)
Click(Text("텍스트"));
Drag(Line(Canvas(450, 880), Canvas(900, 960)));
w.KeyTextInput("안녕하세요 Paint");
Pump();
Click(Canvas(30, 30));
Click(Tip("3D 텍스트"));
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
Click(Tip("노을"));
Check(engine.View.Filter == 0 && engine.View.Tint > 0.4, "preset changes the lighting");
Shot("effects_sunset");
Click(Tip("없음"));

// --- canvas
Click(Text("캔버스"));
var sw = w.GetVisualDescendants().OfType<ToggleSwitch>().ElementAt(1);
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
Click(Tip("우주"));
Click(Text("로켓"));
Check(engine.ObjectCount == 6, "library model is placed");
Shot("library_rocket");

// --- 3D view
Click(Text("3D 보기"));
Check(S.View3D, "3D view turns on");
Shot("view3d");
var c = new Point(500, 450);
Drag(Line(c, c + new Vector(200, 60), 10));
Shot("view3d_orbit");
Click(Tip("장면 전체를 입체로 돌려 봅니다. 드래그: 회전, 휠: 확대/축소"));
Check(!S.View3D, "3D view turns off");

// select tool picks 3D objects
Click(Text("선택"));
var rocket = engine.Info(surface.Selected);
Key(Avalonia.Input.Key.Escape);
Check(surface.Selected == 0, "Escape clears the selection");
var any = engine.Info(sphere)!.Value;
Click(Canvas(any.Screen.Center.X, any.Screen.Center.Y));
Check(surface.Selected == sphere, "select tool picks a 3D object");
Shot("select_tool");

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


// ---------------------------------------------------------------------------
// Paint 3D keyboard shortcuts, 2D selection, multi-select / groups, print
{
bool MenuOpen() => ((Border)typeof(MainWindow).GetField("_menu", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!).IsVisible;
void K(Avalonia.Input.Key k, RawInputModifiers m = RawInputModifiers.None) { w.KeyPress(k, m, PhysicalKey.None, null); w.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
const RawInputModifiers C = RawInputModifiers.Control, SH = RawInputModifiers.Shift;
string Focused() => w.FocusManager?.GetFocusedElement()?.GetType().Name ?? "null";
void Stroke() => Drag(Canvas(200, 200), Canvas(600, 300));
// start from plain tools: the walkthrough above left the oil brush and 3D text on
S.Brush = 0;
S.Text3D = false;
Click(Text("브러시"));
Console.WriteLine("-- 기본 단축키");
Stroke(); int u = engine.UndoCount; K(Avalonia.Input.Key.Z, C); Check(engine.UndoCount == u - 1, "Ctrl+Z"); K(Avalonia.Input.Key.Y, C); Check(engine.UndoCount == u, "Ctrl+Y");
double z = surface.Zoom; K(Avalonia.Input.Key.PageUp, C); Check(surface.Zoom > z, "Ctrl+PgUp 확대");
z = surface.Zoom; K(Avalonia.Input.Key.PageDown, C); Check(surface.Zoom < z, "Ctrl+PgDn 축소");
double bs = S.BrushSize; z = surface.Zoom; K(Avalonia.Input.Key.OemPlus, C); Check(S.BrushSize > bs && surface.Zoom == z, $"Ctrl++ 브러시 크게 {bs}->{S.BrushSize}");
bs = S.BrushSize; K(Avalonia.Input.Key.Subtract, C); Check(S.BrushSize < bs, $"Ctrl+숫자패드- 브러시 작게 {bs}->{S.BrushSize}");
K(Avalonia.Input.Key.D3, C); Check(S.View3D, "Ctrl+3 3D 보기 켜기");
K(Avalonia.Input.Key.E, C | SH); Check(!S.Perspective, "Ctrl+Shift+E 원근감 끄기"); Pump(); K(Avalonia.Input.Key.E, C | SH);
K(Avalonia.Input.Key.D3, C); Check(!S.View3D, "Ctrl+3 3D 보기 끄기");
K(Avalonia.Input.Key.I); Check(S.Picking, "I 스포이드"); K(Avalonia.Input.Key.Escape); Check(!S.Picking, "Esc 스포이드 해제");
var side = w.GetVisualDescendants().OfType<Border>().First(b => b.Child is ScrollViewer); K(Avalonia.Input.Key.M); Check(!side.IsVisible, "M 패널 숨기기"); K(Avalonia.Input.Key.M); Check(side.IsVisible, "M 패널 표시");
K(Avalonia.Input.Key.X, C | SH); Check(S.CropMode, "Ctrl+Shift+X 자르기"); K(Avalonia.Input.Key.Escape); Check(!S.CropMode, "Esc 자르기 취소");
K(Avalonia.Input.Key.E, C); Check(S.Tool == Tool.Canvas, "Ctrl+E 캔버스");
K(Avalonia.Input.Key.P, C); Check(MenuOpen(), "Ctrl+P 인쇄 메뉴"); K(Avalonia.Input.Key.Escape);
// Hangul IME: key reported as ImeProcessed, physical key Z
u = engine.UndoCount; w.KeyPress(Avalonia.Input.Key.ImeProcessed, C, PhysicalKey.Z, null); w.KeyRelease(Avalonia.Input.Key.ImeProcessed, C, PhysicalKey.Z, null); Pump(); Check(engine.UndoCount == u - 1, "한글 입력 상태(ImeProcessed)에서도 Ctrl+Z");
K(Avalonia.Input.Key.Y, C);
// wheel zoom
Click(Text("브러시")); z = surface.Zoom; var mid = Canvas(900, 500); w.MouseWheel(mid, new Vector(0, 1)); Pump(); Check(surface.Zoom > z, "마우스 휠 확대");
w.MouseWheel(mid, new Vector(0, -1)); Pump(); K(Avalonia.Input.Key.D0, C);

Console.WriteLine("-- 3D 개체: 복사/잘라내기/붙여넣기/다중 선택/그룹");
Click(Text("3D 셰이프")); Click(Tip("정육면체")); Drag(Canvas(800, 500), Canvas(1000, 700));
var a = surface.Selected; int n = engine.ObjectCount;
K(Avalonia.Input.Key.C, C); Pump(8); K(Avalonia.Input.Key.V, C); Pump(8); Check(engine.ObjectCount == n + 1, $"Ctrl+C → Ctrl+V 개체 ({n}->{engine.ObjectCount})");
var b = surface.Selected; Check(b != a, "붙여넣은 개체가 선택됨");
n = engine.ObjectCount; K(Avalonia.Input.Key.X, C); Pump(8); Check(engine.ObjectCount == n - 1 && surface.Selected == 0, "Ctrl+X 잘라내기");
K(Avalonia.Input.Key.V, C); Pump(8); Check(engine.ObjectCount == n, "잘라낸 개체 붙여넣기"); b = surface.Selected;
var pos = engine.Info(b)!.Value.Pos; K(Avalonia.Input.Key.Right); K(Avalonia.Input.Key.Down, SH); var p2 = engine.Info(b)!.Value.Pos; Check(p2.X == pos.X + 1 && p2.Y == pos.Y + 10, "방향키 1px / Shift 10px 이동");
K(Avalonia.Input.Key.A, C); Check(surface.SelectedIds.Count == engine.ObjectCount, $"Ctrl+A 개체 모두 선택 ({surface.SelectedIds.Count})");
var before = engine.ObjectIds().ToDictionary(i => i, i => engine.Info(i)!.Value.Pos);
var gi = surface.SelectionInfo()!.Value; var gc = Canvas(gi.Screen.Center.X, gi.Screen.Center.Y);
var ownHit = engine.Info(a)!.Value.Screen.Center; Drag(Canvas(ownHit.X, ownHit.Y), Canvas(ownHit.X + 50, ownHit.Y));
Check(engine.ObjectIds().All(i => Math.Abs(engine.Info(i)!.Value.Pos.X - before[i].X - 50) < 1), "여러 개체를 함께 끌어 옮김");
K(Avalonia.Input.Key.G, C); Check(engine.Group(a) != 0 && engine.Group(a) == engine.Group(b), "Ctrl+G 그룹");
K(Avalonia.Input.Key.Escape); Click(Canvas(ownHit.X + 50, ownHit.Y)); Check(surface.SelectedIds.Count == 2, $"그룹 개체 하나를 누르면 그룹 전체 선택 ({surface.SelectedIds.Count})");
K(Avalonia.Input.Key.G, C | SH); Check(engine.Group(a) == 0, "Ctrl+Shift+G 그룹 해제");
K(Avalonia.Input.Key.Escape); { var ii = engine.Info(b)!.Value; engine.SetObject(b, new OpenPaint3D.Vector3D(ii.Pos.X + 400, ii.Pos.Y, ii.Pos.Z), ii.Scale); surface.Refresh(); }
var ia = engine.Info(a)!.Value.Screen.Center; var ib = engine.Info(b)!.Value.Screen.Center;
Click(Canvas(ia.X, ia.Y)); w.MouseMove(Canvas(ib.X, ib.Y)); w.MouseDown(Canvas(ib.X, ib.Y), MouseButton.Left, C); w.MouseUp(Canvas(ib.X, ib.Y), MouseButton.Left, C); Pump();
Check(surface.SelectedIds.Count == 2, "Ctrl+클릭 다중 선택");
n = engine.ObjectCount; K(Avalonia.Input.Key.D, C); Check(engine.ObjectCount == n + 2, "Ctrl+D 선택한 것 모두 복제");
n = engine.ObjectCount; u = engine.UndoCount; K(Avalonia.Input.Key.Delete); Check(engine.ObjectCount == n - 2 && engine.UndoCount == u + 1, "Delete 여러 개 한 번에(실행 취소 1단계)");
K(Avalonia.Input.Key.Z, C); Check(engine.ObjectCount == n, "삭제 실행 취소");
foreach (var id in engine.ObjectIds()) { surface.Select(id); surface.DeleteSelection(); }

Console.WriteLine("-- 2D 영역 선택");
Click(Text("브러시")); S.Color = Avalonia.Media.Color.Parse("#FF0000"); S.BrushSize = 40; Drag(Canvas(200, 200), Canvas(400, 200));
Click(Text("선택")); Drag(Canvas(150, 150), Canvas(450, 250));
Check(surface.Pending is SelectionItem, "끌어서 영역 선택");
Check(engine.Pick(new Point(300, 200)) is not { A: > 0 } || engine.Pick(new Point(300, 200))!.Value == Avalonia.Media.Color.Parse("#FFFFFF"), "선택한 자리는 비워짐");
var lifted = surface.Pending!.Box; Drag(Canvas(300, 200), Canvas(300, 600));
Check(Math.Abs(surface.Pending!.Box.Y - lifted.Y - 400) < 1, "선택 영역 끌어 옮기기");
K(Avalonia.Input.Key.Enter); Check(surface.Pending == null && engine.Pick(new Point(300, 600))?.R == 255 && engine.Pick(new Point(300, 600))?.G == 0, "Enter로 내려놓기");
u = engine.UndoCount; K(Avalonia.Input.Key.Z, C); Check(engine.Pick(new Point(300, 200))?.G == 0, "실행 취소 한 번에 원래 자리로");
Drag(Canvas(150, 150), Canvas(450, 250)); K(Avalonia.Input.Key.C, C); Pump(8); K(Avalonia.Input.Key.Escape);
K(Avalonia.Input.Key.V, C); Pump(8); Check(surface.Pending is SelectionItem { Lifted: false }, "2D 복사 → 붙여넣기");
K(Avalonia.Input.Key.Escape); int und = engine.UndoCount;
Drag(Canvas(150, 150), Canvas(450, 250)); K(Avalonia.Input.Key.Z, C); Check(surface.Pending == null && engine.Pick(new Point(300, 200))?.G == 0 && engine.RedoCount == 0 || engine.UndoCount == und, "선택 후 Ctrl+Z는 선택 취소(그림 복구)");
Drag(Canvas(150, 150), Canvas(450, 250)); K(Avalonia.Input.Key.Delete); Check(surface.Pending == null && engine.Pick(new Point(300, 200)) is not { R: 255, G: 0 }, "Delete로 선택 영역 지우기");
K(Avalonia.Input.Key.Z, C);
K(Avalonia.Input.Key.A, C); Check(surface.Pending is SelectionItem && surface.Pending.Box.Width == engine.Width, "개체 없을 때 Ctrl+A = 캔버스 전체 선택"); K(Avalonia.Input.Key.Escape);

// stamp a lifted selection, then undo: the stamp goes, the selection keeps floating; undo again restores
for (int i = 0; i < 45; i++) engine.Checkpoint(OpenPaint3D.Label.Brush);   // history full: counts stop changing
Drag(Canvas(150, 150), Canvas(450, 250));
surface.StampPending(); Pump();
K(Avalonia.Input.Key.Z, C); Check(surface.Pending is SelectionItem, "스탬프 뒤 Ctrl+Z: 스탬프만 취소, 선택은 유지 (기록이 가득 찬 상태)");
K(Avalonia.Input.Key.Z, C); Check(surface.Pending == null && engine.Pick(new Point(300, 200))?.G == 0, "한 번 더 Ctrl+Z: 원래 그림으로");
// history flyout commits a floating selection first
Drag(Canvas(150, 150), Canvas(450, 250)); Click(Text("기록")); Check(surface.Pending == null, "기록 열면 떠 있는 선택을 먼저 내려놓음"); K(Avalonia.Input.Key.Escape);
Console.WriteLine("-- 포커스");
Click(Text("브러시"));
var box = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsEffectivelyVisible);
Click(box.TranslatePoint(new Point(10, 10), w)!.Value); w.KeyTextInput("30"); K(Avalonia.Input.Key.Enter); Pump(6);
Check(S.BrushSize == 30, $"두께 칸 클릭 후 입력하면 값이 바뀜 (두께 {S.BrushSize}, 칸 '{box.Text}')");
Stroke(); Click(box.TranslatePoint(new Point(10, 10), w)!.Value); K(Avalonia.Input.Key.Enter); Pump(6);
u = engine.UndoCount; K(Avalonia.Input.Key.Z, C); Check(engine.UndoCount == u - 1, $"Enter 뒤 단축키 다시 동작 (포커스 {Focused()})");
var combo = w.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsEffectivelyVisible);
Click(combo.TranslatePoint(new Point(20, 10), w)!.Value); Pump(); Check(combo.IsDropDownOpen, "재질 콤보 클릭하면 목록 열림"); K(Avalonia.Input.Key.Escape); combo.IsDropDownOpen = false; Pump();
int fin = combo.SelectedIndex; K(Avalonia.Input.Key.Down); Check(combo.SelectedIndex == fin, $"↓ 키가 재질을 바꾸지 않음 (포커스 {Focused()})");

Console.WriteLine("-- 텍스트");
Click(Text("텍스트")); Drag(Canvas(300, 700), Canvas(800, 800)); w.KeyTextInput("가나다"); Pump();
bool b0 = S.Bold; K(Avalonia.Input.Key.B, C); Check(S.Bold != b0 && ((TextItem)surface.Pending!).Bold == S.Bold, "Ctrl+B 굵게"); K(Avalonia.Input.Key.B, C);
bool i0 = S.Italic; K(Avalonia.Input.Key.I, C); Check(S.Italic != i0, "Ctrl+I 기울임꼴"); K(Avalonia.Input.Key.I, C);
bool u0 = S.Underline; K(Avalonia.Input.Key.U, C); Check(S.Underline != u0, "Ctrl+U 밑줄"); K(Avalonia.Input.Key.U, C);
S.TextBg = true; S.TextBgColor = Avalonia.Media.Color.Parse("#00FF00"); surface.SyncPending();
K(Avalonia.Input.Key.Escape); Check(surface.Pending == null, "Esc로 텍스트 편집 끝내기");
Check(engine.Pick(new Point(790, 790))?.G == 255, "배경 채우기가 텍스트 상자에 칠해짐");
S.TextBg = false;
Console.WriteLine("-- 메뉴");
Click(Text("메뉴")); Click(Text("인쇄")); Check(w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "2D 인쇄" && t.IsEffectivelyVisible), "메뉴에 인쇄 페이지");
var pdf = Path.Combine(outDir, "print.pdf"); Export.SavePdf(engine, pdf, true); Check(new FileInfo(pdf).Length > 1000 && File.ReadAllText(pdf).StartsWith("%PDF"), "PDF 저장");
K(Avalonia.Input.Key.Escape);
var proj = Path.Combine(outDir, "groups.op3d");
Click(Text("3D 셰이프")); Click(Tip("구")); Drag(Canvas(800, 500), Canvas(1000, 700)); K(Avalonia.Input.Key.D, C); K(Avalonia.Input.Key.A, C); K(Avalonia.Input.Key.G, C);
Export.SaveProject(engine, proj); using (var e2 = new Engine(10, 10)) { Check(Export.LoadProject(e2, proj) && e2.ObjectIds().All(i => e2.Group(i) != 0), "그룹이 프로젝트 파일에 저장됨"); }
}

Console.WriteLine(failures.Count == 0 ? "ALL PASSED" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;
