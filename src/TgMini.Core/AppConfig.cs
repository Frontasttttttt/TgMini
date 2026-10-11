// Конфиг TgMini — тот же формат, что config.toml Python-прототипа (та же папка-сестра
// telegram-overlay/config.toml читается как fallback, чтобы не вводить ключи заново).
// Используем встроенный парсер без внешних пакетов: TOML-подмножество "секции + key = value".
using System.Text.RegularExpressions;

namespace TgMini.Core;

public sealed class AppConfig
{
    public TelegramSection Telegram { get; } = new();
    public FilterSection Filter { get; } = new();
    public OverlaySection Overlay { get; } = new();
    public HotkeysSection Hotkeys { get; } = new();
    public TraySection Tray { get; } = new();

    public const int DefaultApiId = 2040;
    public const string DefaultApiHash = "b18441a1ff607e10a989891a5462e627";
    public sealed class TelegramSection { public int ApiId = DefaultApiId; public string ApiHash = DefaultApiHash; public string Phone = ""; }
    public sealed class FilterSection
    {
        public List<string> AllowOnly = new();
        public List<string> Mute = new();
        public bool OnlyMentionsInGroups;
        public bool IgnoreOutgoing = true;
        public int MaxTextLen = 180;
        public bool QuietInGame = true;
        public bool ShowGroupsInGame;
        public List<string> Dnd = new();
    }
    public sealed class OverlaySection
    {
        public string Position = "top-right";
        public int Width = 360;
        public double DurationSec = 6;
        public bool CompactInFullscreen = true;
        public double CompactDurationSec = 4;
        public double Opacity = 0.96;
        public bool Sound;
        public bool Avatar = true;
        public bool ProgressBar = true;
        public bool HideText;
    }
    public sealed class HotkeysSection
    {
        public string HideAll = "Win+H";
        public string History = "Win+J";
        public string ToggleQuiet = "Win+M";
    }
    public sealed class TraySection { public bool Enabled = true; public bool Autostart; }

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "config.toml");

    /// <summary>Персональный конфиг пользователя (GUI сохраняет сюда, не в папку с zip).</summary>
    public static string UserConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TgMini", "config.toml");

    /// <summary>
    /// Грузит конфиг: (1) %LocalAppData%\TgMini\config.toml — чужой пользователь,
    /// (2) config.toml рядом с exe, (3) [только дев-сборка] прототип telegram-overlay.
    /// </summary>
    public static AppConfig Load(string? path = null)
    {
        var cfg = new AppConfig();
        var file = path ?? FindConfig();
        if (file is not null && File.Exists(file)) cfg.Parse(File.ReadAllLines(file));
        var envId = Environment.GetEnvironmentVariable("TG_API_ID");
        if (int.TryParse(envId, out var eid) && eid != 0) cfg.Telegram.ApiId = eid;
        var envHash = Environment.GetEnvironmentVariable("TG_API_HASH");
        if (!string.IsNullOrWhiteSpace(envHash)) cfg.Telegram.ApiHash = envHash.Trim();
        return cfg;
    }

    /// <summary>Сохраняет только ключи авторизации (остальное — дефолты кода).</summary>
    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserConfigPath)!);
        var s = $"""
[Telegram]
api_id = {cfg.Telegram.ApiId}
api_hash = "{cfg.Telegram.ApiHash}"
phone = "{cfg.Telegram.Phone}"
""";
        File.WriteAllText(UserConfigPath, s);
    }

    public void Save() => Save(this);

    public static string? FindConfig()
    {
        var user = UserConfigPath;
        if (File.Exists(user)) return user;
        var local = DefaultPath;
        if (File.Exists(local)) return local;
#if !PORTABLE
        // дев-сборка: подхватить ключи прототипа, чтобы не вводить заново
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var sibling = Path.Combine(dir.FullName, "telegram-overlay", "config.toml");
            if (File.Exists(sibling)) return sibling;
        }
#endif
        return null;
    }

    private void Parse(string[] lines)
    {
        string section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var m = Regex.Match(line, @"^\[(.+)\]$");
            if (m.Success) { section = m.Groups[1].Value.Trim(); continue; }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim().Trim('"');
            Set(section, key, val);
        }
    }

    private void Set(string section, string key, string val)
    {
        bool B() => val.Equals("true", StringComparison.OrdinalIgnoreCase);
        int I() => int.TryParse(val, out var n) ? n : 0;
        double D() => double.TryParse(val.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
        List<string> L() => val.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('"')).Where(s => s.Length > 0).ToList();

        switch (section.ToLowerInvariant())
        {
            case "telegram":
                if (key == "api_id") Telegram.ApiId = I();
                else if (key == "api_hash") Telegram.ApiHash = val;
                else if (key == "phone") Telegram.Phone = val;
                break;
            case "filter":
                if (key == "allow_only") Filter.AllowOnly = L();
                else if (key == "mute") Filter.Mute = L();
                else if (key == "only_mentions_in_groups") Filter.OnlyMentionsInGroups = B();
                else if (key == "ignore_outgoing") Filter.IgnoreOutgoing = B();
                else if (key == "max_text_len") Filter.MaxTextLen = I();
                else if (key == "quiet_in_game") Filter.QuietInGame = B();
                else if (key == "show_groups_in_game") Filter.ShowGroupsInGame = B();
                else if (key == "dnd") Filter.Dnd = L();
                break;
            case "overlay":
                if (key == "position") Overlay.Position = val;
                else if (key == "width") Overlay.Width = I();
                else if (key == "duration_sec") Overlay.DurationSec = D();
                else if (key == "compact_in_fullscreen") Overlay.CompactInFullscreen = B();
                else if (key == "compact_duration_sec") Overlay.CompactDurationSec = D();
                else if (key == "opacity") Overlay.Opacity = D();
                else if (key == "sound") Overlay.Sound = B();
                else if (key == "avatar") Overlay.Avatar = B();
                else if (key == "progress_bar") Overlay.ProgressBar = B();
                else if (key == "hide_text") Overlay.HideText = B();
                break;
            case "hotkeys":
                if (key == "hide_all") Hotkeys.HideAll = val;
                else if (key == "history") Hotkeys.History = val;
                else if (key == "toggle_quiet") Hotkeys.ToggleQuiet = val;
                break;
            case "tray":
                if (key == "enabled") Tray.Enabled = B();
                else if (key == "autostart") Tray.Autostart = B();
                break;
        }
    }
}
