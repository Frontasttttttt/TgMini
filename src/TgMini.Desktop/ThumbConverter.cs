using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TgMini.Desktop;

public sealed class ThumbConverter : IValueConverter
{
    public static readonly ThumbConverter Instance = new();
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();

    public static void ClearCache()
    {
        _cache.Clear();
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var width = 48;
        if (parameter is int w) width = w;
        else if (parameter is string s && int.TryParse(s, out var pw)) width = pw;

        var key = $"{path}_{width}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            using var stream = File.OpenRead(path);
            var bmp = Bitmap.DecodeToWidth(stream, width);
            _cache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
