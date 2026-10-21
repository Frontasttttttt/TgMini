// Смок-тест портированной логики — зеркалит проверки Python-прототипа:
// папки Personal(34)/Study(13)/Unread(1), скролл-правила, DND, TTL не нужен.
// Выход 0 = всё сошлось с эталоном.
using TgMini.Core;
using TgMini.Core.Models;

static int Fail(string msg) { Console.WriteLine("FAIL: " + msg); return 1; }

var personal = new ChatFolder(2, "Personal",
    new List<long> { 101, 102 }, new List<long>(), new List<string> { "user" },
    false, false, false);
var study = new ChatFolder(4, "Study",
    new List<long> { 201, 202, 203 }, new List<long>(), new List<string>(),
    false, false, false);
var unread = new ChatFolder(3, "Unread",
    new List<long>(), new List<long>(), new List<string> { "user", "bot", "group", "channel" },
    true, false, false);

var dialogs = new List<(long Id, string Kind, int Un)>();
dialogs.AddRange(new[] { 101L, 102L }.Select(id => (id, "user", 0)));
dialogs.AddRange(Enumerable.Range(0, 31).Select(i => ((long)(1000 + i), "user", 0))); // контакты: 2+31+1=34 юзера
dialogs.AddRange(new[] { 201L, 202L, 203L }.Select(id => (id, "group", 0)));
dialogs.AddRange(Enumerable.Range(0, 10).Select(i => ((long)(2000 + i), "group", 0)));
dialogs.Add((301L, "channel", 2));
dialogs.Add((302L, "user", 1));

int Count(ChatFolder? f) => dialogs.Count(d => FolderMatcher.Match(d.Id, d.Kind, d.Un, false, false, f));
if (Count(null) != dialogs.Count) return Fail("All");
if (Count(personal) != 34) return Fail($"Personal={Count(personal)} != 34");
if (Count(study) != 3) return Fail($"Study={Count(study)} != 3");
if (Count(unread) != 2) return Fail($"Unread={Count(unread)} != 2");
Console.WriteLine($"folders: All={dialogs.Count} Personal={Count(personal)} Study={Count(study)} Unread={Count(unread)}");

// DND окно через полночь, как "23:00-08:00"
var dnd = new List<string> { "23:00-08:00" };
bool At(int h, int m) => NotifyFilter.InDnd(dnd, new DateTime(2026, 10, 1, h, m, 0));
if (!At(0, 30) || !At(23, 30) || At(12, 0) || At(9, 0)) return Fail("DND");

// quiet-режим: упоминание в группе идёт, обычная группа — в лог
var cfg = new AppConfig();
var (s1, h1) = NotifyFilter.Route(cfg, true, new DateTime(2026, 10, 1, 12, 0, 0), true, true);
var (s2, h2) = NotifyFilter.Route(cfg, true, new DateTime(2026, 10, 1, 12, 0, 0), true, false);
var (s3, h3) = NotifyFilter.Route(cfg, true, new DateTime(2026, 10, 1, 12, 0, 0), false, false);
if (!(s1 && !h1) || !(!s2 && h2) || !(s3 && !h3)) return Fail("quiet-route");

// фильтры allow/mute как в tg.py should_show
var f = new AppConfig.FilterSection { AllowOnly = new() { "mama" }, Mute = new() { "bot" } };
if (NotifyFilter.ShouldShow(f, "Mama", 1, null, false, false, false) != true) return Fail("allow");
if (NotifyFilter.ShouldShow(f, "Boss", 2, null, false, false, false) != false) return Fail("allow2");
if (NotifyFilter.ShouldShow(f, "Mama", 1, null, false, false, true) != false) return Fail("outgoing");
var f2 = new AppConfig.FilterSection { OnlyMentionsInGroups = true };
if (NotifyFilter.ShouldShow(f2, "Chat", 5, null, true, false, false) != false) return Fail("mention");

// кэш: лимит держится, по возрасту ничего не трогаем (как media_cache.py)
MediaCache.Ensure();
MediaCache.Cleanup();

// missed.db: log/unseen/markSeen (порт missed_store.py)
var dbPath = Path.Combine(Path.GetTempPath(), $"tgmini_smoke_{Guid.NewGuid():N}.db");
try { File.Delete(dbPath); } catch { }
using (var s2_ = new MissedStore(dbPath))
{
    s2_.Log("Мама", "позвони", hiddenQuiet: true);
    s2_.Log("Boss", "отчёт");
    if (s2_.UnseenCount() != 2) return Fail("unseen");
    var rows = s2_.Recent(10);
    if (rows.Count != 2 || rows.Count(r => r.HiddenQuiet) != 1) return Fail("recent");
    s2_.MarkSeen();
    if (s2_.UnseenCount() != 0) return Fail("markseen");
    Console.WriteLine("missed.db: log/unseen/markseen ok");
    try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
}

// конфиг: читаем реальные ключи Python-прототипа (fallback на telegram-overlay/config.toml)
var real = AppConfig.Load();
if (real.Telegram.ApiId == 0 || real.Telegram.ApiHash.Contains("PASTE"))
    return Fail("config fallback не подхватил ключи из telegram-overlay/config.toml");
Console.WriteLine($"config: api_id={real.Telegram.ApiId} phone_len={real.Telegram.Phone.Length} (ключи из Python-прототипа)");

Console.WriteLine("SMOKE_OK");
return 0;
