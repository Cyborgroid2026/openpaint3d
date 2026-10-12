using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#FAFAFA"));

    private static readonly string[] Palette =
    {
        "#000000", "#4D4D4D", "#999999", "#C8C8C8", "#FFFFFF", "#8B572A",
        "#E81123", "#F7630C", "#FFB900", "#FCE100", "#BAD80A", "#16C60C",
        "#00B294", "#00BCF2", "#0078D7", "#0037DA", "#5C2D91", "#B4009E",
        "#E3008C", "#FF8FB1", "#F4C7A1", "#C19A6B", "#2D7D46", "#1B3A5C",
    };

    private static readonly string[] BrushNames = { "마커", "캘리그래피 펜", "유화 브러시", "수채화", "픽셀 펜", "연필", "지우개", "크레용", "스프레이 캔" };
    private static readonly string[] Finishes = { "무광", "유광", "무광 금속", "광택 금속" };

    private sealed record Preset(string Name, string Light, float Brightness, float Ambient, float Angle, uint Filter, float Tint);

    private static readonly Preset[] Presets =
    {
        new("없음", "#FFFFFF", 1f, 0.35f, 0, 0, 0),
        new("해돋이", "#FFB27A", 1.05f, 0.35f, -60, 3, 0.45f),
        new("한낮", "#FFFFFF", 1.3f, 0.45f, 0, 0, 0),
        new("노을", "#FF7A9A", 0.95f, 0.3f, 60, 0, 0.5f),
        new("밤", "#6E8BFF", 0.6f, 0.2f, 0, 4, 0.6f),
        new("북극광", "#7FFFD4", 0.9f, 0.3f, 200, 0, 0.45f),
        new("흑백", "#FFFFFF", 1f, 0.35f, 0, 1, 0),
        new("세피아", "#FFFFFF", 1f, 0.35f, 0, 2, 0),
        new("빈티지", "#FFE0B0", 1f, 0.35f, 0, 8, 0.2f),
        new("느와르", "#FFFFFF", 1.1f, 0.25f, 30, 9, 0),
        new("대비 강하게", "#FFFFFF", 1.1f, 0.3f, 0, 6, 0),
        new("비네트", "#FFFFFF", 1f, 0.35f, 0, 7, 0),
    };

    private readonly Engine _engine = new(1920, 1080);
    private readonly ToolState S = new();
    private readonly PaintSurface _surface;
    private readonly ContentControl _panel = new();
    private readonly Dictionary<Tool, Border> _tabs = new();
    private readonly List<Action> _colorWatchers = new();
    private readonly Button _undo, _redo, _history, _paste;
    private readonly ToggleButton _view3d = new();
    private readonly Button _reset3d = new();
    private readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, Width = 48, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _sizeText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Muted };
    private readonly Slider _zoomSlider = new() { Minimum = 5, Maximum = 800, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _menu = new() { IsVisible = false };
    private readonly ContentControl _menuContent = new();
    private readonly Dictionary<string, Border> _menuItems = new();
    private bool _syncingZoom, _dirty, _forceClose;
    private string? _path;
    private int _stickerTab;
    private string? _libraryCategory;
    private string _libraryQuery = "";

    private static List<string>? _fonts;
    private static readonly Dictionary<string, IImage> ThumbCache = new();

    private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openpaint3d");
    private static string StickerDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openpaint3d", "stickers");

    public MainWindow(string[] args)
    {
        Width = 1400;
        Height = 880;
        MinWidth = 1000;
        MinHeight = 620;
        Background = Brushes.White;
        Icon = LoadIcon();

        _fonts ??= FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n).ToList();
        S.Font = _fonts.FirstOrDefault(f => f == "Noto Sans CJK KR") ?? _fonts.FirstOrDefault(f => f.Contains("Nanum")) ?? "";

        _surface = new PaintSurface(_engine, S);
        _surface.Changed += () => { _dirty = true; UpdateState(); };
        _surface.ZoomChanged += UpdateZoom;
        _surface.ColorPicked += c => { S.Picking = false; SetColor(c); };
        _surface.SelectionChanged += () => Dispatcher.UIThread.Post(ShowPanel);

        _paste = TopAction("붙여넣기", Icons.Paste, () => _ = Paste());
        _undo = TopAction("실행 취소", Icons.Undo, Undo);
        _history = TopAction("기록", Icons.History, () => { });
        _redo = TopAction("다시 실행", Icons.Redo, Redo);
        var historyFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        historyFlyout.Opening += (_, _) => historyFlyout.Content = BuildHistory(historyFlyout);
        _history.Flyout = historyFlyout;

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        main.Children.Add(BuildTopBar());
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetRow(body, 1);
        body.Children.Add(BuildCanvasArea());
        var side = new Border
        {
            Width = 316,
            Background = PanelBg,
            BorderBrush = Line,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = _panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        main.Children.Add(body);

        BuildMenu();
        Content = new Grid { Children = { main, _menu } };

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
        bar.Children.Add(new Border { Child = TopAction("메뉴", Icons.Menu, () => ShowMenu("열기")), Margin = new Thickness(8, 0) });

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
        Tab(Tool.Library, "3D 라이브러리", Icons.Library);
        Grid.SetColumn(tabs, 1);
        bar.Children.Add(tabs);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0), Children = { _paste, _undo, _history, _redo } };
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
            Padding = new Thickness(11, 6),
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
        var area = new Grid { Background = new SolidColorBrush(Color.Parse("#E6E6E6")) };
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
        _view3d.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { Icons.Make(Icons.View3D, 18), new TextBlock { Text = "3D 보기", VerticalAlignment = VerticalAlignment.Center } },
        };
        _view3d.Padding = new Thickness(10, 3);
        _view3d.IsCheckedChanged += (_, _) =>
        {
            _surface.SetView3D(_view3d.IsChecked == true);
            _reset3d.IsVisible = _view3d.IsChecked == true;
            ShowPanel();
        };
        ToolTip.SetTip(_view3d, "장면 전체를 입체로 돌려 봅니다. 드래그: 회전, 휠: 확대/축소");
        _reset3d.Content = "보기 재설정";
        _reset3d.IsVisible = false;
        _reset3d.Click += (_, _) => _surface.Reset3DView();

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(10, 0) };
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { _view3d, _reset3d } });
        _sizeText.Margin = new Thickness(16, 0);
        Grid.SetColumn(_sizeText, 1);
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
        Grid.SetColumn(zoom, 3);
        bar.Children.Add(zoom);
        area.Children.Add(new Border
        {
            Height = 42,
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
        _surface.CommitPending();
        if (S.CropMode) _surface.EndCrop(false);
        if (S.View3D) _view3d.IsChecked = false;
        if (t is not (Tool.Shape3D or Tool.Library or Tool.Effects)) _surface.Select(0);
        S.Tool = t;
        S.Picking = false;
        foreach (var (tool, holder) in _tabs)
        {
            holder.BorderBrush = tool == t ? Accent : Brushes.Transparent;
            holder.Background = tool == t ? AccentLight : Brushes.Transparent;
        }
        _surface.Redraw();
        ShowPanel();
    }

    private void ShowPanel()
    {
        _colorWatchers.Clear();
        var p = new StackPanel { Margin = new Thickness(16, 12, 16, 24), Spacing = 6 };
        if (S.View3D)
        {
            p.Children.Add(Header("3D 보기"));
            p.Children.Add(Hint("캔버스와 3D 개체를 입체로 둘러봅니다.\n\n• 드래그: 장면 회전\n• 마우스 휠: 확대/축소\n• '보기 재설정': 처음 각도로\n\n편집하려면 아래쪽 '3D 보기'를 끄세요."));
            _panel.Content = p;
            return;
        }
        switch (S.Tool)
        {
            case Tool.Brush: BrushPanel(p); break;
            case Tool.Shape2D: ShapePanel(p); break;
            case Tool.Shape3D: ModelPanel(p); break;
            case Tool.Sticker: StickerPanel(p); break;
            case Tool.Text: TextPanel(p); break;
            case Tool.Effects: EffectPanel(p); break;
            case Tool.Canvas: CanvasPanel(p); break;
            case Tool.Library: LibraryPanel(p); break;
        }
        _panel.Content = p;
    }

    private static TextBlock Header(string t) => new() { Text = t, FontSize = 22, FontWeight = FontWeight.Light, Margin = new Thickness(0, 0, 0, 6) };
    private static TextBlock Section(string t) => new() { Text = t, FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 2) };
    private static TextBlock Hint(string t) => new() { Text = t, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    private Control Tile(Control content, string tip, bool selected, Action click, double w = 82, double h = 64)
    {
        var b = new Border
        {
            Width = w,
            Height = h,
            Margin = new Thickness(2),
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
        Children = { visual, new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis } },
    };

    private static Control SliderRow(string label, double min, double max, double value, Action<double> set, Func<double, string> fmt, Action? done = null)
    {
        var val = new TextBlock { Text = fmt(value), HorizontalAlignment = HorizontalAlignment.Right, Foreground = Muted };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
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
        if (done != null) s.AddHandler(PointerReleasedEvent, (_, _) => done(), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        return new StackPanel { Children = { head, s } };
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> set)
    {
        var c = new ComboBox { ItemsSource = items, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(c.SelectedIndex); };
        return c;
    }

    private static Button Action(string text, Action click, string? icon = null, bool enabled = true)
    {
        object content = icon == null
            ? text
            : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icons.Make(icon, 16), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
        var b = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(2), IsEnabled = enabled };
        b.Click += (_, _) => click();
        return b;
    }

    private static UniformGrid Grid2(params Control[] items)
    {
        var g = new UniformGrid { Columns = 2 };
        foreach (var i in items) g.Children.Add(i);
        return g;
    }

    // ------------------------------------------------------------ colour UI

    private void SetColor(Color c, bool final = true)
    {
        S.Color = Color.FromRgb(c.R, c.G, c.B);
        foreach (var w in _colorWatchers) w();
        _surface.SyncPending();
        if (_surface.Selected != 0)
        {
            if (final) _engine.Checkpoint(Label.Material);
            _engine.SetMaterial(_surface.Selected, S.Color, _engine.Info(_surface.Selected)?.Finish ?? S.Finish);
            _surface.Refresh(final, !final);
        }
    }

    private void SetFinish(int f)
    {
        S.Finish = f;
        _surface.SyncPending();
        if (_surface.Selected != 0 && _engine.Info(_surface.Selected) is { } info)
        {
            _engine.Checkpoint(Label.Material);
            _engine.SetMaterial(_surface.Selected, info.Color, f);
            _surface.Refresh();
        }
    }

    private Control FinishRow(string label = "재질")
    {
        var sp = new StackPanel();
        sp.Children.Add(Section(label));
        sp.Children.Add(Combo(Finishes, S.Finish, SetFinish));
        return sp;
    }

    private Control ColorSection(string title = "색")
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(Section(title));

        var swatch = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), BorderBrush = Line, BorderThickness = new Thickness(1) };
        var pick = new ToggleButton { Content = Icons.Make(Icons.Picker, 20), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(pick, "스포이드: 캔버스에서 색 가져오기");
        pick.IsCheckedChanged += (_, _) => S.Picking = pick.IsChecked == true;

        var hex = new TextBox { Width = 92, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("monospace") };
        NumericUpDown Channel() => new() { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 76, ShowButtonSpinner = false };
        var (r, g, b) = (Channel(), Channel(), Channel());
        bool syncing = false;
        void ApplyHex()
        {
            if (syncing) return;
            var t = (hex.Text ?? "").Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (t.Length == 7 && Color.TryParse(t, out var c)) SetColor(c);
        }
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        hex.LostFocus += (_, _) => ApplyHex();
        void ApplyRgb()
        {
            if (syncing) return;
            SetColor(Color.FromRgb((byte)(r.Value ?? 0), (byte)(g.Value ?? 0), (byte)(b.Value ?? 0)));
        }
        r.ValueChanged += (_, _) => ApplyRgb();
        g.ValueChanged += (_, _) => ApplyRgb();
        b.ValueChanged += (_, _) => ApplyRgb();

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { swatch } };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "HEX", VerticalAlignment = VerticalAlignment.Center, Foreground = Muted }, hex } };
        Grid.SetColumn(hexRow, 1);
        Grid.SetColumn(pick, 2);
        top.Children.Add(hexRow);
        top.Children.Add(pick);
        sp.Children.Add(top);
        var rgb = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (lbl, ctl, col) in new[] { ("R", r, 0), ("G", g, 1), ("B", b, 2) })
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new TextBlock { Text = lbl, VerticalAlignment = VerticalAlignment.Center, Foreground = Muted }, ctl } };
            Grid.SetColumn(cell, col);
            rgb.Children.Add(cell);
        }
        sp.Children.Add(rgb);

        void Sync()
        {
            syncing = true;
            swatch.Background = new SolidColorBrush(S.Color);
            hex.Text = $"#{S.Color.R:X2}{S.Color.G:X2}{S.Color.B:X2}";
            r.Value = S.Color.R;
            g.Value = S.Color.G;
            b.Value = S.Color.B;
            pick.IsChecked = S.Picking;
            syncing = false;
        }
        _colorWatchers.Add(Sync);
        Sync();

        sp.Children.Add(PaletteGrid(c => SetColor(c)));
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

    private static UniformGrid PaletteGrid(Action<Color> pick)
    {
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var h in Palette)
        {
            var c = Color.Parse(h);
            var cell = new Border
            {
                Height = 34,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(c),
                BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0")),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(cell, h);
            cell.PointerPressed += (_, _) => pick(c);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private Control ColorButton(string label, Func<Color> get, Action<Color> set)
    {
        var sw = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(get()), BorderBrush = Line, BorderThickness = new Thickness(1) };
        var view = new ColorView { Color = get(), IsAlphaEnabled = false, IsAlphaVisible = false, Width = 320 };
        view.ColorChanged += (_, e) =>
        {
            set(Color.FromRgb(e.NewColor.R, e.NewColor.G, e.NewColor.B));
            sw.Background = new SolidColorBrush(get());
        };
        return new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { sw, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } } },
            Flyout = new Flyout { Content = view, Placement = PlacementMode.Left },
            Margin = new Thickness(0, 0, 6, 0),
        };
    }

    // ------------------------------------------------------------ thumbnails

    private static IImage Thumb(string key, int w, int h, Action<Engine> draw, bool transparent = false)
    {
        if (ThumbCache.TryGetValue(key, out var img)) return img;
        using var e = new Engine(w, h);
        if (transparent)
        {
            var v = e.View;
            v.ShowBg = 0;
            e.SetView(v);
        }
        draw(e);
        var bmp = e.CreateBitmap();
        e.CopyDisplay(bmp, 2);
        ThumbCache[key] = bmp;
        return bmp;
    }

    private static IImage ModelThumb(string key, IEnumerable<ModelPart> parts, string color, int finish, int size = 60) =>
        Thumb("model:" + key, size, size, e =>
        {
            double s = size * 0.78;
            e.AddObject(parts, new Vector3D(size / 2.0, size / 2.0, -s / 2), new Vector3D(s, s, s), 0, Color.Parse(color), finish, Label.None, -12, 28);
        }, true);

    private static IImage BrushThumb(int i) => Thumb("brush:" + i, 70, 34, e =>
    {
        var ink = Color.Parse("#2B2B2B");
        if (i == PaintTools.Eraser)
        {
            e.StrokeBegin(0, Color.Parse("#8A8A8A"), 18, 1, 0, new Point(0, 17));
            e.StrokeTo(new Point(70, 17));
            e.StrokeEnd();
        }
        double size = i switch { PaintTools.Pixel => 3, PaintTools.Pencil => 3, PaintTools.Spray => 14, PaintTools.Eraser => 10, _ => 8 };
        e.StrokeBegin(i, ink, size, i == 3 ? 0.6 : 1, 0, new Point(8, 22));
        for (int x = 8; x <= 62; x += 2) e.StrokeTo(new Point(x, 17 + Math.Sin((x - 8) / 54.0 * Math.PI * 2) * 8));
        e.StrokeEnd();
    }, true);

    // ----------------------------------------------------------------- panels

    private void BrushPanel(StackPanel p)
    {
        p.Children.Add(Header("브러시"));
        p.Children.Add(new TextBlock { Text = S.Bucket ? "채우기" : BrushNames[S.Brush], FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Accent });
        var grid = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < BrushNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(Labeled(Img(BrushThumb(k)), BrushNames[k]), BrushNames[k], !S.Bucket && S.Brush == k,
                () => { S.Brush = k; S.Bucket = false; S.Picking = false; }, 90, 64));
        }
        grid.Children.Add(Tile(Labeled(Icons.Make(Icons.Bucket, 30), "채우기"), "채우기", S.Bucket, () => { S.Bucket = true; S.Picking = false; }, 90, 64));
        p.Children.Add(grid);

        if (S.Bucket)
        {
            p.Children.Add(SliderRow("허용 오차", 0, 255, S.Tolerance, v => S.Tolerance = (int)v, v => $"{(int)v}"));
        }
        else
        {
            if (S.Brush != PaintTools.Eraser) p.Children.Add(FinishRow());
            p.Children.Add(SliderRow("두께", 1, 200, S.BrushSize, v => S.BrushSize = v, v => $"{(int)v}px"));
            if (S.Brush != PaintTools.Eraser)
                p.Children.Add(SliderRow("불투명도", 1, 100, S.Opacity * 100, v => S.Opacity = v / 100, v => $"{(int)v}%"));
        }
        if (S.Brush != PaintTools.Eraser || S.Bucket) p.Children.Add(ColorSection());
        else p.Children.Add(Hint("지우개로 지운 부분은 투명해지며, 캔버스 배경 색이 비쳐 보입니다."));
    }

    private void ShapePanel(StackPanel p)
    {
        p.Children.Add(Header("2D 셰이프"));
        var pending = _surface.Pending is ShapeItem or CurveItem;
        if (pending)
        {
            p.Children.Add(Section("편집"));
            p.Children.Add(Grid2(
                Action("3D로 만들기", () => _surface.Make3D(), Icons.Make3D),
                Action("완료", () => _surface.CommitPending()),
                Action("왼쪽으로 회전", () => _surface.RotatePending(-90), Icons.RotateLeft),
                Action("오른쪽으로 회전", () => _surface.RotatePending(90), Icons.RotateRight),
                Action("좌우 대칭", () => _surface.FlipPending(true), Icons.FlipH),
                Action("상하 대칭", () => _surface.FlipPending(false), Icons.FlipV)));
        }
        p.Children.Add(Section("선 및 곡선"));
        var lines = new WrapPanel();
        for (int i = 0; i < Shapes2D.LineNames.Length; i++)
        {
            int k = i, n = k == 0 ? 2 : k + 2;
            var view = new GeometryView
            {
                Width = 52,
                Height = 46,
                Build = r =>
                {
                    var pts = Enumerable.Range(0, n).Select(j =>
                    {
                        double t = j / (double)(n - 1);
                        double wave = j == 0 || j == n - 1 ? 0 : (j % 2 == 1 ? -1 : 1) * r.Height * 0.45;
                        return new Point(r.Left + r.Width * t, r.Bottom - r.Height * t + wave);
                    }).ToList();
                    return Shapes2D.Curve(pts);
                },
            };
            lines.Children.Add(Tile(view, Shapes2D.LineNames[k], S.Line == k, () => S.Line = k, 64, 50));
        }
        p.Children.Add(lines);
        p.Children.Add(Section("셰이프"));
        var grid = new WrapPanel();
        for (int i = 0; i < Shapes2D.Names.Length; i++)
        {
            int k = i;
            grid.Children.Add(Tile(new GeometryView { Width = 46, Height = 42, Build = r => Shapes2D.Build(k, r), Filled = true },
                Shapes2D.Names[k], S.Line < 0 && S.Shape == k, () => { S.Shape = k; S.Line = -1; }, 52, 46));
        }
        p.Children.Add(grid);
        p.Children.Add(Hint("캔버스에서 드래그해 그린 뒤 손잡이로 크기·회전을 조정하세요. 곡선은 점을 끌어 모양을 바꿉니다. 다른 곳을 클릭하거나 Enter를 누르면 확정됩니다."));

        p.Children.Add(Section("채우기"));
        p.Children.Add(Combo(new[] { "없음", "단색", "그라데이션" }, S.FillMode, v => { S.FillMode = v; _surface.SyncPending(); ShowPanel(); }));
        if (S.FillMode > 0)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(ColorButton(S.FillMode == 2 ? "시작 색" : "채우기 색", () => S.FillColor, c => { S.FillColor = c; _surface.SyncPending(); }));
            if (S.FillMode == 2) row.Children.Add(ColorButton("끝 색", () => S.FillColor2, c => { S.FillColor2 = c; _surface.SyncPending(); }));
            p.Children.Add(row);
        }
        p.Children.Add(Section("선 종류"));
        p.Children.Add(Combo(new[] { "실선", "파선", "점선", "없음" }, S.LineType, v => { S.LineType = v; _surface.SyncPending(); }));
        p.Children.Add(SliderRow("두께", 0, 60, S.ShapeThickness, v => { S.ShapeThickness = v; _surface.SyncPending(); }, v => $"{(int)v}px"));
        p.Children.Add(SliderRow("불투명도", 1, 100, S.ShapeOpacity * 100, v => { S.ShapeOpacity = v / 100; _surface.SyncPending(); }, v => $"{(int)v}%"));
        p.Children.Add(FinishRow());
        p.Children.Add(ColorSection("선 색"));
    }

    private void ModelPanel(StackPanel p)
    {
        p.Children.Add(Header("3D 셰이프"));
        p.Children.Add(Section("3D 개체"));
        var basics = new WrapPanel();
        for (int i = 0; i < Library.BasicNames.Length; i++)
        {
            int k = i;
            var thumb = ModelThumb("basic" + k, new[] { new ModelPart(Library.BasicPrims[k], 0, 0, 0, 1, 1, 1) }, "#4F8EF7", 1);
            basics.Children.Add(Tile(Labeled(Img(thumb), Library.BasicNames[k]), Library.BasicNames[k],
                S.Model == null && S.Doodle < 0 && S.Basic == k, () => { S.Basic = k; S.Model = null; S.Doodle = -1; }, 90, 84));
        }
        p.Children.Add(basics);
        p.Children.Add(Section("3D 모델"));
        var people = new WrapPanel();
        foreach (var m in Library.People)
        {
            var model = m;
            people.Children.Add(Tile(Labeled(Img(ModelThumb(m.Name, m.Parts, m.Color, m.Finish)), m.Name), m.Name,
                S.Model == model, () => { S.Model = model; S.Doodle = -1; S.Color = Color.Parse(model.Color); S.Finish = model.Finish; }, 90, 84));
        }
        p.Children.Add(people);
        p.Children.Add(Section("3D 낙서"));
        var doodles = new WrapPanel();
        string[] dn = { "날카로운 가장자리", "부드러운 가장자리" };
        for (int i = 0; i < 2; i++)
        {
            int k = i;
            var thumb = Thumb("doodle" + k, 60, 60, e =>
            {
                var mask = new byte[40 * 40];
                for (int y = 0; y < 40; y++)
                    for (int x = 0; x < 40; x++)
                    {
                        double dx = x - 20, dy = y - 20, ang = Math.Atan2(dy, dx), rr = 15 + 4 * Math.Sin(ang * 3);
                        mask[y * 40 + x] = Math.Sqrt(dx * dx + dy * dy) < rr ? (byte)255 : (byte)0;
                    }
                e.AddMaskObject(mask, 40, 40, k == 1, new Vector3D(30, 30, -12), new Vector3D(52, 52, k == 1 ? 26 : 14), 0, Color.Parse("#FF7043"), 1, Label.None);
            }, true);
            doodles.Children.Add(Tile(Labeled(Img(thumb), dn[k]), dn[k], S.Doodle == k, () => { S.Doodle = k; S.Model = null; }, 136, 84));
        }
        p.Children.Add(doodles);
        p.Children.Add(Hint(S.Doodle >= 0
            ? "캔버스에 윤곽을 그리면 입체 개체가 만들어집니다."
            : "캔버스에서 드래그(또는 클릭)해 3D 개체를 놓습니다. 개체를 클릭하면 선택되고, 손잡이로 회전(X·Y·Z축)·크기·깊이를 조정할 수 있습니다."));
        SelectedObjectSection(p);
        p.Children.Add(FinishRow());
        p.Children.Add(ColorSection());
    }

    private void SelectedObjectSection(StackPanel p)
    {
        if (_surface.Selected == 0) return;
        p.Children.Add(Section("선택한 3D 개체"));
        p.Children.Add(Hint("아래 색과 재질을 바꾸면 선택한 개체에 바로 적용됩니다. Delete 키로 삭제할 수 있습니다."));
        p.Children.Add(Grid2(
            Action("복제", () => { var id = _engine.DuplicateObject(_surface.Selected); _surface.Refresh(); _surface.Select(id); }, Icons.Copy),
            Action("삭제", () => _surface.DeleteSelection(), Icons.Trash)));
    }

    private void StickerPanel(StackPanel p)
    {
        p.Children.Add(Header("스티커"));
        if (_surface.Pending is ImageItem)
        {
            p.Children.Add(Section("편집"));
            p.Children.Add(Grid2(
                Action("완료", () => _surface.CommitPending()),
                Action("취소", () => _surface.CancelPending()),
                Action("좌우 대칭", () => _surface.FlipPending(true), Icons.FlipH),
                Action("상하 대칭", () => _surface.FlipPending(false), Icons.FlipV)));
        }
        var tabs = new UniformGrid { Columns = 3 };
        string[] names = { "기본", "질감", "사용자 지정" };
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            var b = new ToggleButton { Content = names[i], IsChecked = _stickerTab == k, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(2) };
            b.Click += (_, _) => { _stickerTab = k; ShowPanel(); };
            tabs.Children.Add(b);
        }
        p.Children.Add(tabs);
        var grid = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        switch (_stickerTab)
        {
            case 0:
                for (int i = 0; i < Stickers.Names.Length; i++)
                {
                    int k = i;
                    grid.Children.Add(Tile(new StickerView { Kind = k }, Stickers.Names[k], S.Texture < 0 && S.Custom == null && S.Sticker == k,
                        () => { S.Sticker = k; S.Texture = -1; S.Custom = null; }, 66, 66));
                }
                break;
            case 1:
                for (int i = 0; i < Textures.Names.Length; i++)
                {
                    int k = i;
                    var img = new Image { Source = Textures.Get(k), Stretch = Stretch.UniformToFill, Width = 60, Height = 44 };
                    grid.Children.Add(Tile(Labeled(img, Textures.Names[k]), Textures.Names[k], S.Texture == k,
                        () => { S.Texture = k; S.Custom = null; }, 90, 70));
                }
                break;
            default:
                foreach (var f in CustomStickers())
                {
                    var path = f;
                    IImage? img = null;
                    try { img = new Bitmap(path); } catch { continue; }
                    var bitmap = img;
                    grid.Children.Add(Tile(new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(4) }, Path.GetFileName(path),
                        S.Custom != null && ReferenceEquals(S.Custom, bitmap), () => { S.Custom = bitmap; S.Texture = -1; }, 66, 66));
                }
                grid.Children.Add(Tile(Labeled(Icons.Make(Icons.Insert, 26), "스티커 추가"), "이미지 파일로 스티커 만들기", false, () => _ = AddCustomSticker(), 90, 66));
                break;
        }
        p.Children.Add(grid);
        p.Children.Add(SliderRow("불투명도", 1, 100, S.StickerOpacity * 100, v => { S.StickerOpacity = v / 100; _surface.SyncPending(); }, v => $"{(int)v}%"));
        p.Children.Add(Hint("캔버스에서 드래그(또는 클릭)해 붙인 뒤 손잡이로 크기·회전을 조정하세요. 다른 곳을 클릭하면 확정됩니다.\n3D 개체 표면에 찍는 기능은 지원하지 않습니다."));
    }

    private static IEnumerable<string> CustomStickers()
    {
        if (!Directory.Exists(StickerDir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(StickerDir).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => File.GetCreationTime(f));
    }

    private async Task AddCustomSticker()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "스티커로 사용할 이미지",
            FileTypeFilter = new[] { new FilePickerFileType("이미지") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } src) return;
        try
        {
            Directory.CreateDirectory(StickerDir);
            using var bmp = SkiaSharp.SKBitmap.Decode(src) ?? throw new InvalidDataException("이미지를 읽을 수 없습니다.");
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            var dst = Path.Combine(StickerDir, $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileNameWithoutExtension(src)}.png");
            using (var fs = File.Create(dst)) data.SaveTo(fs);
            S.Custom = new Bitmap(dst);
            S.Texture = -1;
            ShowPanel();
        }
        catch (Exception ex)
        {
            await Ask("스티커를 추가할 수 없음", ex.Message, "확인");
        }
    }

    private void TextPanel(StackPanel p)
    {
        p.Children.Add(Header("텍스트"));
        var kinds = new WrapPanel();
        kinds.Children.Add(Tile(Labeled(new TextBlock { Text = "가", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center }, "2D 텍스트"), "2D 텍스트", !S.Text3D,
            () => { S.Text3D = false; _surface.SyncPending(); }, 136, 70));
        kinds.Children.Add(Tile(Labeled(Img(Thumb("text3d", 60, 40, e =>
        {
            var mask = new byte[48 * 30];
            for (int y = 0; y < 30; y++)
                for (int x = 0; x < 48; x++)
                    mask[y * 48 + x] = (x is > 6 and < 42 && y is > 4 and < 10) || (x is > 20 and < 28 && y is > 4 and < 27) ? (byte)255 : (byte)0;
            e.AddMaskObject(mask, 48, 30, false, new Vector3D(30, 20, -6), new Vector3D(50, 32, 10), 0, Color.Parse("#E53935"), 1, Label.None);
        }, true)), "3D 텍스트"), "3D 텍스트", S.Text3D, () => { S.Text3D = true; _surface.SyncPending(); }, 136, 70));
        p.Children.Add(kinds);
        p.Children.Add(Hint("캔버스에서 드래그해 텍스트 상자를 만들고 바로 입력하세요. 상자 밖을 클릭하면 확정됩니다."));

        p.Children.Add(Section("글꼴"));
        var fonts = new ComboBox { ItemsSource = _fonts, SelectedItem = S.Font, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 400 };
        fonts.SelectionChanged += (_, _) => { S.Font = fonts.SelectedItem as string ?? ""; _surface.SyncPending(); };
        p.Children.Add(fonts);
        p.Children.Add(SliderRow("크기", 8, 300, S.FontSize, v => { S.FontSize = v; _surface.SyncPending(); }, v => $"{(int)v}pt"));

        ToggleButton Toggle(Control content, bool on, string tip, Action<bool> set)
        {
            var t = new ToggleButton { Content = content, IsChecked = on, Width = 40 };
            ToolTip.SetTip(t, tip);
            t.IsCheckedChanged += (_, _) => { set(t.IsChecked == true); _surface.SyncPending(); };
            return t;
        }
        var style = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        style.Children.Add(Toggle(new TextBlock { Text = "가", FontWeight = FontWeight.Bold }, S.Bold, "굵게", v => S.Bold = v));
        style.Children.Add(Toggle(new TextBlock { Text = "가", FontStyle = FontStyle.Italic }, S.Italic, "기울임꼴", v => S.Italic = v));
        style.Children.Add(Toggle(new TextBlock { Text = "가", TextDecorations = TextDecorations.Underline }, S.Underline, "밑줄", v => S.Underline = v));
        style.Children.Add(new Border { Width = 12 });
        var aligns = new[] { (TextAlignment.Left, "M4,6 H20 M4,10 H14 M4,14 H20 M4,18 H14", "왼쪽 맞춤"), (TextAlignment.Center, "M4,6 H20 M7,10 H17 M4,14 H20 M7,18 H17", "가운데 맞춤"), (TextAlignment.Right, "M4,6 H20 M10,10 H20 M4,14 H20 M10,18 H20", "오른쪽 맞춤") };
        foreach (var (al, icon, tip) in aligns)
        {
            var a = al;
            var t = new ToggleButton { Content = Icons.Make(icon, 16), IsChecked = S.Align == a, Width = 40 };
            ToolTip.SetTip(t, tip);
            t.Click += (_, _) => { S.Align = a; _surface.SyncPending(); ShowPanel(); };
            style.Children.Add(t);
        }
        p.Children.Add(style);
        if (S.Text3D) p.Children.Add(FinishRow());
        p.Children.Add(ColorSection("글자 색"));
    }

    private void EffectPanel(StackPanel p)
    {
        var v = _engine.View;
        p.Children.Add(Header("효과"));
        p.Children.Add(Section("필터"));
        var grid = new WrapPanel();
        foreach (var preset in Presets)
        {
            var pr = preset;
            var thumb = Thumb("fx:" + pr.Name, 82, 46, e =>
            {
                e.StrokeBegin(0, Color.Parse("#BFE3FF"), 200, 1, 0, new Point(0, 10));
                e.StrokeTo(new Point(82, 10));
                e.StrokeEnd();
                e.StrokeBegin(0, Color.Parse("#8BC34A"), 30, 1, 0, new Point(0, 46));
                e.StrokeTo(new Point(82, 46));
                e.StrokeEnd();
                var sph = new[] { new ModelPart(Library.Sphere, 0, 0, 0, 1, 1, 1) };
                var cube = new[] { new ModelPart(Library.Cube, 0, 0, 0, 1, 1, 1) };
                e.AddObject(sph, new Vector3D(24, 22, -14), new Vector3D(28, 28, 28), 0, Color.Parse("#FF8A3D"), 1, Label.None);
                e.AddObject(cube, new Vector3D(58, 24, -14), new Vector3D(24, 24, 24), 0, Color.Parse("#3D7BFF"), 0, Label.None, 20, 30);
                e.SetView(ToView(pr, e.View));
            });
            bool on = Engine.Pack(Color.Parse(pr.Light)) == v.LightColor && pr.Filter == v.Filter && Math.Abs(pr.Brightness - v.Brightness) < 0.01;
            grid.Children.Add(Tile(Labeled(Img(thumb), pr.Name), pr.Name, on, () =>
            {
                _engine.SetView(ToView(pr, _engine.View), Label.Effect);
                _surface.Refresh();
            }, 90, 66));
        }
        p.Children.Add(grid);

        p.Children.Add(Section("조명"));
        var wheel = new HueWheel { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, Value = Engine.Unpack(v.LightColor), Cursor = new Cursor(StandardCursorType.Hand) };
        bool started = false;
        wheel.ValueChanged += (c, final) =>
        {
            var nv = _engine.View;
            nv.LightColor = Engine.Pack(c);
            nv.Tint = Math.Max(nv.Tint, 0.35f);
            _engine.SetView(nv, started ? Label.None : Label.Effect);
            started = !final;
            _surface.Refresh(final, !final);
        };
        p.Children.Add(wheel);
        Action Commit = () => _surface.Refresh();
        void Edit(Func<ViewSettings, ViewSettings> f)
        {
            _engine.SetView(f(_engine.View));
            _surface.Refresh(false, true);
        }
        p.Children.Add(SliderRow("밝기", 30, 170, v.Brightness * 100, x => Edit(s => { s.Brightness = (float)(x / 100); return s; }), x => $"{(int)x}%", Commit));
        p.Children.Add(SliderRow("조명 회전", 0, 360, ((v.LightAngle % 360) + 360) % 360, x => Edit(s => { s.LightAngle = (float)x; return s; }), x => $"{(int)x}°", Commit));
        p.Children.Add(SliderRow("주변광", 0, 100, v.Ambient * 100, x => Edit(s => { s.Ambient = (float)(x / 100); return s; }), x => $"{(int)x}%", Commit));
        p.Children.Add(SliderRow("조명 색 강도", 0, 100, v.Tint * 100, x => Edit(s => { s.Tint = (float)(x / 100); return s; }), x => $"{(int)x}%", Commit));
        p.Children.Add(Hint("효과는 캔버스와 3D 개체 전체에 비파괴적으로 적용되며, 이미지로 저장할 때 함께 반영됩니다. 조명 회전과 주변광은 3D 개체의 음영에 영향을 줍니다."));
    }

    private static ViewSettings ToView(Preset p, ViewSettings v)
    {
        v.LightColor = Engine.Pack(Color.Parse(p.Light));
        v.Brightness = p.Brightness;
        v.Ambient = p.Ambient;
        v.LightAngle = p.Angle;
        v.Filter = p.Filter;
        v.Tint = p.Tint;
        return v;
    }

    private void CanvasPanel(StackPanel p)
    {
        var v = _engine.View;
        p.Children.Add(Header("캔버스"));
        if (S.CropMode)
        {
            p.Children.Add(Section("자르기"));
            p.Children.Add(Hint("손잡이를 끌거나 영역을 드래그해 남길 부분을 정한 뒤 '완료'를 누르세요."));
            p.Children.Add(Grid2(Action("완료", () => { _surface.EndCrop(true); ShowPanel(); }), Action("취소", () => { _surface.EndCrop(false); ShowPanel(); })));
            _panel.Content = p;
            return;
        }
        var show = new ToggleSwitch { IsChecked = v.ShowBg != 0, OnContent = "켬", OffContent = "끔" };
        show.IsCheckedChanged += (_, _) =>
        {
            var nv = _engine.View;
            nv.ShowBg = show.IsChecked == true ? 1 : 0;
            _engine.SetView(nv, Label.CanvasView);
            _surface.Refresh();
        };
        p.Children.Add(Section("캔버스 표시"));
        p.Children.Add(show);
        p.Children.Add(Hint("끄면 배경이 투명해지며, PNG로 저장할 때 투명 배경이 유지됩니다."));

        p.Children.Add(Section("캔버스 색"));
        p.Children.Add(PaletteGrid(c =>
        {
            var nv = _engine.View;
            nv.Bg = Engine.Pack(c);
            nv.ShowBg = 1;
            _engine.SetView(nv, Label.CanvasView);
            _surface.Refresh();
            ShowPanel();
        }));

        p.Children.Add(Section("캔버스 크기"));
        var unit = Combo(new[] { "픽셀", "백분율" }, 0, _ => { });
        var wBox = new NumericUpDown { Minimum = 1, Maximum = 16384, Value = _engine.Width, Increment = 1, FormatString = "0" };
        var hBox = new NumericUpDown { Minimum = 1, Maximum = 16384, Value = _engine.Height, Increment = 1, FormatString = "0" };
        var lockRatio = new CheckBox { Content = "가로 세로 비율 고정", IsChecked = true };
        var scale = new CheckBox { Content = "캔버스와 함께 이미지 크기 조정", IsChecked = false };
        double ratio = (double)_engine.Width / _engine.Height;
        bool syncing = false;
        bool Percent() => unit.SelectedIndex == 1;
        unit.SelectionChanged += (_, _) =>
        {
            syncing = true;
            wBox.Maximum = hBox.Maximum = Percent() ? 1000 : 16384;
            wBox.Value = Percent() ? 100 : _engine.Width;
            hBox.Value = Percent() ? 100 : _engine.Height;
            syncing = false;
        };
        wBox.ValueChanged += (_, _) =>
        {
            if (syncing || lockRatio.IsChecked != true || wBox.Value is not { } x) return;
            syncing = true;
            hBox.Value = Math.Max(1, Math.Round(Percent() ? x : x / (decimal)ratio));
            syncing = false;
        };
        hBox.ValueChanged += (_, _) =>
        {
            if (syncing || lockRatio.IsChecked != true || hBox.Value is not { } x) return;
            syncing = true;
            wBox.Value = Math.Max(1, Math.Round(Percent() ? x : x * (decimal)ratio));
            syncing = false;
        };
        var sizes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        sizes.Children.Add(new StackPanel { Children = { Hint("너비"), wBox } });
        var hp = new StackPanel { Children = { Hint("높이"), hBox } };
        Grid.SetColumn(hp, 2);
        sizes.Children.Add(hp);
        p.Children.Add(unit);
        p.Children.Add(sizes);
        p.Children.Add(lockRatio);
        p.Children.Add(scale);
        p.Children.Add(Action("크기 적용", () =>
        {
            double w = (double)(wBox.Value ?? 1), h = (double)(hBox.Value ?? 1);
            if (Percent()) { w = _engine.Width * w / 100; h = _engine.Height * h / 100; }
            int iw = (int)Math.Round(w), ih = (int)Math.Round(h);
            if (iw == _engine.Width && ih == _engine.Height) return;
            _engine.Resize(iw, ih, scale.IsChecked == true);
            _surface.Refresh();
            _surface.FitToView();
            ShowPanel();
        }));
        p.Children.Add(Hint("캔버스 가장자리의 손잡이를 끌어 크기를 바꿀 수도 있습니다(Shift: 비율 유지)."));

        p.Children.Add(Section("자르기"));
        p.Children.Add(Action("자르기", () => { _surface.BeginCrop(); ShowPanel(); }, Icons.Crop));

        p.Children.Add(Section("회전 및 대칭"));
        void Op(Action a)
        {
            a();
            _surface.Refresh();
            _surface.FitToView();
            ShowPanel();
        }
        p.Children.Add(Grid2(
            Action("왼쪽으로 회전", () => Op(() => _engine.Rotate(false)), Icons.RotateLeft),
            Action("오른쪽으로 회전", () => Op(() => _engine.Rotate(true)), Icons.RotateRight),
            Action("좌우 대칭", () => Op(() => _engine.Flip(true)), Icons.FlipH),
            Action("상하 대칭", () => Op(() => _engine.Flip(false)), Icons.FlipV)));
    }

    private void LibraryPanel(StackPanel p)
    {
        p.Children.Add(Header("3D 라이브러리"));
        var search = new TextBox { Text = _libraryQuery, Watermark = "모델 검색", InnerLeftContent = new Border { Padding = new Thickness(8, 0, 0, 0), Child = Icons.Make(Icons.Search, 16) } };
        var results = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        void Fill()
        {
            results.Children.Clear();
            foreach (var m in Library.Search(_libraryCategory, _libraryQuery))
            {
                var model = m;
                results.Children.Add(Tile(Labeled(Img(ModelThumb(m.Name, m.Parts, m.Color, m.Finish, 64)), m.Name), $"{m.Category} · {m.Name}", false,
                    () => _surface.PlaceModel(model), 90, 88));
            }
            if (results.Children.Count == 0) results.Children.Add(Hint("검색 결과가 없습니다."));
        }
        search.TextChanged += (_, _) => { _libraryQuery = search.Text ?? ""; Fill(); };
        p.Children.Add(search);
        var cats = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var c in new string?[] { null }.Concat(Library.Categories))
        {
            var cat = c;
            var b = new ToggleButton { Content = cat ?? "전체", IsChecked = _libraryCategory == cat, Margin = new Thickness(2), Padding = new Thickness(10, 4), FontSize = 12 };
            b.Click += (_, _) => { _libraryCategory = cat; ShowPanel(); };
            cats.Children.Add(b);
        }
        p.Children.Add(cats);
        Fill();
        p.Children.Add(results);
        p.Children.Add(Hint("모델을 클릭하면 캔버스 가운데에 놓입니다. 이 모델들은 OpenPaint 3D가 기본 도형을 조합해 만든 자체 모델입니다(온라인 Remix 3D 라이브러리는 없습니다)."));
        SelectedObjectSection(p);
        if (_surface.Selected != 0)
        {
            p.Children.Add(FinishRow());
            p.Children.Add(ColorSection());
        }
    }

    // ---------------------------------------------------------------- history

    private Control BuildHistory(Flyout flyout)
    {
        int u = _engine.UndoCount, r = _engine.RedoCount;
        var list = new StackPanel { Width = 240 };
        list.Children.Add(new TextBlock { Text = "기록", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 8) });
        Control Row(string text, bool current, bool future, Action go)
        {
            var b = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = current ? AccentLight : Brushes.Transparent,
                Foreground = future ? Muted : Dark,
                FontWeight = current ? FontWeight.SemiBold : FontWeight.Normal,
            };
            b.Click += (_, _) => { go(); flyout.Hide(); };
            return b;
        }
        list.Children.Add(Row("처음 상태", u == 0, false, () => { for (int i = 0; i < u; i++) _engine.Undo(); _surface.Refresh(); }));
        for (int k = 0; k < u; k++)
        {
            int steps = u - 1 - k;
            list.Children.Add(Row(Labels.Name(_engine.HistoryLabel(false, steps)), k == u - 1, false, () =>
            {
                for (int i = 0; i < steps; i++) _engine.Undo();
                _surface.Refresh();
            }));
        }
        for (int k = 0; k < r; k++)
        {
            int steps = k + 1;
            list.Children.Add(Row(Labels.Name(_engine.HistoryLabel(true, k)), false, true, () =>
            {
                for (int i = 0; i < steps; i++) _engine.Redo();
                _surface.Refresh();
            }));
        }
        if (u == 0 && r == 0) list.Children.Add(Hint("아직 기록이 없습니다."));
        return new ScrollViewer { Content = list, MaxHeight = 480 };
    }

    // ----------------------------------------------------------------- state

    private void UpdateState()
    {
        _undo.IsEnabled = _engine.CanUndo || _surface.Pending != null;
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
        if (_surface.Pending != null) _surface.CancelPending();
        else if (_engine.Undo()) _surface.Refresh();
        if (S.Tool is Tool.Canvas or Tool.Effects) ShowPanel();
        UpdateState();
    }

    private void Redo()
    {
        _surface.CommitPending();
        if (_engine.Redo()) _surface.Refresh();
        if (S.Tool is Tool.Canvas or Tool.Effects) ShowPanel();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool inText = e.Source is TextBox;
        if (_menu.IsVisible)
        {
            if (e.Key == Key.Escape) { HideMenu(); e.Handled = true; }
            return;
        }
        if (inText && !(ctrl && e.Key is Key.S or Key.O or Key.N)) return;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift: Redo(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.S when ctrl && shift: _surface.CommitPending(); ShowMenu("다른 이름으로 저장"); break;
            case Key.S when ctrl: _ = Save(); break;
            case Key.O when ctrl: _ = OpenFile(); break;
            case Key.N when ctrl: _ = NewCanvas(); break;
            case Key.V when ctrl: _ = Paste(); break;
            case Key.D when ctrl && _surface.Selected != 0:
                var id = _engine.DuplicateObject(_surface.Selected);
                _surface.Refresh();
                _surface.Select(id);
                break;
            case Key.D0 when ctrl: _surface.FitToView(); break;
            case Key.OemPlus when ctrl: _surface.SetZoom(_surface.Zoom * 1.25); break;
            case Key.OemMinus when ctrl: _surface.SetZoom(_surface.Zoom / 1.25); break;
            case Key.Delete or Key.Back: _surface.DeleteSelection(); break;
            case Key.Enter: _surface.CommitPending(); break;
            case Key.Escape:
                S.Picking = false;
                if (S.CropMode) _surface.EndCrop(false);
                else if (_surface.Pending != null) _surface.CancelPending();
                else _surface.Select(0);
                ShowPanel();
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------- menu

    private void BuildMenu()
    {
        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        var back = new Button { Content = Icons.Make(Icons.Back, 22, Brushes.White), Background = Brushes.Transparent, Margin = new Thickness(12, 12, 0, 12), Padding = new Thickness(8) };
        back.Click += (_, _) => HideMenu();
        nav.Children.Add(back);
        void Item(string name, string icon)
        {
            var row = new Border
            {
                Padding = new Thickness(24, 12),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children = { Icons.Make(icon, 22, Brushes.White), new TextBlock { Text = name, FontSize = 16, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } },
                },
            };
            row.PointerPressed += (_, _) => ShowMenu(name);
            _menuItems[name] = row;
            nav.Children.Add(row);
        }
        Item("새로 만들기", Icons.New);
        Item("열기", Icons.Open);
        Item("삽입", Icons.Insert);
        Item("저장", Icons.Save);
        Item("다른 이름으로 저장", Icons.SaveAs);
        Item("정보", Icons.Info);
        Item("끝내기", Icons.Exit);
        var left = new Border { Width = 280, Background = new SolidColorBrush(Color.Parse("#1F2A44")), Child = nav };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { left } };
        var right = new Border { Background = new SolidColorBrush(Color.Parse("#F3F3F3")), Padding = new Thickness(48, 40), Child = _menuContent };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        _menu.Child = grid;
    }

    private void HideMenu() => _menu.IsVisible = false;

    private void ShowMenu(string page)
    {
        _surface.CommitPending();
        _menu.IsVisible = true;
        foreach (var (name, row) in _menuItems)
            row.Background = name == page ? new SolidColorBrush(Color.Parse("#34466E")) : Brushes.Transparent;
        var p = new StackPanel { Spacing = 14, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        TextBlock Title(string t) => new() { Text = t, FontSize = 30, FontWeight = FontWeight.Light, Margin = new Thickness(0, 0, 0, 10) };
        Control BigTile(string title, string desc, string icon, Action click)
        {
            var b = new Border
            {
                Width = 230,
                Height = 150,
                Background = Brushes.White,
                CornerRadius = new CornerRadius(6),
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 14, 14),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        Icons.Make(icon, 34, Accent),
                        new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = desc, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            b.PointerPressed += (_, _) => click();
            return b;
        }
        switch (page)
        {
            case "새로 만들기":
                p.Children.Add(Title("새로 만들기"));
                p.Children.Add(new WrapPanel
                {
                    Children =
                    {
                        BigTile("새 프로젝트", "1920 × 1080 흰색 캔버스로 시작합니다.", Icons.New, () => { HideMenu(); _ = NewCanvas(); }),
                        BigTile("투명 캔버스", "배경 없이 시작합니다. PNG로 저장하면 투명하게 남습니다.", Icons.Canvas, () => { HideMenu(); _ = NewCanvas(transparent: true); }),
                    },
                });
                break;
            case "열기":
                p.Children.Add(Title("열기"));
                p.Children.Add(new WrapPanel { Children = { BigTile("파일 찾아보기", "OpenPaint 3D 프로젝트(.op3d)나 이미지(PNG, JPEG, BMP, GIF, WebP)를 엽니다.", Icons.Open, () => { HideMenu(); _ = OpenFile(); }) } });
                var recent = Recent().ToList();
                if (recent.Count > 0)
                {
                    p.Children.Add(new TextBlock { Text = "최근 파일", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
                    foreach (var f in recent)
                    {
                        var path = f;
                        var b = new Button { Content = new StackPanel { Children = { new TextBlock { Text = Path.GetFileName(f), FontWeight = FontWeight.SemiBold }, new TextBlock { Text = f, FontSize = 11, Foreground = Muted } } }, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 8) };
                        b.Click += async (_, _) => { HideMenu(); if (await ConfirmDiscard()) OpenPath(path); };
                        p.Children.Add(b);
                    }
                }
                break;
            case "삽입":
                p.Children.Add(Title("삽입"));
                p.Children.Add(new WrapPanel { Children = { BigTile("파일 찾아보기", "이미지를 현재 캔버스 위에 넣습니다. 손잡이로 크기와 위치를 조정할 수 있습니다.", Icons.Insert, () => { HideMenu(); _ = InsertFile(); }) } });
                break;
            case "저장":
                HideMenu();
                _ = Save();
                return;
            case "다른 이름으로 저장":
                p.Children.Add(Title("다른 이름으로 저장"));
                p.Children.Add(new WrapPanel
                {
                    Children =
                    {
                        BigTile("OpenPaint 3D 프로젝트", "캔버스, 3D 개체, 효과를 모두 담아 나중에 계속 편집할 수 있게 저장합니다(.op3d).", Icons.Save, () => { HideMenu(); _ = SaveAs(SaveKind.Project); }),
                        BigTile("이미지", "PNG, JPEG, BMP, GIF, TIFF로 저장합니다.", Icons.Insert, () => { HideMenu(); _ = SaveAs(SaveKind.Image); }),
                        BigTile("3D 모델", "3D 개체를 GLB 또는 3MF로 내보냅니다.", Icons.Shapes3D, () => { HideMenu(); _ = SaveAs(SaveKind.Model); }),
                    },
                });
                p.Children.Add(Hint("FBX 내보내기와 비디오 내보내기는 지원하지 않습니다."));
                break;
            case "정보":
                p.Children.Add(Title("정보"));
                p.Children.Add(new TextBlock
                {
                    Text = "OpenPaint 3D 0.2.0\n\nPaint 3D의 화면 구성과 기능 설명을 참고해 C#(Avalonia)과 Rust로 새로 만든 오픈소스 그림판입니다.\n" +
                           "Microsoft의 코드나 리소스는 포함되어 있지 않으며 Microsoft와 관련이 없습니다.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                });
                break;
            case "끝내기":
                HideMenu();
                Close();
                return;
        }
        _menuContent.Content = new ScrollViewer { Content = p };
    }

    // ------------------------------------------------------------------ files

    private enum SaveKind { Project, Image, Model }

    private static IEnumerable<string> Recent()
    {
        var f = Path.Combine(ConfigDir, "recent");
        return File.Exists(f) ? File.ReadAllLines(f).Where(File.Exists).Take(8) : Array.Empty<string>();
    }

    private static void AddRecent(string path)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var list = new[] { path }.Concat(Recent().Where(p => p != path)).Take(8);
            File.WriteAllLines(Path.Combine(ConfigDir, "recent"), list);
        }
        catch (IOException) { }
    }

    private async Task<bool> ConfirmDiscard()
    {
        _surface.CommitPending();
        if (!_dirty) return true;
        var r = await Ask("OpenPaint 3D", "변경 내용을 저장할까요?", "저장", "저장 안 함", "취소");
        return r switch { 0 => await Save(), 1 => true, _ => false };
    }

    private async Task NewCanvas(bool transparent = false)
    {
        if (!await ConfirmDiscard()) return;
        _engine.Reset(1920, 1080);
        if (transparent)
        {
            var v = _engine.View;
            v.ShowBg = 0;
            _engine.SetView(v);
        }
        _path = null;
        _surface.Select(0);
        _surface.Refresh(false);
        _surface.FitToView();
        _dirty = false;
        UpdateState();
        ShowPanel();
    }

    private static readonly FilePickerFileType OpenTypes = new("OpenPaint 3D 프로젝트 및 이미지")
    {
        Patterns = new[] { "*.op3d", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" },
    };

    private async Task OpenFile()
    {
        if (!await ConfirmDiscard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "열기", FileTypeFilter = new[] { OpenTypes } });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) OpenPath(p);
    }

    private void OpenPath(string p)
    {
        try
        {
            if (p.EndsWith(Export.ProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (!Export.LoadProject(_engine, p)) throw new InvalidDataException("OpenPaint 3D 프로젝트 파일이 아니거나 손상되었습니다.");
            }
            else _engine.LoadImage(p);
            _path = p;
            AddRecent(p);
            _surface.Select(0);
            _surface.Refresh(false);
            _surface.FitToView();
            _dirty = false;
            UpdateState();
            ShowPanel();
        }
        catch (Exception ex)
        {
            _ = Ask("열 수 없음", $"{Path.GetFileName(p)}\n{ex.Message}", "확인");
        }
    }

    private async Task InsertFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "삽입",
            FileTypeFilter = new[] { new FilePickerFileType("이미지") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" } } },
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } p) return;
        try
        {
            SelectTab(Tool.Sticker);
            _surface.BeginImage(new Bitmap(p));
            ShowPanel();
        }
        catch (Exception ex)
        {
            await Ask("삽입할 수 없음", ex.Message, "확인");
        }
    }

    private async Task Paste()
    {
        var cb = Clipboard;
        if (cb == null) return;
        try
        {
            var formats = await cb.GetFormatsAsync();
            var fmt = formats.FirstOrDefault(f => f is "image/png" or "PNG" or "image/bmp" or "image/jpeg");
            if (fmt != null && await cb.GetDataAsync(fmt) is byte[] bytes)
            {
                SelectTab(Tool.Sticker);
                _surface.BeginImage(new Bitmap(new MemoryStream(bytes)));
                ShowPanel();
                return;
            }
        }
        catch (Exception)
        {
            // fall through to the message below
        }
        await Ask("붙여넣기", "클립보드에 붙여넣을 이미지가 없습니다.", "확인");
    }

    private async Task<bool> Save()
    {
        _surface.CommitPending();
        if (_path == null) return await SaveAs(SaveKind.Project);
        var ext = Path.GetExtension(_path).ToLowerInvariant();
        try
        {
            if (ext == Export.ProjectExtension) Export.SaveProject(_engine, _path);
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp") Export.SaveImage(_engine, _path);
            else return await SaveAs(SaveKind.Project);
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

    private async Task<bool> SaveAs(SaveKind kind)
    {
        _surface.CommitPending();
        var baseName = _path != null ? Path.GetFileNameWithoutExtension(_path) : "그림";
        var (types, ext) = kind switch
        {
            SaveKind.Project => (new[] { new FilePickerFileType("OpenPaint 3D 프로젝트") { Patterns = new[] { "*.op3d" } } }, "op3d"),
            SaveKind.Image => (new[]
            {
                new FilePickerFileType("PNG 이미지") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG 이미지") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new FilePickerFileType("BMP 이미지") { Patterns = new[] { "*.bmp" } },
                new FilePickerFileType("GIF 이미지") { Patterns = new[] { "*.gif" } },
                new FilePickerFileType("TIFF 이미지") { Patterns = new[] { "*.tif", "*.tiff" } },
            }, "png"),
            _ => (new[]
            {
                new FilePickerFileType("GLB 3D 모델") { Patterns = new[] { "*.glb" } },
                new FilePickerFileType("3MF 3D 모델") { Patterns = new[] { "*.3mf" } },
            }, "glb"),
        };
        if (kind == SaveKind.Model && _engine.ObjectCount == 0)
        {
            await Ask("3D 모델 내보내기", "내보낼 3D 개체가 없습니다. 3D 셰이프나 3D 라이브러리로 개체를 추가하세요.", "확인");
            return false;
        }
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "다른 이름으로 저장",
            SuggestedFileName = $"{baseName}.{ext}",
            DefaultExtension = ext,
            FileTypeChoices = types,
            ShowOverwritePrompt = true,
        });
        if (f?.TryGetLocalPath() is not { } p) return false;
        try
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            switch (kind)
            {
                case SaveKind.Project:
                    if (e != Export.ProjectExtension) p += Export.ProjectExtension;
                    Export.SaveProject(_engine, p);
                    _path = p;
                    _dirty = false;
                    AddRecent(p);
                    break;
                case SaveKind.Image:
                    if (e is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")) p += ".png";
                    Export.SaveImage(_engine, p);
                    break;
                default:
                    if (e == ".3mf") Export.Save3mf(_engine, p);
                    else Export.SaveGlb(_engine, e == ".glb" ? p : p + ".glb");
                    break;
            }
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
        _surface.CommitPending();
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
