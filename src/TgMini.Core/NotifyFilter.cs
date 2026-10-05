// Фильтры показа уведомлений — порт should_show/_route/in_dnd из Python-прототипа.
namespace TgMini.Core;

public static class NotifyFilter
{
    public static bool InDnd(IEnumerable<string> ranges, DateTime now)
    {
        foreach (var r in ranges)
        {
            var parts = r.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (!TimeSpan.TryParse(parts[0], out var a)) continue;
            if (!TimeSpan.TryParse(parts[1], out var b)) continue;
            var t = now.TimeOfDay;
            if (a <= b) { if (a <= t && t <= b) return true; }
            else if (t >= a || t <= b) return true;
        }
        return false;
    }

    public static bool ShouldShow(
        AppConfig.FilterSection f,
        string senderName, long senderId, string? username,
        bool isGroup, bool mentioned, bool outgoing)
    {
        if (outgoing && f.IgnoreOutgoing) return false;
        var blob = $"{senderName} {username} {senderId}".ToLowerInvariant();
        if (f.Mute.Any(m => blob.Contains(m.ToLowerInvariant()))) return false;
        if (f.OnlyMentionsInGroups && isGroup && !mentioned) return false;
        if (f.AllowOnly.Count > 0 &&
            !f.AllowOnly.Any(a => blob.Contains(a.ToLowerInvariant()))) return false;
        return true;
    }

    /// <summary>
    /// Тихий режим: в игре показываем личку и упоминания, остальное — молча в лог.
    /// Возвращает (show, hiddenQuiet).
    /// </summary>
    public static (bool Show, bool HiddenQuiet) Route(
        AppConfig cfg, bool isFullscreenGame, DateTime now,
        bool isGroup, bool mentioned)
    {
        if (InDnd(cfg.Filter.Dnd, now)) return (false, true);
        if (cfg.Filter.QuietInGame && isFullscreenGame)
        {
            if (mentioned) return (true, false);
            if (isGroup && !cfg.Filter.ShowGroupsInGame) return (false, true);
            return (true, false); // личка в игре — показываем
        }
        return (true, false);
    }
}
