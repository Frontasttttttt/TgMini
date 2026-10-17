using System;
using Avalonia;
using Avalonia.Media;

namespace TgMini.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    new FontFallback { FontFamily = new FontFamily("Noto Color Emoji") },
                    new FontFallback { FontFamily = new FontFamily("Noto Emoji") },
                    new FontFallback { FontFamily = new FontFamily("Twitter Color Emoji") },
                    new FontFallback { FontFamily = new FontFamily("Apple Color Emoji") },
                    new FontFallback { FontFamily = new FontFamily("Segoe UI Emoji") },
                    new FontFallback { FontFamily = new FontFamily("DejaVu Sans") },
                    new FontFallback { FontFamily = new FontFamily("Symbola") }
                }
            })
            .LogToTrace();
}
