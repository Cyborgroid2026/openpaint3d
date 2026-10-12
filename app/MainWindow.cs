using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Path = System.IO.Path;

namespace OpenPaint3D;

public sealed class MainWindow : Window
{
    private static readonly IBrush Muted = P3.Muted;
    private static readonly IBrush Dark = P3.Text;
    private static readonly IBrush Line = P3.PaneLine;
    private static readonly IBrush AccentLight = new SolidColorBrush(Color.Parse("#DCE8F7"));

    // 6 x 3 palette, as laid out in the colour section
    private static readonly string[] Palette =
    {
        "#FFFFFF", "#C3C3C3", "#585858", "#000000", "#88001B", "#EC1C24",
        "#FF7F27", "#FFCA18", "#FDECA6", "#FFF200", "#C4FF0E", "#0ED145",
        "#8CFFFB", "#00A8F3", "#3F48CC", "#B83DBA", "#FFAEC8", "#B97A56",
    };

    private static readonly string[] BrushNames = { "마커", "캘리그래피 펜", "유화 브러시", "수채화", "픽셀 펜", "연필", "지우개", "크레용", "스프레이 캔" };
    private static readonly string[] Finishes = { "무광", "유광", "무광 금속", "광택 금속" };

    private sealed record Preset(string Name, string Swatch, string Light, float Brightness, float Ambient, float Angle, uint Filter, float Tint);

    // filter swatches of the effects panel (light colour + colour grade)
    private static readonly Preset[] Presets =
    {
        new("없음", "#DDE1E6", "#FFFFFF", 1f, 0.35f, 0, 0, 0),
        new("서리", "#E3E6F5", "#E6ECFF", 1.05f, 0.4f, 0, 4, 0.25f),
        new("벚꽃", "#F2DCE6", "#FFD6E5", 1.05f, 0.38f, 0, 0, 0.35f),
        new("라일락", "#E7D9EE", "#E3CCFF", 1f, 0.36f, 0, 0, 0.35f),
        new("새싹", "#DDF0D6", "#DFFFD0", 1.05f, 0.38f, 0, 0, 0.3f),
        new("모래", "#F1E3B9", "#FFF0C2", 1.05f, 0.38f, 0, 3, 0.3f),
        new("해돋이", "#F0C3B4", "#FFB27A", 1.05f, 0.35f, -60, 3, 0.45f),
        new("황금", "#F2B562", "#FFC266", 1.1f, 0.35f, -30, 3, 0.5f),
        new("안개", "#D5DCD9", "#F2F2F2", 1.1f, 0.55f, 0, 0, 0.1f),
        new("민트", "#CFEDE4", "#C8FFE9", 1f, 0.38f, 0, 0, 0.35f),
        new("하늘", "#D6EEF2", "#CDEFFF", 1.05f, 0.4f, 0, 4, 0.3f),
        new("석양", "#9C93B8", "#B59CFF", 0.9f, 0.3f, 50, 0, 0.45f),
        new("숲", "#96B79A", "#9FD8A0", 0.9f, 0.32f, 0, 0, 0.45f),
        new("돌", "#7F9399", "#C9D6DB", 0.9f, 0.3f, 0, 1, 0.2f),
        new("흑백", "#888B8E", "#FFFFFF", 1f, 0.35f, 0, 1, 0),
        new("세피아", "#8F7867", "#FFFFFF", 1f, 0.35f, 0, 2, 0),
        new("노을", "#F08BA2", "#FF7A9A", 0.95f, 0.3f, 60, 0, 0.5f),
        new("밤", "#1D1A2B", "#6E8BFF", 0.6f, 0.2f, 0, 4, 0.6f),
        new("심해", "#1D1F6E", "#5A6BFF", 0.7f, 0.25f, 0, 4, 0.55f),
        new("느와르", "#22272B", "#FFFFFF", 1.1f, 0.25f, 30, 9, 0),
        new("빈티지", "#A0402F", "#FFE0B0", 1f, 0.35f, 0, 8, 0.2f),
        new("비네트", "#5E6A63", "#FFFFFF", 1f, 0.35f, 0, 7, 0),
    };

