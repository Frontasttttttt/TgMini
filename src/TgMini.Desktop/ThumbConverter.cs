using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace TgMini.Desktop;

public sealed class ThumbConverter : IValueConverter
{
    public static readonly ThumbConverter Instance = new();
    private const int MaxCapacity = 96;
    private static readonly object _lock = new();
    private static readonly Dictionary<string, (LinkedListNode<string> Node, Bitmap Bmp)> _cache = new();
    private static readonly LinkedList<string> _lruList = new();

    public static void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var width = 48;
        if (parameter is int w) width = w;
        else if (parameter is string s && int.TryParse(s, out var pw)) width = pw;

        var key = $"{path}_{width}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _lruList.Remove(entry.Node);
                _lruList.AddFirst(entry.Node);
                return entry.Bmp;
            }
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bmp = Bitmap.DecodeToWidth(stream, width);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    try { bmp.Dispose(); } catch { }
                    return existing.Bmp;
                }

                if (_cache.Count >= MaxCapacity)
                {
                    var oldest = _lruList.Last;
                    if (oldest != null)
                    {
                        _lruList.RemoveLast();
                        _cache.Remove(oldest.Value);
                    }
                }

                var node = new LinkedListNode<string>(key);
                _lruList.AddFirst(node);
                _cache[key] = (node, bmp);
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
