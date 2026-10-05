// Живой тест моста WTelegramClient: конфиг -> логин -> диалоги -> папки -> история.
// Код из Telegram берётся из env TGMINI_CODE (для неинтерактивного запуска) или с консоли.
// Выход: 0 = ок; 3 = нужен код (сессии ещё нет, но соединение с DC установлено).
// ВАЖНО: WTelegramClient сам ограничивает попытки кода (MaxCodePwdAttempts=3);
// не гонять тест вхолостую подряд — Telegram может дать FLOOD_WAIT.
using TgMini.Core;
using TgMini.Core.Models;

var cfg = AppConfig.Load();
var session = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TgMini", "TgMini.session");
Directory.CreateDirectory(Path.GetDirectoryName(session)!);
Console.WriteLine($"config: api_id={cfg.Telegram.ApiId} api_hash_len={cfg.Telegram.ApiHash.Length} " +
                  $"phone_len={cfg.Telegram.Phone.Length} session={File.Exists(session)} path={session}");

var prompted = new List<string>();
var envCode = Environment.GetEnvironmentVariable("TGMINI_CODE");

using var bridge = new WTelegramBridge(cfg, session);
bridge.Prompt = what =>
{
    prompted.Add(what);
    Console.WriteLine($"PROMPT {what}");
    if (what == "verification_code") return envCode ?? (Console.IsInputRedirected ? "" : Console.ReadLine());
    if (what == "password") return envCode ?? (Console.IsInputRedirected ? "" : Console.ReadLine());
    return null;
};
bridge.Message += m => Console.WriteLine($"LIVE <{m.Name}> {m.Text[..Math.Min(40, m.Text.Length)]}");

try
{
    var conn = bridge.ConnectAsync();
    var done = await Task.WhenAny(conn, Task.Delay(TimeSpan.FromSeconds(45)));
    if (done != conn)
    {
        Console.WriteLine("TIMEOUT: ждём ответа Telegram (нужен код?) prompted=" + string.Join(",", prompted));
        return 3;
    }
    await conn;
}
catch (Exception e)
{
    Console.WriteLine($"connect err: {e.GetType().Name}: {e.Message}");
    if (prompted.Contains("verification_code") || prompted.Contains("password"))
    {
        Console.WriteLine($"NEED_CODE: соединение с Telegram прошло, api_id/api_hash приняты (prompted={string.Join(",", prompted)})");
        return 3;
    }
    return 2;
}

var dialogs = await bridge.GetDialogsAsync();
var folders = await bridge.GetFoldersAsync();
Console.WriteLine($"dialogs={dialogs.Count} folders={folders.Count}");
for (var i = 0; i < Math.Min(6, dialogs.Count); i++)
    Console.WriteLine($"  dlg[{i}] id={dialogs[i].Id} kind={dialogs[i].Kind} unread={dialogs[i].Unread} '{dialogs[i].Name}' :: {dialogs[i].LastMessage[..Math.Min(28, dialogs[i].LastMessage.Length)]}");

static string KindStr(ChatKind k) => k switch
{
    ChatKind.User => "user", ChatKind.Bot => "bot", ChatKind.Group => "group",
    ChatKind.Channel => "channel", _ => "other"
};

foreach (var f in folders)
{
    var matched = dialogs.Count(d => FolderMatcher.Match(d.Id, KindStr(d.Kind), d.Unread, d.Muted, d.Archived, f));
    Console.WriteLine($"  folder {f.Id} '{f.Title}' peers={f.Peers.Count} (include+pinned) types=[{string.Join(",", f.Types)}] exclude_read={f.ExcludeRead} matched={matched}");
}

if (dialogs.Count > 0)
{
    var first = dialogs.OrderByDescending(d => d.Unread > 0).First();
    var hist = await bridge.GetHistoryAsync(first.Id, 8);
    Console.WriteLine($"history('{first.Name}')={hist.Count}");
    foreach (var m in hist.TakeLast(5))
        Console.WriteLine($"  {(m.Out ? "→" : "←")} [{m.Author}] {m.Kind} {(m.Text.Length > 0 ? m.Text[..Math.Min(40, m.Text.Length)] : m.MediaLabel)} reply_to={m.ReplyTo}");
    var withReply = hist.FirstOrDefault(m => m.ReplyTo > 0);
    if (withReply is not null)
    {
        var q = await bridge.GetReplyTextAsync(first.Id, withReply.ReplyTo);
        Console.WriteLine($"reply_text(#{withReply.ReplyTo})='{q}'");
        var ctx = await bridge.GetContextAsync(first.Id, withReply.Id, 5, 5);
        Console.WriteLine($"context(#{withReply.Id})={ctx.Count}");
    }
}

Console.WriteLine("BRIDGE_OK");
return 0;