    private readonly Engine _engine = new(1920, 1080);
    private readonly ToolState S = new();
    private readonly PaintSurface _surface;
    private readonly ContentControl _panel = new();
    private readonly Border _side = new();
    private readonly ScrollViewer _sideScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<Tool, FlatButton> _tabs = new();
    private readonly List<Action> _colorWatchers = new();
    private readonly FlatButton _undo, _redo, _history, _paste;
    private FlatButton _selectBtn = null!, _cropBtn = null!, _view3dBtn = null!;
    private readonly TextBlock _zoomText = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 13 };
    private readonly TextBlock _titleText = new() { Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _zoomSlider = new() { Minimum = 5, Maximum = 800, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _menu = new() { IsVisible = false };
    private readonly ContentControl _menuContent = new();
    private readonly Dictionary<string, Border> _menuItems = new();
    private readonly Grid _overlay = new() { IsVisible = false };
    private Control _maxIcon = null!;
    private FlatButton _maxBtn = null!;
    private bool _syncingZoom, _dirty, _forceClose, _editColor, _panelHidden;
    private string? _path;
    private int _stickerTab;
    private string? _libraryCategory;
    private string _libraryQuery = "";
    private Tool _beforeLibrary = Tool.Shape3D;
    private uint _clipObject;

    private static List<string>? _fonts;
    private static readonly Dictionary<string, IImage> ThumbCache = new();
    private static readonly List<Color> CustomColors = new();

    private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "openpaint3d");
    private static string StickerDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openpaint3d", "stickers");

    public MainWindow(string[] args)
    {
        Width = 1400;
        Height = 880;
        MinWidth = 1000;
        MinHeight = 620;
        Background = P3.Pane;
        Icon = LoadIcon();
        // the dark title strip is part of the app bar, so the window draws its own caption
        SystemDecorations = SystemDecorations.None;

        _fonts ??= FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n).ToList();
        S.Font = _fonts.FirstOrDefault(f => f == "Noto Sans CJK KR") ?? _fonts.FirstOrDefault(f => f.Contains("Nanum")) ?? "";

        _surface = new PaintSurface(_engine, S);
        _surface.Changed += () => { _dirty = true; UpdateState(); };
        _surface.ZoomChanged += UpdateZoom;
        _surface.ColorPicked += c => { S.Picking = false; SetColor(c); };
        _surface.SelectionChanged += () => Dispatcher.UIThread.Post(ShowPanel);

        _paste = TopButton("붙여넣기", Icons.Paste, () => _ = Paste());
        _undo = TopButton("실행 취소", Icons.Undo, Undo);
        _history = TopButton("기록", Icons.History, ShowHistory);
        _redo = TopButton("다시 실행", Icons.Redo, Redo);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        left.Children.Add(BuildSubBar());
        var area = new Grid { Children = { _surface } };
        Grid.SetRow(area, 1);
        left.Children.Add(area);
        body.Children.Add(left);
        _side.Background = P3.Pane;
        _side.BorderBrush = Line;
        _side.BorderThickness = new Thickness(1, 0, 0, 0);
        _sideScroll.Content = _panel;
        _side.Child = _sideScroll;
        Grid.SetColumn(_side, 1);
        body.Children.Add(_side);

        var main = new DockPanel();
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(header);
        main.Children.Add(body);

        BuildMenu();
        Content = new Grid { Children = { main, _menu, _overlay, BuildResizeGrips() } };

        KeyDown += OnKey;
        Closing += OnClosing;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateMaximizeIcon();
            if (e.Property == BoundsProperty && S.Tool == Tool.Library) _side.Width = SideWidth();
        };
        SelectTab(Tool.Brush);
        UpdateState();
        UpdateZoom();

        if (args.Length > 0 && File.Exists(args[0]))
            Opened += (_, _) => Dispatcher.UIThread.Post(() => OpenPath(args[0]), DispatcherPriority.Background);
        else if (ShowWelcomeSetting)
            ShowWelcome();
    }

    private static WindowIcon? LoadIcon()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "openpaint3d.png");
        return File.Exists(p) ? new WindowIcon(p) : null;
    }

    // ------------------------------------------------------------ window chrome

    private Control BuildHeader()
    {
        // caption strip: icon + title, drag to move, double-click to maximise
        var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 32, Background = Brushes.Transparent };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "openpaint3d.png");
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        if (File.Exists(iconPath)) titleRow.Children.Add(new Image { Source = new Bitmap(iconPath), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(_titleText);
        caption.Children.Add(titleRow);
        caption.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(caption).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(e);
        };
        FlatButton CaptionButton(string icon, string tip, Action a, IBrush? hover = null)
        {
            var b = new FlatButton { Width = 46, Height = 32, Hover = hover ?? P3.BarHover, Child = Icons.Make(icon, 10, Brushes.White) }.Init();
            ToolTip.SetTip(b, tip);
            b.Click += a;
            return b;
        }
        _maxBtn = CaptionButton(Icons.Maximize, "최대화", ToggleMaximize);
        _maxIcon = _maxBtn.Child!;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CaptionButton(Icons.Minimize, "최소화", () => WindowState = WindowState.Minimized),
                _maxBtn,
                CaptionButton(Icons.Close, "닫기", Close, new SolidColorBrush(Color.Parse("#E81123"))),
            },
        };
        Grid.SetColumn(buttons, 2);
        caption.Children.Add(buttons);

        // tool row
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 56 };
        var menu = TopButton("메뉴", Icons.Menu, () => ShowMenu("새로 만들기"));
        menu.Margin = new Thickness(6, 0, 0, 0);
        bar.Children.Add(menu);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        void Tab(Tool t, string label, string icon)
        {
            var b = TopButton(label, icon, () => SelectTab(t));
            b.SelectedBrush = P3.TabSelected;
            _tabs[t] = b;
            tabs.Children.Add(b);
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

        var collapse = new FlatButton { Width = 44, Hover = P3.BarHover, Child = Icons.Make(Icons.ChevronUp, 16, Brushes.White) }.Init();
        ToolTip.SetTip(collapse, "오른쪽 패널 숨기기/표시");
        collapse.Click += () =>
        {
            _panelHidden = !_panelHidden;
            collapse.Child = Icons.Make(_panelHidden ? "M5,9 L12,16 L19,9" : Icons.ChevronUp, 16, Brushes.White);
            ShowPanel();
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0), Children = { _paste, _undo, _history, _redo, collapse } };
        Grid.SetColumn(right, 2);
        bar.Children.Add(right);

        var stack = new StackPanel { Children = { caption, bar } };
        return new Border { Background = P3.Bar, Child = stack };
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateMaximizeIcon()
    {
        if (_maxBtn == null) return;
        bool max = WindowState == WindowState.Maximized;
        _maxBtn.Child = Icons.Make(max ? Icons.Restore : Icons.Maximize, 10, Brushes.White);
        ToolTip.SetTip(_maxBtn, max ? "이전 크기로 복원" : "최대화");
    }

    /// <summary>Thin borders along the window edges for resizing (the window has no system frame).</summary>
    private Control BuildResizeGrips()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("6,*,6"), RowDefinitions = new RowDefinitions("5,*,6") };
        void Grip(int col, int row, WindowEdge edge, StandardCursorType cursor)
        {
            var b = new Border { Background = Brushes.Transparent, Cursor = new Cursor(cursor) };
            b.PointerPressed += (_, e) =>
            {
                if (WindowState == WindowState.Normal && e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) BeginResizeDrag(edge, e);
            };
            Grid.SetColumn(b, col);
            Grid.SetRow(b, row);
            g.Children.Add(b);
        }
        Grip(0, 0, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner);
        Grip(1, 0, WindowEdge.North, StandardCursorType.TopSide);
        Grip(2, 0, WindowEdge.NorthEast, StandardCursorType.TopRightCorner);
        Grip(0, 1, WindowEdge.West, StandardCursorType.LeftSide);
        Grip(2, 1, WindowEdge.East, StandardCursorType.RightSide);
        Grip(0, 2, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner);
        Grip(1, 2, WindowEdge.South, StandardCursorType.BottomSide);
        Grip(2, 2, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner);
        // the middle cell must not swallow clicks
        g.IsHitTestVisible = true;
        return g;
    }

    private static FlatButton TopButton(string label, string icon, Action click)
    {
        var b = new FlatButton
        {
            MinWidth = 64,
            Padding = new Thickness(8, 0),
            Hover = P3.BarHover,
            Child = new StackPanel
            {
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Icons.Make(icon, 20, Brushes.White),
                    new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White },
                },
            },
        }.Init();
        b.Click += click;
        return b;
    }

    // --------------------------------------------------------------- sub bar

    private static FlatButton SubButton(string label, string icon, Action click, bool enabled = true, string? tip = null)
    {
        var b = new FlatButton
        {
            Padding = new Thickness(14, 0),
            Hover = P3.PaneHover,
            SelectedBrush = P3.SubSelected,
            IsEnabled = enabled,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Icons.Make(icon, 18, P3.Ink),
                    new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Dark },
                },
            },
        }.Init();
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += click;
        return b;
    }

    private static FlatButton IconButton(string icon, string tip, Action click, double width = 40)
    {
        var b = new FlatButton { Width = width, Hover = P3.PaneHover, Child = Icons.Make(icon, 18, P3.Ink) }.Init();
        ToolTip.SetTip(b, tip);
        b.Click += click;
        return b;
    }

    private Control BuildSubBar()
    {
        _selectBtn = SubButton("선택", Icons.Select, () => SelectTab(Tool.Select), tip: "3D 개체를 선택해 옮기고 회전합니다");
        _cropBtn = SubButton("자르기", Icons.Crop, () =>
        {
            SelectTab(Tool.Canvas);
            _surface.BeginCrop();
            ShowPanel();
        });
        var magic = SubButton("매직 선택", Icons.MagicSelect, () => { }, false, "OpenPaint 3D에서는 지원하지 않습니다");
        var mixed = SubButton("혼합 현실", Icons.MixedReality, () => { }, false, "OpenPaint 3D에서는 지원하지 않습니다");
        _view3dBtn = SubButton("3D 보기", Icons.Plane3D, () =>
        {
            _surface.SetView3D(!S.View3D);
            ShowPanel();
        }, tip: "장면 전체를 입체로 돌려 봅니다. 드래그: 회전, 휠: 확대/축소");

        _zoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_syncingZoom) _surface.SetZoom(_zoomSlider.Value / 100);
        };
        var more = IconButton(Icons.More, "자세히", () => { }, 44);
        var moreMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void MoreItem(string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => a();
            moreMenu.Items.Add(mi);
        }
        MoreItem("화면에 맞추기", () => _surface.FitToView());
        MoreItem("실제 크기(100%)", () => _surface.SetZoom(1));
        MoreItem("3D 보기 재설정", () => _surface.Reset3DView());
        FlyoutBase.SetAttachedFlyout(more, moreMenu);
        more.Click += () => FlyoutBase.ShowAttachedFlyout(more);

        var percent = new Border
        {
            Width = 64,
            BorderBrush = Line,
            BorderThickness = new Thickness(1, 0),
            Margin = new Thickness(6, 8),
            Child = _zoomText,
        };
        var leftItems = new StackPanel { Orientation = Orientation.Horizontal, Children = { _selectBtn, _cropBtn, magic } };
        var rightItems = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                mixed, _view3dBtn,
                IconButton(Icons.Minus, "축소", () => _surface.SetZoom(_surface.Zoom / 1.25)),
                _zoomSlider,
                IconButton(Icons.Plus, "확대", () => _surface.SetZoom(_surface.Zoom * 1.25)),
                percent, more,
            },
        };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 48, Children = { leftItems } };
        Grid.SetColumn(rightItems, 2);
        bar.Children.Add(rightItems);
        return new Border { Background = P3.Pane, Child = bar, ClipToBounds = true };
    }

    // ------------------------------------------------------------------- tabs

    private void SelectTab(Tool t)
    {
        _surface.CommitPending();
        if (S.CropMode) _surface.EndCrop(false);
        if (S.View3D) _surface.SetView3D(false);
        if (t is not (Tool.Shape3D or Tool.Library or Tool.Effects or Tool.Select)) _surface.Select(0);
        if (t == Tool.Library && S.Tool != Tool.Library) _beforeLibrary = S.Tool;
        if (t == Tool.Library) _libraryCategory = null;
        S.Tool = t;
        S.Picking = false;
        _editColor = false;
        foreach (var (tool, b) in _tabs) b.IsSelected = tool == t;
        _surface.Redraw();
        ShowPanel();
    }

    private double SideWidth() => S.Tool == Tool.Library && !S.View3D ? Math.Max(470, Bounds.Width * 0.4) : 300;

    private void ShowPanel()
    {
        _colorWatchers.Clear();
        _selectBtn.IsSelected = S.Tool == Tool.Select;
        _cropBtn.IsSelected = S.CropMode;
        _view3dBtn.IsSelected = S.View3D;
        _side.IsVisible = !_panelHidden;
        _side.Width = SideWidth();
        var p = new StackPanel { Margin = new Thickness(18, 14, 18, 24), Spacing = 6 };
        if (S.View3D)
        {
            p.Children.Add(Header("3D 보기"));
            p.Children.Add(Hint("캔버스와 3D 개체를 입체로 둘러봅니다.\n\n• 드래그: 장면 회전\n• 마우스 휠: 확대/축소\n• 자세히(…) > 3D 보기 재설정: 처음 각도로\n\n편집하려면 위쪽의 '3D 보기'를 다시 누르세요."));
            _panel.Content = p;
            return;
        }
        bool objectTools = S.Tool is Tool.Shape3D or Tool.Library or Tool.Select;
        if (objectTools && _surface.Selected != 0) SelectionPanel(p);
        else
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
                case Tool.Select: SelectPanel(p); break;
            }
        _panel.Content = p;
    }

    // ------------------------------------------------------------ panel parts

    private static TextBlock Header(string t) => new() { Text = t, FontSize = 20, Foreground = P3.Title, Margin = new Thickness(0, 0, 0, 10) };
    private static TextBlock Section(string t) => new() { Text = t, FontSize = 13, Foreground = Dark, Margin = new Thickness(0, 14, 0, 4) };
    private static TextBlock Hint(string t) => new() { Text = t, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private static Border Divider() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 12, 0, 4) };

    /// <summary>Square icon tile: plain until hovered, solid blue when selected.</summary>
    private FlatButton IconTile(Func<bool, Control> content, string tip, bool selected, Action click, double size = 44)
    {
        var b = new FlatButton
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, 8, 8),
            Hover = P3.PaneHover,
            SelectedBrush = P3.TileSelected,
            Child = content(selected),
        };
        b.IsSelected = selected;
        b.Init();
        ToolTip.SetTip(b, tip);
        b.Click += () =>
        {
            click();
            ShowPanel();
        };
        return b;
    }

    private static Control Center(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    private static Image Img(IImage src, double size) => new() { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform };

    /// <summary>Bordered light box button (full width), Paint 3D panel style.</summary>
    private static FlatButton BoxButton(Control content, Action click, double height = 40, bool enabled = true, string? tip = null)
    {
        var b = new FlatButton
        {
            Height = height,
            Normal = P3.Box,
            Hover = P3.PaneHover,
            BorderBrush = P3.BoxBorder,
            BorderThickness = new Thickness(1),
            IsEnabled = enabled,
            Child = Center(content),
        }.Init();
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += click;
        return b;
    }

    private static FlatButton WideButton(string text, string? icon, Action click, bool enabled = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (icon != null) row.Children.Add(Icons.Make(icon, 18, P3.Ink));
        row.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Foreground = Dark });
        return BoxButton(row, click, 42, enabled);
    }

    /// <summary>Tall button with an icon above its label (3D selection panel).</summary>
    private static FlatButton TallButton(string text, string icon, Action click, bool enabled = true)
    {
        var col = new StackPanel { Spacing = 4, Children = { Center(Icons.Make(icon, 18, P3.Ink)), new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Dark } } };
        return BoxButton(col, click, 64, enabled, enabled ? null : "OpenPaint 3D에서는 지원하지 않습니다");
    }

    private static FlatButton SquareButton(string icon, string tip, Action click, bool enabled = true)
    {
        var b = BoxButton(Icons.Make(icon, 18, P3.Ink), click, 46, enabled, tip);
        b.Width = 46;
        b.Margin = new Thickness(0, 0, 8, 0);
        return b;
    }

    private static Control Row(params Control[] items)
    {
        var g = new UniformGrid { Columns = items.Length };
        for (int i = 0; i < items.Length; i++)
        {
            items[i].Margin = new Thickness(i == 0 ? 0 : 4, 0, i == items.Length - 1 ? 0 : 4, 0);
            g.Children.Add(items[i]);
        }
        return g;
    }

    private static Control SquareRow(params Control[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.AddRange(items);
        return sp;
    }

    /// <summary>Label with an editable value box on the right and a slider underneath.</summary>
    private static Control ValueRow(string label, double min, double max, double value, Action<double> set, string unit, Action? done = null)
    {
        string Fmt(double v) => $"{(int)Math.Round(v)}{unit}";
        var box = new TextBox
        {
            Text = Fmt(value),
            Width = 84,
            Height = 32,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = P3.Box,
            BorderBrush = P3.BoxBorder,
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        head.Children.Add(new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Dark });
        Grid.SetColumn(box, 1);
        head.Children.Add(box);
        var s = new Slider { Minimum = min, Maximum = max, Value = value };
        s.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            set(s.Value);
            box.Text = Fmt(s.Value);
        };
        void FromBox()
        {
            var digits = new string((box.Text ?? "").Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                s.Value = Math.Clamp(v, min, max);
                done?.Invoke();
            }
            box.Text = Fmt(s.Value);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) FromBox(); };
        box.LostFocus += (_, _) => FromBox();
        if (done != null) s.AddHandler(PointerReleasedEvent, (_, _) => done(), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        return new StackPanel { Children = { head, s } };
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> set)
    {
        var c = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Height = 42,
            Background = P3.Box,
            BorderBrush = P3.BoxBorder,
            Margin = new Thickness(0, 8, 0, 0),
        };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(c.SelectedIndex); };
        return c;
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

    private Control FinishCombo()
    {
        int finish = _surface.Selected != 0 && _engine.Info(_surface.Selected) is { } info ? info.Finish : S.Finish;
        return Combo(Finishes, finish, SetFinish);
    }

    private void ShowOverlay(Control content)
    {
        _overlay.Children.Clear();
        _overlay.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) });
        _overlay.Children.Add(content);
        _overlay.IsVisible = true;
    }

    private void HideOverlay()
    {
        _overlay.IsVisible = false;
        _overlay.Children.Clear();
    }

    /// <summary>Open the "새 색 선택" dialog; <paramref name="keep"/> adds the result to the custom colours.</summary>
    private void PickColor(Color start, Action<Color> ok, bool keep = false)
    {
        var dlg = new ColorDialog(start);
        dlg.Done += c =>
        {
            HideOverlay();
            if (c is not { } col) return;
            if (keep)
            {
                CustomColors.Remove(col);
                CustomColors.Insert(0, col);
                if (CustomColors.Count > 4) CustomColors.RemoveAt(4);
            }
            ok(col);
            ShowPanel();
        };
        ShowOverlay(dlg);
    }

    /// <summary>Colour swatch button that opens the colour dialog.</summary>
    private Control SwatchButton(Func<Color> get, Action<Color> set, double width = 46)
    {
        var sw = new Border { Background = new SolidColorBrush(get()), BorderBrush = P3.BoxBorder, BorderThickness = new Thickness(1), Margin = new Thickness(6) };
        var b = new FlatButton { Width = width, Height = 42, Normal = P3.Box, BorderBrush = P3.BoxBorder, BorderThickness = new Thickness(1), Child = sw }.Init();
        ToolTip.SetTip(b, "색 선택");
        b.Click += () => PickColor(get(), c => { set(c); sw.Background = new SolidColorBrush(get()); });
        _colorWatchers.Add(() => sw.Background = new SolidColorBrush(get()));
        return b;
    }

    /// <summary>Current colour + eyedropper, 6x3 palette and the "색 추가" row.</summary>
    private Control ColorBlock()
    {
        var sp = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        var current = new Border { Height = 42, BorderBrush = P3.BoxBorder, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(current, "색 선택");
        current.PointerPressed += (_, _) => PickColor(S.Color, c => SetColor(c), true);
        var picker = new FlatButton
        {
            Width = 64,
            Height = 42,
            Normal = P3.Box,
            Hover = P3.PaneHover,
            SelectedBrush = P3.SubSelected,
            BorderBrush = P3.BoxBorder,
            BorderThickness = new Thickness(1),
            Child = Center(Icons.Make(Icons.Picker, 18, P3.Ink)),
        }.Init();
        ToolTip.SetTip(picker, "스포이드: 캔버스에서 색 가져오기");
        picker.Click += () =>
        {
            S.Picking = !S.Picking;
            picker.IsSelected = S.Picking;
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto"), Children = { current } };
        Grid.SetColumn(picker, 2);
        top.Children.Add(picker);
        sp.Children.Add(top);

        var grid = new UniformGrid { Columns = 6 };
        var cells = new List<(Border cell, Color c)>();
        foreach (var h in Palette)
        {
            var c = Color.Parse(h);
            var cell = new Border { Height = 36, Background = new SolidColorBrush(c), Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(cell, h);
            cell.PointerPressed += (_, _) => SetColor(c);
            cells.Add((cell, c));
            grid.Children.Add(cell);
        }
        sp.Children.Add(grid);

        var custom = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", CustomColors.Select(_ => "46").Append("*"))) };
        for (int i = 0; i < CustomColors.Count; i++)
        {
            var c = CustomColors[i];
            var cell = new Border { Height = 42, Background = new SolidColorBrush(c), Cursor = new Cursor(StandardCursorType.Hand) };
            cell.PointerPressed += (_, _) => SetColor(c);
            Grid.SetColumn(cell, i);
            custom.Children.Add(cell);
        }
        var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Icons.Make(Icons.Plus, 16, P3.Ink) } };
        if (CustomColors.Count < 2) addContent.Children.Add(new TextBlock { Text = "색 추가", FontSize = 14, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
        var add = BoxButton(addContent, () => PickColor(S.Color, c => SetColor(c), true), 42);
        Grid.SetColumn(add, CustomColors.Count);
        custom.Children.Add(add);
        sp.Children.Add(custom);

        void Sync()
        {
            current.Background = new SolidColorBrush(S.Color);
            picker.IsSelected = S.Picking;
            foreach (var (cell, c) in cells)
            {
                bool on = c == S.Color;
                cell.BorderBrush = on ? P3.Ink : null;
                cell.BorderThickness = new Thickness(on ? 2 : 0);
                cell.Child = on ? new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(1) } : null;
            }
        }
        _colorWatchers.Add(Sync);
        Sync();
        return sp;
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
        Thumb("model:" + key + ":" + size, size, size, e =>
        {
            double s = size * 0.78;
            e.AddObject(parts, new Vector3D(size / 2.0, size / 2.0, -s / 2), new Vector3D(s, s, s), 0, Color.Parse(color), finish, Label.None, -12, 28);
        }, true);

    // ----------------------------------------------------------------- panels

    private void BrushPanel(StackPanel p)
    {
        p.Children.Add(Header(S.Bucket ? "채우기" : BrushNames[S.Brush]));
        var grid = new WrapPanel { Margin = new Thickness(0, 2, -8, 0) };
        for (int i = 0; i < BrushNames.Length; i++)
        {
            int k = i;
            grid.Children.Add(IconTile(sel => Center(ColorIcons.Brush(k, 38, sel)), BrushNames[k], !S.Bucket && S.Brush == k,
                () => { S.Brush = k; S.Bucket = false; S.Picking = false; }));
        }
        grid.Children.Add(IconTile(sel => Center(ColorIcons.Brush(9, 38, sel)), "채우기", S.Bucket, () => { S.Bucket = true; S.Picking = false; }));
        p.Children.Add(grid);

        if (S.Bucket)
            p.Children.Add(ValueRow("허용 오차", 0, 255, S.Tolerance, v => S.Tolerance = (int)v, ""));
        else
        {
            p.Children.Add(ValueRow("두께", 1, 200, S.BrushSize, v => S.BrushSize = v, "px"));
            if (S.Brush != PaintTools.Eraser)
                p.Children.Add(ValueRow("불투명도", 1, 100, S.Opacity * 100, v => S.Opacity = v / 100, "%"));
        }
        if (S.Brush == PaintTools.Eraser && !S.Bucket)
        {
            p.Children.Add(Hint("지우개로 지운 부분은 투명해지며, 캔버스 배경 색이 비쳐 보입니다."));
            return;
        }
        if (!S.Bucket) p.Children.Add(FinishCombo());
        p.Children.Add(ColorBlock());
    }

    private void ShapePanel(StackPanel p)
    {
        if (_surface.Pending is ShapeItem or CurveItem)
        {
            ShapeEditPanel(p);
            return;
        }
        p.Children.Add(Header("2D 셰이프"));
        p.Children.Add(Section("선과 곡선"));
        var lines = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        for (int i = 0; i < Shapes2D.LineNames.Length; i++)
        {
            int k = i, n = k == 0 ? 2 : k + 2;
            lines.Children.Add(IconTile(sel => new GeometryView
            {
                Inset = 10,
                Ink = sel ? Brushes.White : P3.Ink,
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
            }, Shapes2D.LineNames[k], S.Line == k, () => S.Line = k));
        }
        p.Children.Add(lines);
        p.Children.Add(Section("2D 셰이프"));
        var grid = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        for (int i = 0; i < Shapes2D.Names.Length; i++)
        {
            int k = i;
            grid.Children.Add(IconTile(sel => new GeometryView { Inset = 12, Ink = sel ? Brushes.White : P3.Ink, Build = r => Shapes2D.Build(k, r) },
                Shapes2D.Names[k], S.Line < 0 && S.Shape == k, () => { S.Shape = k; S.Line = -1; }));
        }
        p.Children.Add(grid);
    }

    private void ShapeEditPanel(StackPanel p)
    {
        var shape = _surface.Pending as ShapeItem;
        p.Children.Add(Header(shape != null ? Shapes2D.Names[shape.Kind] : Shapes2D.LineNames[Math.Max(0, S.Line)]));
        if (shape != null)
        {
            p.Children.Add(Section("채우기"));
            var fill = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*") };
            fill.Children.Add(SwatchButton(() => S.FillColor, c => { S.FillColor = c; if (S.FillMode == 0) S.FillMode = 1; _surface.SyncPending(); }));
            var mode = Combo(new[] { "없음", "단색", "그라데이션" }, S.FillMode, v => { S.FillMode = v; _surface.SyncPending(); ShowPanel(); });
            mode.Margin = new Thickness(0);
            Grid.SetColumn(mode, 2);
            fill.Children.Add(mode);
            p.Children.Add(fill);
            if (S.FillMode == 2)
            {
                var end = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*"), Margin = new Thickness(0, 6, 0, 0) };
                end.Children.Add(SwatchButton(() => S.FillColor2, c => { S.FillColor2 = c; _surface.SyncPending(); }));
                var lbl = new TextBlock { Text = "그라데이션 끝 색", VerticalAlignment = VerticalAlignment.Center, Foreground = Muted };
                Grid.SetColumn(lbl, 2);
                end.Children.Add(lbl);
                p.Children.Add(end);
            }
        }
        p.Children.Add(Section("선 종류"));
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*") };
        line.Children.Add(SwatchButton(() => S.Color, c => SetColor(c)));
        var type = Combo(new[] { "실선", "파선", "점선", "없음" }, S.LineType, v => { S.LineType = v; _surface.SyncPending(); });
        type.Margin = new Thickness(0);
        Grid.SetColumn(type, 2);
        line.Children.Add(type);
        p.Children.Add(line);
        p.Children.Add(ValueRow("두께", 0, 60, S.ShapeThickness, v => { S.ShapeThickness = v; _surface.SyncPending(); }, "px"));
        p.Children.Add(ValueRow("불투명도", 1, 100, S.ShapeOpacity * 100, v => { S.ShapeOpacity = v / 100; _surface.SyncPending(); }, "%"));
        p.Children.Add(FinishCombo());
        var make = BoxButton(new StackPanel
        {
            Spacing = 4,
            Children = { Center(ColorIcons.Make(30, ("M5,8 L12,4 L19,8 L12,12 Z", "#7FB2F5"), ("M5,8 V16 L12,20 V12 Z", "#4F8DF0"), ("M19,8 V16 L12,20 V12 Z", "#3466C9"))), new TextBlock { Text = "3D로 만들기", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Dark } },
        }, () => _surface.Make3D(), 70);
        make.Margin = new Thickness(0, 14, 0, 0);
        p.Children.Add(make);
        p.Children.Add(Section("회전 및 대칭"));
        p.Children.Add(SquareRow(
            SquareButton(Icons.RotateLeft, "왼쪽으로 회전", () => _surface.RotatePending(-90)),
            SquareButton(Icons.RotateRight, "오른쪽으로 회전", () => _surface.RotatePending(90)),
            SquareButton(Icons.FlipH, "좌우 대칭", () => _surface.FlipPending(true)),
            SquareButton(Icons.FlipV, "상하 대칭", () => _surface.FlipPending(false))));
    }

    private static readonly (string tip, (string, string?)[] layers)[] Doodles =
    {
        ("날카로운 가장자리", new (string, string?)[] { ("M3,15 L7,6 L14,4 L19,9 L16,17 L8,18 Z", "#5B8DEF"), ("M15,21 L21,15 L22.5,16.5 L16.5,22.5 Z", "#F6C443") }),
        ("부드러운 가장자리", new (string, string?)[] { ("M3,12 C3,6 8,3.5 12,4.5 C17,4.5 20,8 19,12 C18.5,16.5 14,18.5 10,18 C6,17.5 3,15.5 3,12 Z", "#5B8DEF"), ("M7,9.5 C8,7.5 10,6.8 12,7", null), ("M15,21 L21,15 L22.5,16.5 L16.5,22.5 Z", "#F6C443") }),
        ("튜브", new (string, string?)[] { ("M2.5,17 C2.5,9 8.5,12 10.5,7.5 C12.5,3 17.5,3.5 20,4.5 L19,7.5 C16.8,6.6 14.6,6.8 13.6,9.6 C11,15.5 5.6,12.4 5.6,17 Z", "#5B8DEF"), ("M15,21 L21,15 L22.5,16.5 L16.5,22.5 Z", "#F6C443") }),
    };

    private void ModelPanel(StackPanel p)
    {
        p.Children.Add(WideButton("3D 라이브러리 열기", Icons.Library, () => SelectTab(Tool.Library)));
        p.Children.Add(Section("3D 낙서"));
        var doodles = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        for (int i = 0; i < Doodles.Length; i++)
        {
            int k = i;
            doodles.Children.Add(IconTile(_ => Center(ColorIcons.Make(30, Doodles[k].layers)), Doodles[k].tip, S.Doodle == k, () => { S.Doodle = k; S.Model = null; }));
        }
        p.Children.Add(doodles);
        p.Children.Add(Section("3D 개체"));
        var basics = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        for (int i = 0; i < Library.BasicNames.Length; i++)
        {
            int k = i;
            var thumb = ModelThumb("basic" + k, new[] { new ModelPart(Library.BasicPrims[k], 0, 0, 0, 1, 1, 1) }, "#4F8EF7", 1, 64);
            basics.Children.Add(IconTile(_ => Center(Img(thumb, 34)), Library.BasicNames[k],
                S.Model == null && S.Doodle < 0 && S.Basic == k, () => { S.Basic = k; S.Model = null; S.Doodle = -1; }));
        }
        p.Children.Add(basics);
        p.Children.Add(Section("3D 모델"));
        var people = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        foreach (var m in Library.People)
        {
            var model = m;
            var thumb = ModelThumb(m.Name, m.Parts, m.Color, m.Finish, 64);
            people.Children.Add(IconTile(_ => Center(Img(thumb, 36)), m.Name, S.Model == model,
                () => { S.Model = model; S.Doodle = -1; S.Color = Color.Parse(model.Color); S.Finish = model.Finish; }));
        }
        p.Children.Add(people);
        p.Children.Add(FinishCombo());
        p.Children.Add(ColorBlock());
    }

    private void SelectPanel(StackPanel p)
    {
        p.Children.Add(Header("선택"));
        p.Children.Add(Hint("3D 개체를 클릭하면 선택됩니다. 선택한 개체는 끌어서 옮기고, 둘레의 손잡이로 회전·크기·깊이를 조정할 수 있습니다."));
    }

    private void SelectionPanel(StackPanel p)
    {
        uint id = _surface.Selected;
        p.Children.Add(Header("3D 선택"));
        var edit = WideButton("색 편집", Icons.Palette, () => { _editColor = !_editColor; ShowPanel(); });
        edit.IsSelected = _editColor;
        edit.SelectedBrush = P3.SubSelected;
        p.Children.Add(edit);
        if (_editColor)
        {
            if (_engine.Info(id) is { } info) S.Color = info.Color;
            p.Children.Add(FinishCombo());
            p.Children.Add(ColorBlock());
        }
        p.Children.Add(Section("선택"));
        p.Children.Add(Row(TallButton("모두 선택", Icons.SelectAll, () => { }, false), TallButton("다중 선택", Icons.MultiSelect, () => { }, false)));
        p.Children.Add(Section("그룹화"));
        p.Children.Add(Row(TallButton("그룹", Icons.Group, () => { }, false), TallButton("그룹 해제", Icons.Ungroup, () => { }, false)));
        p.Children.Add(Section("편집"));
        p.Children.Add(SquareRow(
            SquareButton(Icons.Cut, "잘라내기(지원하지 않음)", () => { }, false),
            SquareButton(Icons.Copy, "복사", () => { _clipObject = id; ShowPanel(); }),
            SquareButton(Icons.Paste, "붙여넣기", PasteObject, _clipObject != 0 && _engine.Info(_clipObject) != null),
            SquareButton(Icons.Trash, "삭제", () => _surface.DeleteSelection())));
        p.Children.Add(Section("회전 및 대칭"));
        void Turn(int axis, double deg, Label label)
        {
            _engine.Checkpoint(label);
            _engine.RotateObject(id, axis, deg);
            _surface.Refresh();
        }
        p.Children.Add(SquareRow(
            SquareButton(Icons.RotateLeft, "왼쪽으로 회전", () => Turn(2, -90, Label.Rotate)),
            SquareButton(Icons.RotateRight, "오른쪽으로 회전", () => Turn(2, 90, Label.Rotate)),
            SquareButton(Icons.FlipH, "좌우 대칭", () => Turn(1, 180, Label.Flip)),
            SquareButton(Icons.FlipV, "상하 대칭", () => Turn(0, 180, Label.Flip))));
    }

    private void PasteObject()
    {
        if (_clipObject == 0 || _engine.Info(_clipObject) == null) return;
        var id = _engine.DuplicateObject(_clipObject);
        _surface.Refresh();
        _surface.Select(id);
    }

    private void StickerPanel(StackPanel p)
    {
        p.Children.Add(Header("스티커"));
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        string[] names = { "스티커", "질감", "사용자 지정 스티커" };
        string[] icons = { Icons.StickerTab, Icons.TextureTab, Icons.CustomTab };
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            tabs.Children.Add(IconTile(sel => Center(Icons.Make(icons[k], 24, sel ? Brushes.White : P3.Ink)), names[k], _stickerTab == k, () => _stickerTab = k));
        }
        p.Children.Add(tabs);
        p.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4, 0, 12) });
        var grid = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        switch (_stickerTab)
        {
            case 0:
                for (int i = 0; i < Stickers.Names.Length; i++)
                {
                    int k = i;
                    grid.Children.Add(IconTile(_ => new StickerView { Kind = k, Margin = new Thickness(5) }, Stickers.Names[k],
                        S.Texture < 0 && S.Custom == null && S.Sticker == k, () => { S.Sticker = k; S.Texture = -1; S.Custom = null; }, 52));
                }
                break;
            case 1:
                for (int i = 0; i < Textures.Names.Length; i++)
                {
                    int k = i;
                    grid.Children.Add(IconTile(_ => new Image { Source = Textures.Get(k), Stretch = Stretch.UniformToFill, Margin = new Thickness(5) }, Textures.Names[k],
                        S.Texture == k, () => { S.Texture = k; S.Custom = null; }, 52));
                }
                break;
            default:
                foreach (var f in CustomStickers())
                {
                    IImage? img;
                    try { img = new Bitmap(f); } catch { continue; }
                    var bitmap = img;
                    grid.Children.Add(IconTile(_ => new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(5) }, Path.GetFileName(f),
                        S.Custom != null && ReferenceEquals(S.Custom, bitmap), () => { S.Custom = bitmap; S.Texture = -1; }, 52));
                }
                break;
        }
        p.Children.Add(grid);
        if (_stickerTab == 2) p.Children.Add(WideButton("스티커 추가", Icons.Plus, () => _ = AddCustomSticker()));
        p.Children.Add(ValueRow("불투명도", 1, 100, S.StickerOpacity * 100, v => { S.StickerOpacity = v / 100; _surface.SyncPending(); }, "%"));
        if (_surface.Pending is ImageItem)
        {
            p.Children.Add(Section("회전 및 대칭"));
            p.Children.Add(SquareRow(
                SquareButton(Icons.RotateLeft, "왼쪽으로 회전", () => _surface.RotatePending(-90)),
                SquareButton(Icons.RotateRight, "오른쪽으로 회전", () => _surface.RotatePending(90)),
                SquareButton(Icons.FlipH, "좌우 대칭", () => _surface.FlipPending(true)),
                SquareButton(Icons.FlipV, "상하 대칭", () => _surface.FlipPending(false))));
        }
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

    private static readonly int[] FontSizes = { 8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 60, 72, 96, 120, 160, 200, 300 };

    private void TextPanel(StackPanel p)
    {
        p.Children.Add(Header("텍스트"));
        var kinds = new WrapPanel { Margin = new Thickness(0, 0, -8, 0) };
        kinds.Children.Add(IconTile(sel => Center(Icons.Make(Icons.Text2D, 24, sel ? Brushes.White : P3.Ink)), "2D 텍스트", !S.Text3D,
            () => { S.Text3D = false; _surface.SyncPending(); }));
        kinds.Children.Add(IconTile(sel => Center(Icons.Make(Icons.Text3D, 24, sel ? Brushes.White : P3.Ink)), "3D 텍스트", S.Text3D,
            () => { S.Text3D = true; _surface.SyncPending(); }));
        p.Children.Add(kinds);
        p.Children.Add(Divider());

        var fonts = new ComboBox { ItemsSource = _fonts, SelectedItem = S.Font, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 400, Height = 42, Background = P3.Box, BorderBrush = P3.BoxBorder, Margin = new Thickness(0, 8, 0, 0) };
        fonts.SelectionChanged += (_, _) => { S.Font = fonts.SelectedItem as string ?? ""; _surface.SyncPending(); };
        p.Children.Add(fonts);
        var sizes = FontSizes.Select(s => s.ToString()).ToArray();
        int nearest = Array.IndexOf(FontSizes, FontSizes.OrderBy(s => Math.Abs(s - S.FontSize)).First());
        var sizeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        sizeRow.Children.Add(Combo(sizes, nearest, i => { S.FontSize = FontSizes[i]; _surface.SyncPending(); }));
        var sw = SwatchButton(() => S.Color, c => SetColor(c), 64);
        sw.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetColumn(sw, 2);
        sizeRow.Children.Add(sw);
        p.Children.Add(sizeRow);

        FlatButton Toggle(Control content, bool on, string tip, Action<bool> set)
        {
            var b = new FlatButton
            {
                Width = 38,
                Height = 40,
                Normal = P3.Box,
                Hover = P3.PaneHover,
                SelectedBrush = P3.SubSelected,
                BorderBrush = P3.BoxBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 3, 0),
                Child = Center(content),
            };
            b.IsSelected = on;
            b.Init();
            ToolTip.SetTip(b, tip);
            b.Click += () =>
            {
                b.IsSelected = !b.IsSelected;
                set(b.IsSelected);
                _surface.SyncPending();
            };
            return b;
        }
        var style = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        style.Children.Add(Toggle(new TextBlock { Text = "B", FontWeight = FontWeight.Bold, FontSize = 15 }, S.Bold, "굵게", v => S.Bold = v));
        style.Children.Add(Toggle(new TextBlock { Text = "I", FontStyle = FontStyle.Italic, FontSize = 15 }, S.Italic, "기울임꼴", v => S.Italic = v));
        style.Children.Add(Toggle(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline, FontSize = 15 }, S.Underline, "밑줄", v => S.Underline = v));
        style.Children.Add(new Border { Width = 8 });
        var aligns = new[] { (TextAlignment.Left, "M4,6 H20 M4,10 H14 M4,14 H20 M4,18 H14", "왼쪽 맞춤"), (TextAlignment.Center, "M4,6 H20 M7,10 H17 M4,14 H20 M7,18 H17", "가운데 맞춤"), (TextAlignment.Right, "M4,6 H20 M10,10 H20 M4,14 H20 M10,18 H20", "오른쪽 맞춤") };
        foreach (var (al, icon, tip) in aligns)
        {
            var a = al;
            style.Children.Add(Toggle(Icons.Make(icon, 16, P3.Ink), S.Align == a, tip, _ => { S.Align = a; ShowPanel(); }));
        }
        p.Children.Add(style);
        if (S.Text3D) p.Children.Add(FinishCombo());
        p.Children.Add(ColorBlock());
    }

    private void EffectPanel(StackPanel p)
    {
        var v = _engine.View;
        p.Children.Add(Header("효과"));
        var current = Presets.FirstOrDefault(pr => Engine.Pack(Color.Parse(pr.Light)) == v.LightColor && pr.Filter == v.Filter && Math.Abs(pr.Brightness - v.Brightness) < 0.01);
        p.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run("필터 :  ") { Foreground = Dark },
                new Avalonia.Controls.Documents.Run(current?.Name ?? "사용자 지정") { FontWeight = FontWeight.SemiBold, Foreground = Dark },
            },
        });
        var grid = new WrapPanel { Margin = new Thickness(0, 0, -6, 0) };
        foreach (var preset in Presets)
        {
            var pr = preset;
            bool on = pr == current;
            var circle = new Ellipse
            {
                Width = 32,
                Height = 32,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse(pr.Swatch), 0), new GradientStop(Blend(Color.Parse(pr.Swatch), Colors.Black, 0.12), 1) },
                },
                Stroke = on ? P3.TileSelected : P3.Ink,
                StrokeThickness = on ? 3 : 1.2,
            };
            var b = new FlatButton { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Margin = new Thickness(0, 0, 6, 6), Child = Center(circle) }.Init();
            ToolTip.SetTip(b, pr.Name);
            b.Click += () =>
            {
                _engine.SetView(ToView(pr, _engine.View), Label.Effect);
                _surface.Refresh();
                ShowPanel();
            };
            grid.Children.Add(b);
        }
        p.Children.Add(grid);

        p.Children.Add(Section("조명 휠"));
        var wheel = new LightWheel
        {
            Width = 210,
            Height = 210,
            HorizontalAlignment = HorizontalAlignment.Center,
            Angle = ((v.LightAngle % 360) + 360) % 360,
            DiscColor = Engine.Unpack(v.LightColor),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(wheel, "해를 둘레를 따라 끌어 조명 방향을 바꿉니다. 가운데를 누르면 조명 색을 고릅니다.");
        bool started = false;
        wheel.AngleChanged += (a, final) =>
        {
            var nv = _engine.View;
            nv.LightAngle = (float)a;
            _engine.SetView(nv, started ? Label.None : Label.Effect);
            started = !final;
            _surface.Refresh(final, !final);
        };
        wheel.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var pos = e.GetPosition(wheel);
            var d = pos - new Point(wheel.Bounds.Width / 2, wheel.Bounds.Height / 2);
            if (Math.Sqrt(d.X * d.X + d.Y * d.Y) < wheel.Bounds.Width * 0.25)
            {
                e.Handled = true;
                PickColor(Engine.Unpack(_engine.View.LightColor), c =>
                {
                    var nv = _engine.View;
                    nv.LightColor = Engine.Pack(c);
                    nv.Tint = Math.Max(nv.Tint, 0.35f);
                    _engine.SetView(nv, Label.Effect);
                    _surface.Refresh();
                });
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        p.Children.Add(wheel);
        Action commit = () => _surface.Refresh();
        void Edit(Func<ViewSettings, ViewSettings> f)
        {
            _engine.SetView(f(_engine.View));
            _surface.Refresh(false, true);
        }
        p.Children.Add(ValueRow("밝기", 30, 170, v.Brightness * 100, x => Edit(s => { s.Brightness = (float)(x / 100); return s; }), "%", commit));
        p.Children.Add(ValueRow("주변광", 0, 100, v.Ambient * 100, x => Edit(s => { s.Ambient = (float)(x / 100); return s; }), "%", commit));
    }

    private static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

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
        if (S.CropMode)
        {
            p.Children.Add(Header("자르기"));
            p.Children.Add(Hint("손잡이를 끌거나 영역을 드래그해 남길 부분을 정한 뒤 '완료'를 누르세요."));
            var done = WideButton("완료", null, () => { _surface.EndCrop(true); ShowPanel(); });
            var cancel = WideButton("취소", null, () => { _surface.EndCrop(false); ShowPanel(); });
            p.Children.Add(Row(done, cancel));
            return;
        }
        p.Children.Add(Header("캔버스"));
        ToggleSwitch Switch(bool on, Action<bool> set)
        {
            var t = new ToggleSwitch { IsChecked = on, OnContent = "켬", OffContent = "끔", Margin = new Thickness(0, -4, 0, 4) };
            t.IsCheckedChanged += (_, _) => set(t.IsChecked == true);
            return t;
        }
        p.Children.Add(new TextBlock { Text = "캔버스 표시", FontSize = 13, Foreground = Dark });
        p.Children.Add(Switch(!S.HideCanvas, on =>
        {
            S.HideCanvas = !on;
            _surface.Refresh(false);
        }));
        p.Children.Add(new TextBlock { Text = "투명 캔버스", FontSize = 13, Foreground = Dark, Margin = new Thickness(0, 6, 0, 0) });
        p.Children.Add(Switch(v.ShowBg == 0, on =>
        {
            var nv = _engine.View;
            nv.ShowBg = on ? 0 : 1;
            _engine.SetView(nv, Label.CanvasView);
            _surface.Refresh();
        }));
        p.Children.Add(Divider());

        p.Children.Add(Section("캔버스 크기 조정"));
        var unit = Combo(new[] { "픽셀", "백분율" }, 0, _ => { });
        TextBox Box() => new() { HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Height = 36, Background = Brushes.White, BorderBrush = P3.BoxBorder };
        var wBox = Box();
        var hBox = Box();
        var lockRatio = new CheckBox { Content = "가로 세로 비율 잠금", IsChecked = true };
        var scale = new CheckBox { Content = "캔버스와 함께 이미지 크기 조정", IsChecked = false };
        double ratio = (double)_engine.Width / _engine.Height;
        bool syncing = false;
        bool Percent() => unit.SelectedIndex == 1;
        string U() => Percent() ? "%" : "px";
        double Num(TextBox b) => double.TryParse(new string((b.Text ?? "").Where(char.IsDigit).ToArray()), out var x) ? x : 0;
        void ShowValues()
        {
            syncing = true;
            wBox.Text = $"{(Percent() ? 100 : _engine.Width)}{U()}";
            hBox.Text = $"{(Percent() ? 100 : _engine.Height)}{U()}";
            syncing = false;
        }
        unit.SelectionChanged += (_, _) => ShowValues();
        void Linked(TextBox from, TextBox to, bool widthChanged)
        {
            if (syncing || lockRatio.IsChecked != true) return;
            var x = Num(from);
            if (x <= 0) return;
            syncing = true;
            to.Text = $"{Math.Max(1, Math.Round(Percent() ? x : widthChanged ? x / ratio : x * ratio))}{U()}";
            syncing = false;
        }
        void Apply()
        {
            double w = Num(wBox), h = Num(hBox);
            if (w <= 0 || h <= 0) { ShowValues(); return; }
            if (Percent()) { w = _engine.Width * w / 100; h = _engine.Height * h / 100; }
            int iw = (int)Math.Clamp(Math.Round(w), 1, 16384), ih = (int)Math.Clamp(Math.Round(h), 1, 16384);
            if (iw == _engine.Width && ih == _engine.Height) { ShowValues(); return; }
            _engine.Resize(iw, ih, scale.IsChecked == true);
            _surface.Refresh();
            _surface.FitToView();
            ShowPanel();
        }
        wBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Linked(wBox, hBox, true); Apply(); } };
        hBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Linked(hBox, wBox, false); Apply(); } };
        wBox.LostFocus += (_, _) => Linked(wBox, hBox, true);
        hBox.LostFocus += (_, _) => Linked(hBox, wBox, false);
        ShowValues();
        var sizes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        sizes.Children.Add(new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "너비", FontSize = 12, Foreground = Dark }, wBox } });
        var hp = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "높이", FontSize = 12, Foreground = Dark }, hBox } };
        Grid.SetColumn(hp, 2);
        sizes.Children.Add(hp);
        p.Children.Add(sizes);
        p.Children.Add(lockRatio);
        p.Children.Add(scale);
        p.Children.Add(unit);
        var apply = WideButton("크기 적용", null, Apply);
        apply.Margin = new Thickness(0, 8, 0, 0);
        p.Children.Add(apply);

        p.Children.Add(Section("회전 및 대칭"));
        void Op(Action a)
        {
            a();
            _surface.Refresh();
            _surface.FitToView();
            ShowPanel();
        }
        p.Children.Add(SquareRow(
            SquareButton(Icons.RotateLeft, "왼쪽으로 회전", () => Op(() => _engine.Rotate(false))),
            SquareButton(Icons.RotateRight, "오른쪽으로 회전", () => Op(() => _engine.Rotate(true))),
            SquareButton(Icons.FlipH, "좌우 대칭", () => Op(() => _engine.Flip(true))),
            SquareButton(Icons.FlipV, "상하 대칭", () => Op(() => _engine.Flip(false)))));

        p.Children.Add(Section("캔버스 색"));
        var colors = new UniformGrid { Columns = 6 };
        foreach (var h in Palette)
        {
            var c = Color.Parse(h);
            var cell = new Border { Height = 28, Background = new SolidColorBrush(c), Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(cell, h);
            cell.PointerPressed += (_, _) =>
            {
                var nv = _engine.View;
                nv.Bg = Engine.Pack(c);
                nv.ShowBg = 1;
                _engine.SetView(nv, Label.CanvasView);
                _surface.Refresh();
                ShowPanel();
            };
            colors.Children.Add(cell);
        }
        p.Children.Add(colors);
    }

    private void LibraryPanel(StackPanel p)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(-8, -4, 0, 10) };
        var back = new FlatButton { Width = 36, Height = 36, Child = Center(Icons.Make(Icons.Back, 18, P3.Text)) }.Init();
        ToolTip.SetTip(back, "뒤로");
        back.Click += () =>
        {
            if (_libraryCategory != null || _libraryQuery.Length > 0)
            {
                _libraryCategory = null;
                _libraryQuery = "";
                ShowPanel();
            }
            else SelectTab(_beforeLibrary == Tool.Library ? Tool.Shape3D : _beforeLibrary);
        };
        head.Children.Add(back);
        head.Children.Add(new TextBlock { Text = _libraryCategory ?? "3D 라이브러리", FontSize = 20, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(head);

        var search = new TextBox { Text = _libraryQuery, Watermark = "검색", Height = 38, VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.White, BorderBrush = P3.BoxBorder };
        var results = new WrapPanel { Margin = new Thickness(0, 12, -10, 0) };
        double tileW = Math.Floor((SideWidth() - 36 - 1 - 20) / 3);
        Control Tile(IImage thumb, string name, bool chevron, Action click)
        {
            var label = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = name, FontSize = 14, Foreground = Dark, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            if (chevron)
            {
                var ch = Icons.Make(Icons.ChevronRight, 16, P3.Text);
                Grid.SetColumn(ch, 1);
                label.Children.Add(ch);
            }
            var art = new Border
            {
                Height = tileW,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#E4E6E7"), 0), new GradientStop(Color.Parse("#C9CCCD"), 1) },
                },
                Child = Center(Img(thumb, tileW * 0.72)),
            };
            var body = new DockPanel { Children = { art } };
            DockPanel.SetDock(art, Dock.Top);
            body.Children.Add(label);
            var b = new FlatButton { Width = tileW, Height = tileW + 52, Normal = Brushes.White, Hover = P3.PaneHover, Margin = new Thickness(0, 0, 10, 10), Child = body }.Init();
            ToolTip.SetTip(b, name);
            b.Click += click;
            return b;
        }
        void Fill()
        {
            results.Children.Clear();
            if (_libraryCategory == null && _libraryQuery.Length == 0)
            {
                foreach (var c in Library.Categories)
                {
                    var cat = c;
                    var first = Library.Search(cat, "").First();
                    results.Children.Add(Tile(ModelThumb(first.Name, first.Parts, first.Color, first.Finish, 160), cat, true, () => { _libraryCategory = cat; ShowPanel(); }));
                }
                return;
            }
            foreach (var m in Library.Search(_libraryCategory, _libraryQuery))
            {
                var model = m;
                results.Children.Add(Tile(ModelThumb(m.Name, m.Parts, m.Color, m.Finish, 160), m.Name, false, () =>
                {
                    _surface.PlaceModel(model);
                    ShowPanel();
                }));
            }
            if (results.Children.Count == 0) results.Children.Add(Hint("검색 결과가 없습니다."));
        }
        search.TextChanged += (_, _) => { _libraryQuery = search.Text ?? ""; Fill(); };
        p.Children.Add(search);
        Fill();
        p.Children.Add(results);
        p.Children.Add(Hint("라이브러리 모델은 OpenPaint 3D가 기본 도형을 조합해 만든 자체 모델입니다(온라인 Remix 3D 라이브러리는 없습니다)."));
    }

    // ---------------------------------------------------------------- welcome

    private static string SettingsFile => Path.Combine(ConfigDir, "settings");

    private static bool ShowWelcomeSetting
    {
        get
        {
            try { return !File.Exists(SettingsFile) || !File.ReadAllLines(SettingsFile).Contains("welcome=0"); }
            catch (IOException) { return true; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(SettingsFile, value ? "welcome=1\n" : "welcome=0\n");
            }
            catch (IOException) { }
        }
    }

    private void ShowWelcome()
    {
        Control BigTile(string title, string icon, Action click)
        {
            var b = new FlatButton
            {
                Width = 200,
                Height = 150,
                Normal = Brushes.White,
                Hover = AccentLight,
                Margin = new Thickness(0, 0, 14, 0),
                Child = new StackPanel
                {
                    Spacing = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Center(Icons.Make(icon, 40, P3.Title)), new TextBlock { Text = title, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Dark } },
                },
            }.Init();
            b.Click += () =>
            {
                HideOverlay();
                click();
            };
            return b;
        }
        var show = new CheckBox { Content = "시작할 때 이 화면 표시", IsChecked = true };
        show.IsCheckedChanged += (_, _) => ShowWelcomeSetting = show.IsChecked == true;
        var close = ColorDialog.DialogButton("닫기", HideOverlay);
        close.Width = 110;
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 28, 0, 0), Children = { show } };
        Grid.SetColumn(close, 1);
        bottom.Children.Add(close);
        var card = new Border
        {
            Background = P3.Pane,
            Padding = new Thickness(36, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 10 40 0 #60000000"),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "OpenPaint 3D 시작", FontSize = 28, FontWeight = FontWeight.Light, Foreground = Dark },
                    new TextBlock { Text = "새 프로젝트를 만들거나 그림을 열어 시작하세요.", FontSize = 13, Foreground = Muted, Margin = new Thickness(0, 6, 0, 22) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            BigTile("새로 만들기", Icons.New, () => { }),
                            BigTile("열기", Icons.Open, () => _ = OpenFile()),
                            BigTile("붙여넣기", Icons.Paste, () => _ = Paste()),
                        },
                    },
                    bottom,
                },
            },
        };
        ShowOverlay(card);
    }

    // ---------------------------------------------------------------- history

    private void ShowHistory()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Content = BuildHistory(flyout);
        FlyoutBase.SetAttachedFlyout(_history, flyout);
        FlyoutBase.ShowAttachedFlyout(_history);
    }

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
        _titleText.Text = Title;
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
        if (_overlay.IsVisible)
        {
            if (e.Key == Key.Escape) { HideOverlay(); e.Handled = true; }
            return;
        }
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
            case Key.C when ctrl && _surface.Selected != 0: _clipObject = _surface.Selected; ShowPanel(); break;
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
        Item("설정", Icons.Settings);
        Item("정보", Icons.Info);
        Item("끝내기", Icons.Exit);
        var left = new Border { Width = 280, Background = P3.Bar, Child = nav };
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
            row.Background = name == page ? P3.TabSelected : Brushes.Transparent;
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
                        Icons.Make(icon, 34, P3.Title),
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
            case "설정":
                p.Children.Add(Title("설정"));
                var welcome = new ToggleSwitch { IsChecked = ShowWelcomeSetting, OnContent = "켬", OffContent = "끔" };
                welcome.IsCheckedChanged += (_, _) => ShowWelcomeSetting = welcome.IsChecked == true;
                p.Children.Add(new TextBlock { Text = "시작 화면 표시", FontSize = 14 });
                p.Children.Add(welcome);
                break;
            case "정보":
                p.Children.Add(Title("정보"));
                p.Children.Add(new TextBlock
                {
                    Text = "OpenPaint 3D 0.3.0\n\nPaint 3D의 화면 구성과 기능 설명을 참고해 C#(Avalonia)과 Rust로 새로 만든 오픈소스 그림판입니다.\n" +
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
