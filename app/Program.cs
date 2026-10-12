using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SkiaSharp;

namespace OpenPaint3D;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-icon")
        {
            ExportIcon(args[1]);
            return 0;
        }
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Draw the application icon (a glossy, tilted paint drop) and save it as PNG.</summary>
    private static void ExportIcon(string path)
    {
        const int n = 256;
        using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);

        // soft ground shadow
        using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9) })
            c.DrawOval(new SKRect(58, 214, 206, 240), shadow);

        // paint drop body, tilted like a brush tip
        using var drop = SKPath.ParseSvgPathData(
            "M128,14 C150,52 204,104 204,150 C204,196 170,228 128,228 C86,228 52,196 52,150 C52,104 106,52 128,14 Z");
        c.Save();
        c.Translate(0, -4);
        using (var body = new SKPaint { IsAntialias = true })
        {
            body.Shader = SKShader.CreateLinearGradient(new SKPoint(60, 20), new SKPoint(200, 230),
                new[] { new SKColor(0x22, 0xD3, 0xEE), new SKColor(0x4F, 0x6B, 0xFF), new SKColor(0xB4, 0x4C, 0xF0), new SKColor(0xFF, 0x4F, 0x9A), new SKColor(0xFF, 0xA0, 0x3C) },
                new[] { 0f, 0.3f, 0.55f, 0.78f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(drop, body);
        }
        c.Save();
        c.ClipPath(drop, antialias: true);
        // volume: darken the far side
        using (var vol = new SKPaint { IsAntialias = true })
        {
            vol.Shader = SKShader.CreateRadialGradient(new SKPoint(104, 128), 150,
                new[] { new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 0), new SKColor(20, 0, 60, 120) },
                new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(new SKRect(0, 0, n, n), vol);
        }
        // rim light along the lit edge
        using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12, StrokeCap = SKStrokeCap.Round })
        using (var rp = SKPath.ParseSvgPathData("M112,48 C92,84 66,116 66,152 C66,176 78,196 96,208"))
        {
            rim.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 40), new SKPoint(0, 210),
                new[] { new SKColor(255, 255, 255, 0), new SKColor(255, 255, 255, 170), new SKColor(255, 255, 255, 0) },
                new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(rp, rim);
        }
        // brighter core reflection
        using (var core = new SKPaint { IsAntialias = true })
        {
            core.Shader = SKShader.CreateRadialGradient(new SKPoint(150, 170), 70,
                new[] { new SKColor(255, 255, 255, 60), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp);
            c.DrawRect(new SKRect(0, 0, n, n), core);
        }
        c.Restore();
        // small specular spot
        using (var spec = new SKPaint { IsAntialias = true })
        {
            spec.Shader = SKShader.CreateRadialGradient(new SKPoint(104, 104), 16,
                new[] { new SKColor(255, 255, 255, 240), new SKColor(255, 255, 255, 0) }, null, SKShaderTileMode.Clamp);
            c.DrawOval(new SKRect(88, 86, 120, 122), spec);
        }
        c.Restore();

        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://openpaint3d/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });
        // toolbar buttons stay flat when disabled (Paint 3D style)
        Styles.Add(new Style(x => x.OfType<Button>().Class("top").Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent),
                new Setter(Visual.OpacityProperty, 0.4),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
        base.OnFrameworkInitializationCompleted();
    }
}
