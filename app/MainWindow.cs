using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OpenPaint3D;

public sealed class MainWindow : Window
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#0078D7"));
    private static readonly IBrush AccentLight = new SolidColorBrush(Color.Parse("#E5F1FB"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E1E1E1"));
    private static readonly IBrush TileBg = new SolidColorBrush(Color.Parse("#F4F4F4"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#666666"));
    private static readonly IBrush Dark = new SolidColorBrush(Color.Parse("#262626"));

    private static readonly string[] Palette =
    {
        "#000000", "#4D4D4D", "#999999", "#C8C8C8", "#FFFFFF", "#8B572A",
        "#E81123", "#F7630C", "#FFB900", "#FCE100", "#BAD80A", "#16C60C",
        "#00B294", "#00BCF2", "#0078D7", "#0037DA", "#5C2D91", "#B4009E",
        "#E3008C", "#FF8FB1", "#F4C7A1", "#C19A6B", "#2D7D46", "#1B3A5C",
    };

    private static readonly string[] BrushNames = { "마커", "캘리그래피 펜", "유화 브러시", "수채화", "픽셀 펜", "연필", "지우개", "크레용", "스프레이 캔" };
    private static readonly string[] ShapeNames = { "선", "사각형", "원", "삼각형", "직각 삼각형", "오각형", "육각형", "별", "하트", "화살표" };
    private static readonly string[] ModelNames = { "구", "정육면체", "원기둥", "원뿔" };
    private static readonly string[] EffectNames = { "흑백", "세피아", "따뜻하게", "차갑게", "반전", "밝게", "어둡게", "대비 강하게", "비네트" };

    private readonly Engine _engine = new(1920, 1080);
    private readonly ToolState S = new();
    private readonly PaintSurface _surface;
    private readonly ContentControl _panel = new();
    private readonly Dictionary<Tool, Border> _tabs = new();
    private readonly List<Action> _colorWatchers = new();
    private readonly Button _undo, _redo;
    private readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, Width = 48, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _sizeText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Muted };
    private readonly Slider _zoomSlider = new() { Minimum = 5, Maximum = 800, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private bool _syncingZoom, _dirty, _forceClose;
    private string? _path;

    private static List<string>? _fonts;
    private static Avalonia.Media.Imaging.WriteableBitmap[]? _brushThumbs, _shapeThumbs, _modelThumbs, _effectThumbs;

    public MainWindow(string[] args)
    {
        Width = 1360;
        Height = 860;
        MinWidth = 960;
        MinHeight = 600;
        Background = Brushes.White;
        Icon = LoadIcon();

        _fonts ??= FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n).ToList();
        S.Font = _fonts.FirstOrDefault(f => f == "Noto Sans CJK KR") ?? _fonts.FirstOrDefault(f => f.Contains("Nanum")) ?? "";

        _surface = new PaintSurface(_engine, S);
        _surface.Changed += () => { _dirty = true; UpdateState(); };
        _surface.ZoomChanged += UpdateZoom;
        _surface.ColorPicked += c => { S.Picking = false; SetColor(c); };
        _surface.SelectionChanged += () => { if (S.Tool == Tool.Select) ShowPanel(); };

        _undo = TopAction("실행 취소", Icons.Undo, Undo);
        _redo = TopAction("다시 실행", Icons.Redo, Redo);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(BuildTopBar());
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetRow(body, 1);
        body.Children.Add(BuildCanvasArea());
        var side = new Border
        {
            Width = 300,
            Background = Brushes.White,
            BorderBrush = Line,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = _panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        root.Children.Add(body);
        Content = root;

        KeyDown += OnKey;
        Closing += OnClosing;
        SelectTab(Tool.Brush);
        UpdateState();
        UpdateZoom();

        if (args.Length > 0 && File.Exists(args[0]))
            Opened += (_, _) => Dispatcher.UIThread.Post(() => OpenPath(args[0]), DispatcherPriority.Background);
    }

    private static WindowIcon? LoadIcon()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "openpaint3d.png");
        return File.Exists(p) ? new WindowIcon(p) : null;
    }

    // ----------------------------------------------------------------- layout

    private Control BuildTopBar()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 72 };

        var menuBtn = TopAction("메뉴", Icons.Menu, () => { });
        var menu = new MenuFlyout();
        void Item(string header, KeyGesture? g, Action act)
        {
            var mi = new MenuItem { Header = header, InputGesture = g };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        Item("새로 만들기", new KeyGesture(Key.N, KeyModifiers.Control), () => _ = NewCanvas());
        Item("열기...", new KeyGesture(Key.O, KeyModifiers.Control), () => _ = OpenFile());
        Item("저장", new KeyGesture(Key.S, KeyModifiers.Control), () => _ = Save(false));
        Item("다른 이름으로 저장...", new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), () => _ = Save(true));
        menu.Items.Add(new Separator());
        Item("정보", null, () => _ = Ask("OpenPaint 3D 정보",
            "OpenPaint 3D 0.1.1\n\nPaint 3D의 화면 구성을 참고해 C#(Avalonia)과 Rust로 새로 만든 오픈소스 그림판입니다.\nMicrosoft의 코드나 리소스는 포함되어 있지 않으며 Microsoft와 관련이 없습니다.", "확인"));
        Item("끝내기", null, Close);
        menuBtn.Flyout = menu;
        bar.Children.Add(new Border { Child = menuBtn, Margin = new Thickness(8, 0) });

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        void Tab(Tool t, string label, string icon)
        {
            var btn = TopAction(label, icon, () => SelectTab(t));
            var holder = new Border { Child = btn, BorderThickness = new Thickness(0, 0, 0, 3), BorderBrush = Brushes.Transparent };
            _tabs[t] = holder;
            tabs.Children.Add(holder);
        }
        Tab(Tool.Brush, "브러시", Icons.Brushes);
        Tab(Tool.Shape2D, "2D 셰이프", Icons.Shapes2D);
        Tab(Tool.Shape3D, "3D 셰이프", Icons.Shapes3D);
        Tab(Tool.Sticker, "스티커", Icons.Stickers);
        Tab(Tool.Text, "텍스트", Icons.Text);
        Tab(Tool.Effects, "효과", Icons.Effects);
        Tab(Tool.Canvas, "캔버스", Icons.Canvas);
        Tab(Tool.Select, "매직 선택", Icons.Select);
        Grid.SetColumn(tabs, 1);
        bar.Children.Add(tabs);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0), Children = { _undo, _redo } };
        Grid.SetColumn(right, 2);
        bar.Children.Add(right);

        return new Border { Child = bar, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.White };
    }

    private static Button TopAction(string label, string icon, Action click)
    {
        var b = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Icons.Make(icon, 24),
                    new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Dark },
                },
            },
        };
        b.Classes.Add("top");
        b.Click += (_, _) => click();
        return b;
    }

    private Control BuildCanvasArea()
    {
        var area = new Grid { Background = new SolidColorBrush(Color.Parse("#E9E9E9")) };
        area.Children.Add(_surface);

        _zoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_syncingZoom) _surface.SetZoom(_zoomSlider.Value / 100);
        };
        Button Small(string text, Action a)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) => a();
            return b;
        }
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 0) };
        bar.Children.Add(_sizeText);
        var zoom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                Small("화면에 맞추기", _surface.FitToView),
                Small("−", () => _surface.SetZoom(_surface.Zoom / 1.25)),
                _zoomSlider,
                Small("+", () => _surface.SetZoom(_surface.Zoom * 1.25)),
                _zoomText,
            },
        };
        Grid.SetColumn(zoom, 1);
        bar.Children.Add(zoom);
        area.Children.Add(new Border
        {
            Height = 38,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        });
        return area;
    }

    // ------------------------------------------------------------------- tabs

    private void SelectTab(Tool t)
    {
        S.Tool = t;
        S.Picking = false;
        if (t != Tool.Select) _surface.ClearSelection();
        foreach (var (tool, holder) in _tabs)
        {
            holder.BorderBrush = tool == t ? Accent : Brushes.Transparent;
            holder.Background = tool == t ? AccentLight : Brushes.Transparent;
        }
        ShowPanel();
    }

    private void ShowPanel()
    {
        _colorWatchers.Clear();
        var p = new StackPanel { Margin = new Thickness(16, 12, 16, 24), Spacing = 6 };
        switch (S.Tool)
        {
            case Tool.Brush: BrushPanel(p); break;
            case Tool.Shape2D: ShapePanel(p); break;
            case Tool.Shape3D: ModelPanel(p); break;
            case Tool.Sticker: StickerPanel(p); break;
            case Tool.Text: TextPanel(p); break;
            case Tool.Effects: EffectPanel(p); break;
            case Tool.Canvas: CanvasPanel(p); break;
            case Tool.Select: SelectPanel(p); break;
        }
        _panel.Content = p;
    }

    private static TextBlock Header(string t) => new() { Text = t, FontSize = 22, FontWeight = FontWeight.Light, Margin = new Thickness(0, 0, 0, 8) };
    private static TextBlock Section(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 2) };
    private static TextBlock Hint(string t) => new() { Text = t, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    private Control Tile(Control content, string tip, bool selected, Action click, double w = 76, double h = 64)
    {
        var b = new Border
        {
            Width = w,
            Height = h,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(4),
            Background = selected ? AccentLight : TileBg,
            BorderBrush = selected ? Accent : Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = content,
            ClipToBounds = true,
        };
        ToolTip.SetTip(b, tip);
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            click();
            ShowPanel();
        };
        return b;
    }

    private static Image Img(IImage src) => new() { Source = src, Stretch = Stretch.None };

    private static Control Labeled(Control visual, string label) => new StackPanel
    {
        VerticalAlignment = VerticalAlignment.Center,
        Children = { visual, new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Muted } },
    };

    private static Control SliderRow(string label, double min, double max, double value, Action<double> set, Func<double, string> fmt)
    {
        var val = new TextBlock { Text = fmt(value), HorizontalAlignment = HorizontalAlignment.Right, Foreground = Muted };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        head.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 13 });
        Grid.SetColumn(val, 1);
        head.Children.Add(val);
        var s = new Slider { Minimum = min, Maximum = max, Value = value };
        s.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            set(s.Value);
            val.Text = fmt(s.Value);
        };
        return new StackPanel { Children = { head, s } };
    }

    private void SetColor(Color c)
    {
        S.Color = Color.FromRgb(c.R, c.G, c.B);
        foreach (var w in _colorWatchers) w();
    }

    private Control ColorSection(string title = "색")
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(Section(title));

        var swatch = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), BorderBrush = Line, BorderThickness = new Thickness(1) };
        var hex = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), FontFamily = new FontFamily("monospace") };
        var pick = new ToggleButton { Content = Icons.Make(Icons.Picker, 20), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(pick, "스포이드: 캔버스에서 색 가져오기");
        pick.IsCheckedChanged += (_, _) => S.Picking = pick.IsChecked == true;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { swatch, hex, pick } };
        Grid.SetColumn(hex, 1);
        Grid.SetColumn(pick, 2);
        sp.Children.Add(row);
        void Sync()
        {
            swatch.Background = new SolidColorBrush(S.Color);
            hex.Text = $"#{S.Color.R:X2}{S.Color.G:X2}{S.Color.B:X2}";
            pick.IsChecked = S.Picking;
        }
        _colorWatchers.Add(Sync);
        Sync();

        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var h in Palette)
        {
            var c = Color.Parse(h);
            var cell = new Border
            {
                Height = 36,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(c),
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(cell, h);
            cell.PointerPressed += (_, _) => SetColor(c);
            grid.Children.Add(cell);
        }
        sp.Children.Add(grid);

        var view = new ColorView { Color = S.Color, IsAlphaEnabled = false, IsAlphaVisible = false, Width = 320 };
        view.ColorChanged += (_, e) => SetColor(e.NewColor);
        sp.Children.Add(new Button
        {
            Content = "+ 사용자 지정 색",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Flyout = new Flyout { Content = view, Placement = PlacementMode.Left },
        });
        return sp;
    }

    // ----------------------------------------------------------------- panels

    private static void EnsureThumbs()
    {
        var ink = Color.Parse("#2B2B2B");
        _brushThumbs ??= Enumerable.Range(0, BrushNames.Length).Select(i => Engine.Thumbnail(70, 34, e =>
        {
            if (i == PaintTools.Eraser) e.Shape(1, new Point(0, 8), new Point(70, 26), ink, 0, Color.Parse("#7A7A7A"));
            double size = i switch { PaintTools.Pixel => 3, PaintTools.Pencil => 3, PaintTools.Spray => 14, PaintTools.Eraser => 10, _ => 8 };
            e.StrokeBegin(i, ink, size, 1, new Point(8, 22));
            for (int x = 8; x <= 62; x += 2) e.StrokeTo(new Point(x, 17 + Math.Sin((x - 8) / 54.0 * Math.PI * 2) * 8));
            e.StrokeEnd();
        })).ToArray();
        _shapeThumbs ??= Enumerable.Range(0, ShapeNames.Length).Select(k => Engine.Thumbnail(46, 40, e =>
        {
            if (k == 0) e.Shape(0, new Point(6, 34), new Point(40, 6), ink, 3, null);
            else e.Shape(k, new Point(6, 5), new Point(40, 35), ink, 2.5, null);
        })).ToArray();
        _modelThumbs ??= Enumerable.Range(0, ModelNames.Length).Select(k => Engine.Thumbnail(58, 58, e =>
            e.Model(k, new Point(6, 4), new Point(52, 54), Color.Parse("#4F8EF7")))).ToArray();
        _effectThumbs ??= Enumerable.Range(0, EffectNames.Length).Select(k => Engine.Thumbnail(76, 44, e =>
        {
            e.Shape(1, new Point(0, 0), new Point(76, 44), ink, 0, Color.Parse("#BFE3FF"));
            e.Shape(1, new Point(0, 32), new Point(76, 44), ink, 0, Color.Parse("#8BC34A"));
            e.Model(0, new Point(8, 6), new Point(36, 34), Color.Parse("#FF8A3D"));
            e.Model(1, new Point(42, 8), new Point(68, 40), Color.Parse("#3D7BFF"));
            e.Effect(k);
        })).ToArray();
    }

    private void BrushPanel(StackPanel p)
    {
        EnsureThumbs();
        p.Children.Add(Header("브러시"));
        var grid = new WrapPanel();
        for (int i = 0; i < BrushNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(Labeled(Img(_brushThumbs![k]), BrushNames[k]), BrushNames[k], !S.Bucket && S.Brush == k,
                () => { S.Brush = k; S.Bucket = false; S.Picking = false; }, 82, 64));
        }
        grid.Children.Add(Tile(Labeled(Icons.Make(Icons.Bucket, 30), "채우기"), "채우기", S.Bucket,
            () => { S.Bucket = true; S.Picking = false; }, 82, 64));
        p.Children.Add(grid);

        if (S.Bucket)
        {
            p.Children.Add(SliderRow("허용 오차", 0, 255, S.Tolerance, v => S.Tolerance = (int)v, v => $"{(int)v}"));
        }
        else
        {
            p.Children.Add(SliderRow("두께", 1, 200, S.BrushSize, v => S.BrushSize = v, v => $"{(int)v}px"));
            if (S.Brush != PaintTools.Eraser)
                p.Children.Add(SliderRow("불투명도", 1, 100, S.Opacity * 100, v => S.Opacity = v / 100, v => $"{(int)v}%"));
        }
        p.Children.Add(ColorSection());
    }

    private void ShapePanel(StackPanel p)
    {
        EnsureThumbs();
        p.Children.Add(Header("2D 셰이프"));
        var grid = new WrapPanel();
        for (int i = 0; i < ShapeNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(Img(_shapeThumbs![k]), ShapeNames[k], S.Shape == k, () => S.Shape = k, 52, 46));
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("캔버스에서 드래그해 그립니다. Shift를 누르면 정비율(선은 45° 단위)로 그려집니다."));
        p.Children.Add(SliderRow("선 두께", 0, 60, S.ShapeThickness, v => S.ShapeThickness = v, v => $"{(int)v}px"));

        p.Children.Add(Section("채우기"));
        var fillSwatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(S.FillColor), BorderBrush = Line, BorderThickness = new Thickness(1) };
        var fillView = new ColorView { Color = S.FillColor, IsAlphaEnabled = false, IsAlphaVisible = false, Width = 320 };
        fillView.ColorChanged += (_, e) =>
        {
            S.FillColor = Color.FromRgb(e.NewColor.R, e.NewColor.G, e.NewColor.B);
            fillSwatch.Background = new SolidColorBrush(S.FillColor);
        };
        var check = new CheckBox { Content = "도형 안쪽 채우기", IsChecked = S.ShapeFill };
        check.IsCheckedChanged += (_, _) => S.ShapeFill = check.IsChecked == true;
        p.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                check,
                new Button
                {
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { fillSwatch, new TextBlock { Text = "채우기 색" } } },
                    Flyout = new Flyout { Content = fillView, Placement = PlacementMode.Left },
                },
            },
        });
        p.Children.Add(ColorSection("선 색"));
    }

    private void ModelPanel(StackPanel p)
    {
        EnsureThumbs();
        p.Children.Add(Header("3D 셰이프"));
        p.Children.Add(Section("3D 개체"));
        var grid = new WrapPanel();
        for (int i = 0; i < ModelNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(Labeled(Img(_modelThumbs![k]), ModelNames[k]), ModelNames[k], S.Model == k, () => S.Model = k, 82, 84));
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("캔버스에서 드래그해 입체 도형을 배치합니다. 조명과 음영은 자동으로 입혀지며, Shift를 누르면 정비율로 그려집니다."));
        p.Children.Add(ColorSection("재질 색"));
    }

    private void StickerPanel(StackPanel p)
    {
        p.Children.Add(Header("스티커"));
        var grid = new WrapPanel();
        for (int i = 0; i < Stickers.Names.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(new StickerView { Kind = k }, Stickers.Names[k], S.Sticker == k, () => S.Sticker = k, 76, 76));
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("캔버스에서 드래그한 크기대로 스티커를 붙입니다. Shift를 누르면 정비율로 붙습니다."));
    }

    private void TextPanel(StackPanel p)
    {
        p.Children.Add(Header("텍스트"));
        var box = new TextBox { Text = S.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90 };
        box.TextChanged += (_, _) => S.Text = box.Text ?? "";
        p.Children.Add(box);
        p.Children.Add(Hint("캔버스를 클릭하면 그 위치에 텍스트를 넣습니다."));

        p.Children.Add(Section("글꼴"));
        var fonts = new ComboBox { ItemsSource = _fonts, SelectedItem = S.Font, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 400 };
        fonts.SelectionChanged += (_, _) => S.Font = fonts.SelectedItem as string ?? "";
        p.Children.Add(fonts);

        var bold = new ToggleButton { Content = new TextBlock { Text = "가", FontWeight = FontWeight.Bold }, IsChecked = S.Bold, Width = 40 };
        bold.IsCheckedChanged += (_, _) => S.Bold = bold.IsChecked == true;
        var italic = new ToggleButton { Content = new TextBlock { Text = "가", FontStyle = FontStyle.Italic }, IsChecked = S.Italic, Width = 40 };
        italic.IsCheckedChanged += (_, _) => S.Italic = italic.IsChecked == true;
        ToolTip.SetTip(bold, "굵게");
        ToolTip.SetTip(italic, "기울임꼴");
        p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { bold, italic } });
        p.Children.Add(SliderRow("크기", 8, 300, S.FontSize, v => S.FontSize = v, v => $"{(int)v}pt"));
        p.Children.Add(ColorSection("글자 색"));
    }

    private void EffectPanel(StackPanel p)
    {
        EnsureThumbs();
        p.Children.Add(Header("효과"));
        p.Children.Add(Section("필터"));
        var grid = new WrapPanel();
        for (int i = 0; i < EffectNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(Labeled(Img(_effectThumbs![k]), EffectNames[k]), EffectNames[k], false, () =>
            {
                _engine.Effect(k);
                _surface.Refresh();
            }, 82, 66));
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("효과는 캔버스 전체에 적용됩니다. 실행 취소(Ctrl+Z)로 되돌릴 수 있습니다."));
    }

    private void CanvasPanel(StackPanel p)
    {
        p.Children.Add(Header("캔버스"));
        p.Children.Add(Section("캔버스 크기 (픽셀)"));
        var wBox = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = _engine.Width, Increment = 1, FormatString = "0" };
        var hBox = new NumericUpDown { Minimum = 1, Maximum = 10000, Value = _engine.Height, Increment = 1, FormatString = "0" };
        var lockRatio = new CheckBox { Content = "가로 세로 비율 고정", IsChecked = true };
        var scale = new CheckBox { Content = "캔버스와 함께 이미지 크기 조정", IsChecked = false };
        double ratio = (double)_engine.Width / _engine.Height;
        bool syncing = false;
        wBox.ValueChanged += (_, _) =>
        {
            if (syncing || lockRatio.IsChecked != true || wBox.Value is not { } v) return;
            syncing = true;
            hBox.Value = Math.Max(1, Math.Round(v / (decimal)ratio));
            syncing = false;
        };
        hBox.ValueChanged += (_, _) =>
        {
            if (syncing || lockRatio.IsChecked != true || hBox.Value is not { } v) return;
            syncing = true;
            wBox.Value = Math.Max(1, Math.Round(v * (decimal)ratio));
            syncing = false;
        };
        var sizes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        sizes.Children.Add(new StackPanel { Children = { Hint("너비"), wBox } });
        var hp = new StackPanel { Children = { Hint("높이"), hBox } };
        Grid.SetColumn(hp, 2);
        sizes.Children.Add(hp);
        p.Children.Add(sizes);
        p.Children.Add(lockRatio);
        p.Children.Add(scale);
        var apply = new Button { Content = "크기 적용", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        apply.Click += (_, _) =>
        {
            int w = (int)(wBox.Value ?? _engine.Width), h = (int)(hBox.Value ?? _engine.Height);
            if (w == _engine.Width && h == _engine.Height) return;
            _engine.Resize(w, h, scale.IsChecked == true);
            _surface.Refresh();
            _surface.FitToView();
            ShowPanel();
        };
        p.Children.Add(apply);

        p.Children.Add(Section("회전 및 대칭"));
        var g = new UniformGrid { Columns = 2 };
        void Op(string text, Action a)
        {
            var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(2) };
            b.Click += (_, _) => { a(); _surface.Refresh(); _surface.FitToView(); ShowPanel(); };
            g.Children.Add(b);
        }
        Op("왼쪽으로 회전", () => _engine.Rotate(false));
        Op("오른쪽으로 회전", () => _engine.Rotate(true));
        Op("좌우 대칭", () => _engine.Flip(true));
        Op("상하 대칭", () => _engine.Flip(false));
        p.Children.Add(g);
    }

    private void SelectPanel(StackPanel p)
    {
        p.Children.Add(Header("매직 선택"));
        p.Children.Add(Hint("캔버스에서 드래그해 남길 영역을 선택한 다음 '선택 영역으로 자르기'를 누르세요.\n(이 버전은 사각형 선택과 자르기를 지원합니다.)"));
        var sel = _surface.Selection;
        p.Children.Add(Section("선택 영역"));
        p.Children.Add(new TextBlock { Text = sel is { } r ? $"{(int)r.X}, {(int)r.Y}  ·  {(int)r.Width} × {(int)r.Height} px" : "없음", Foreground = Muted });
        var crop = new Button { Content = "선택 영역으로 자르기", IsEnabled = sel != null, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };
        crop.Click += (_, _) =>
        {
            if (_surface.Selection is not { } r) return;
            _engine.Crop(new PixelRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
            _surface.ClearSelection();
            _surface.Refresh();
            _surface.FitToView();
        };
        var clear = new Button { Content = "선택 해제", IsEnabled = sel != null, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        clear.Click += (_, _) => _surface.ClearSelection();
        p.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children = { crop, clear } });
    }

    // ----------------------------------------------------------------- state

    private void UpdateState()
    {
        _undo.IsEnabled = _engine.CanUndo;
        _redo.IsEnabled = _engine.CanRedo;
        var name = _path != null ? Path.GetFileName(_path) : "제목 없음";
        Title = $"{(_dirty ? "*" : "")}{name} - OpenPaint 3D";
        _sizeText.Text = $"{_engine.Width} × {_engine.Height} px";
    }

    private void UpdateZoom()
    {
        _syncingZoom = true;
        _zoomSlider.Value = _surface.Zoom * 100;
        _syncingZoom = false;
        _zoomText.Text = $"{Math.Round(_surface.Zoom * 100)}%";
    }

    private void Undo()
    {
        if (_engine.Undo()) _surface.Refresh();
        if (S.Tool == Tool.Canvas) ShowPanel();
    }

    private void Redo()
    {
        if (_engine.Redo()) _surface.Refresh();
        if (S.Tool == Tool.Canvas) ShowPanel();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Source is TextBox && e.Key is Key.Z or Key.Y) return;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift: Redo(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.S when ctrl: _ = Save(shift); break;
            case Key.O when ctrl: _ = OpenFile(); break;
            case Key.N when ctrl: _ = NewCanvas(); break;
            case Key.D0 when ctrl: _surface.FitToView(); break;
            case Key.OemPlus when ctrl: _surface.SetZoom(_surface.Zoom * 1.25); break;
            case Key.OemMinus when ctrl: _surface.SetZoom(_surface.Zoom / 1.25); break;
            case Key.Escape:
                S.Picking = false;
                _surface.ClearSelection();
                foreach (var w in _colorWatchers) w();
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ files

    private static readonly FilePickerFileType[] SaveTypes =
    {
        new("PNG 이미지") { Patterns = new[] { "*.png" } },
        new("JPEG 이미지") { Patterns = new[] { "*.jpg", "*.jpeg" } },
        new("WebP 이미지") { Patterns = new[] { "*.webp" } },
    };

    private async Task<bool> ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = await Ask("OpenPaint 3D", "변경 내용을 저장할까요?", "저장", "저장 안 함", "취소");
        return r switch { 0 => await Save(false), 1 => true, _ => false };
    }

    private async Task NewCanvas()
    {
        if (!await ConfirmDiscard()) return;
        _engine.Reset(1920, 1080);
        _path = null;
        _surface.Refresh(false);
        _surface.FitToView();
        _dirty = false;
        UpdateState();
    }

    private async Task OpenFile()
    {
        if (!await ConfirmDiscard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "열기",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("이미지") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) OpenPath(p);
    }

    private void OpenPath(string p)
    {
        try
        {
            _engine.Load(p);
            _path = p;
            _surface.Refresh(false);
            _surface.FitToView();
            _dirty = false;
            UpdateState();
        }
        catch (Exception ex)
        {
            _ = Ask("열 수 없음", $"{Path.GetFileName(p)}\n{ex.Message}", "확인");
        }
    }

    private async Task<bool> Save(bool saveAs)
    {
        var ext = _path == null ? "" : Path.GetExtension(_path).ToLowerInvariant();
        if (saveAs || _path == null || ext is not (".png" or ".jpg" or ".jpeg" or ".webp"))
        {
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "다른 이름으로 저장",
                SuggestedFileName = _path != null ? Path.GetFileNameWithoutExtension(_path) + ".png" : "그림.png",
                DefaultExtension = "png",
                FileTypeChoices = SaveTypes,
                ShowOverwritePrompt = true,
            });
            if (f?.TryGetLocalPath() is not { } p) return false;
            _path = p;
        }
        try
        {
            _engine.Save(_path);
            _dirty = false;
            UpdateState();
            return true;
        }
        catch (Exception ex)
        {
            await Ask("저장할 수 없음", ex.Message, "확인");
            return false;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || !_dirty) return;
        e.Cancel = true;
        if (await ConfirmDiscard())
        {
            _forceClose = true;
            Close();
        }
    }

    private async Task<int> Ask(string title, string message, params string[] buttons)
    {
        int result = -1;
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++)
        {
            int k = i;
            var b = new Button { Content = buttons[i], MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0) b.Classes.Add("accent");
            b.Click += (_, _) => { result = k; dlg.Close(); };
            row.Children.Add(b);
        }
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, row },
        };
        await dlg.ShowDialog(this);
        return result;
    }
}

/// <summary>Brush ids shared with the Rust engine.</summary>
public static class PaintTools
{
    public const int Pixel = 4, Pencil = 5, Eraser = 6, Spray = 8;
}
