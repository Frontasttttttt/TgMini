// Кэш превью как в Telegram: только %TEMP%\TgMini_cache, ничего на диске пользователя.
// Порт media_cache.py: чистка только по лимиту размера, по возрасту файлы не трогаем.
namespace TgMini.Core;

public static class MediaCache
{
    public static long MaxTotalBytes { get; set; } = 150L * 1024 * 1024;

    public static string Dir =>
        Path.Combine(Path.GetTempPath(), "TgMini_cache");

    public static void Ensure() => Directory.CreateDirectory(Dir);

    private static string Key(long chatId, long msgId, string kind)
    {
        var raw = $"{chatId}:{msgId}:{kind}";
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    public static string ThumbPath(long chatId, long msgId) =>
        Path.Combine(Dir, $"{Key(chatId, msgId, "t")}_thumb.jpg");

    public static string SaveBytes(byte[] data, long chatId, long msgId, string ext)
    {
        Ensure();
        var p = Path.Combine(Dir, $"{Key(chatId, msgId, "m")}{ext}");
        File.WriteAllBytes(p, data);
        return p;
    }

    /// <summary>Чистка только при превышении лимита: самое старое удаляется первым.</summary>
    public static void Cleanup()
    {
        try
        {
            Ensure();
            var files = new DirectoryInfo(Dir).GetFiles()
                .OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= MaxTotalBytes) break;
                try { total -= f.Length; f.Delete(); }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }
}
