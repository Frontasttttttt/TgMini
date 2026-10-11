// WTelegramBridge — реализация IBridge на WTelegramClient 4.4.9 (замена tg.py).
// Ключи диалогов — Telethon-style marked id: user +id, chat -id,
// channel -(1_000_000_000_000 + id) — совместимо с Python-прототипом и FolderMatcher.
using TgMini.Core.Models;
using TL;
using WTelegram;

namespace TgMini.Core;

public sealed class WTelegramBridge : IBridge
{
    private readonly AppConfig _cfg;
    private readonly string _sessionPath;
    private Client? _c;
    private long _me;
    private string _meUsername = "";

    private Messages_Dialogs? _dlg;
    private DateTime _dlgAt = DateTime.MinValue;
    private readonly Dictionary<long, string> _names = new();
    private readonly Dictionary<long, ChatKind> _kinds = new();
    private readonly Dictionary<long, InputPeer> _peers = new();
    private readonly object _gate = new();

    /// <summary>Запрос "verification_code" | "password" | "first_name" при первом входе.</summary>
    public Func<string, string?>? Prompt { get; set; }

    public event Action<IncomingMessage>? Message;

    public WTelegramBridge(AppConfig cfg, string? sessionPath = null)
    {
        _cfg = cfg;
        _sessionPath = sessionPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TgMini", "TgMini.session");
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
    }

    /// <summary>
    /// True, если авторизация УЖЕ УДАЛАСЬ раньше: файл сессии существует, не пуст,
    /// и есть отдельный маркер *.auth.
    /// </summary>
    public bool HasSession => File.Exists(_sessionPath) &&
                              new FileInfo(_sessionPath).Length > 0 &&
                              File.Exists(_sessionPath + ".auth");

    private void MarkAuthed()
    {
        try { File.WriteAllText(_sessionPath + ".auth", DateTime.Now.ToString("o")); }
        catch { /* ignore */ }
    }

    private string? Config(string what) => what switch
    {
        "api_id" => _cfg.Telegram.ApiId.ToString(),
        "api_hash" => _cfg.Telegram.ApiHash,
        // КРИТИЧНО: WTelegram 4.4.9 запрашивает именно "session_pathname"
        // (без подчёркивания!), не "session_path_name". Без этого ключа сессия
        // пишется в %USERPROFILE%\WTelegram.session — её перетирают другие копии
        // и возникают IO_SharingViolation_File / AUTH_KEY_UNREGISTERED.
        "session_pathname" => _sessionPath,
        "session_path_name" => _sessionPath,
        "phone_number" => _cfg.Telegram.Phone,
        "verification_code" => Prompt?.Invoke("verification_code"),
        "password" => Prompt?.Invoke("password"),
        "first_name" => Prompt?.Invoke("first_name") ?? "TgMini",
        "last_name" => Prompt?.Invoke("last_name") ?? "",
        "email" => Prompt?.Invoke("email") ?? "",
        _ => null
    };

    private long Key(Peer p) => p switch
    {
        PeerUser u => u.user_id,
        PeerChat c => -c.chat_id,
        PeerChannel c => -(1_000_000_000_000L + c.channel_id),
        _ => 0
    };

    private long Key(InputPeer p) => p switch
    {
        InputPeerUser u => u.user_id,
        InputPeerChat c => -c.chat_id,
        InputPeerChannel c => -(1_000_000_000_000L + c.channel_id),
        InputPeerSelf => _me,
        _ => 0
    };

    private static string Display(User u) =>
        $"{u.first_name} {u.last_name}".Trim() is { Length: > 0 } n
            ? n
            : (u.MainUsername is { Length: > 0 } un ? "@" + un : $"user{u.id}");

    private void RebuildCaches(Messages_Dialogs d)
    {
        lock (_gate)
        {
            foreach (var u in d.users.Values)
            {
                if (u is not User x) continue;
                _names[x.id] = (x.id == _me) ? "Избранное" : Display(x);
                _kinds[x.id] = x.IsBot ? ChatKind.Bot : ChatKind.User;
                try { _peers[x.id] = (InputPeer)x; } catch { /* нет access_hash */ }
            }
            foreach (var c in d.chats.Values)
            {
                long key; ChatKind kind; string name;
                switch (c)
                {
                    case Channel ch:
                        key = -(1_000_000_000_000L + ch.id);
                        kind = ch.IsChannel ? ChatKind.Channel : ChatKind.Group;
                        name = ch.Title;
                        break;
                    case Chat g:
                        key = -g.id;
                        kind = ChatKind.Group;
                        name = g.Title;
                        break;
                    default:
                        continue;
                }
                _names[key] = name;
                _kinds[key] = kind;
                try { _peers[key] = (InputPeer)c; } catch { /* нет access_hash */ }
            }
        }
    }

    private async Task<Messages_Dialogs> LoadDialogsAsync(bool force)
    {
        if (_c is null) throw new InvalidOperationException("not connected");
        lock (_gate)
        {
            if (!force && _dlg is not null && (DateTime.UtcNow - _dlgAt).TotalSeconds < 20)
                return _dlg;
        }
        var d = await _c.Messages_GetAllDialogs();
        RebuildCaches(d);
        lock (_gate) { _dlg = d; _dlgAt = DateTime.UtcNow; }
        return d;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        MediaCache.Cleanup();
        // Старый клиент (если был) держит session-файл открытым — иначе фоллбэк-QR ниже
        // упадёт с IO_SharingViolation_File сам на себя.
        try { _c?.Dispose(); } catch { /* ignore */ }
        var c = new Client(Config);
        _c = c;
        var me = await c.LoginUserIfNeeded();
        _me = me.id;
        _meUsername = me.MainUsername ?? "";
        MarkAuthed();
        await LoadDialogsAsync(force: true);
        c.OnUpdates += HandleUpdates;
        return true;
    }

    public async Task<IReadOnlyList<DialogInfo>> GetDialogsAsync(int limit = 200, CancellationToken ct = default)
    {
        var d = await LoadDialogsAsync(force: false);
        var top = new Dictionary<(long chat, int id), MessageBase>();
        foreach (var m in d.messages)
            if (m is Message mm)
                top[(Key(mm.peer_id), mm.id)] = mm;
        var out_ = new List<DialogInfo>(Math.Min(limit, d.dialogs.Length));
        var pinOrder = 0;
        foreach (var dbase in d.dialogs)
        {
            if (dbase is not Dialog dlg) continue;
            var key = Key(dlg.peer);
            var isPinned = (dlg.flags & Dialog.Flags.pinned) != 0;
            var pIdx = isPinned ? pinOrder++ : int.MaxValue;
            var name = (key == _me) ? "Избранное" : (_names.GetValueOrDefault(key) ?? $"chat{key}");
            var last = "";
            DateTime? date = null;
            if (top.TryGetValue((key, dlg.top_message), out var m2) && m2 is Message mm2)
            {
                var (k, lbl) = DescribeMedia(mm2);
                last = string.IsNullOrWhiteSpace(mm2.message) ? MediaLabel(k, lbl) : mm2.message!;
                date = mm2.date;
            }
            var kind = _kinds.GetValueOrDefault(key, ChatKind.Other);
            var muted = dlg.notify_settings?.mute_until is { } mu && (mu > DateTime.Now || mu > DateTime.UtcNow);
            out_.Add(new DialogInfo(key, name, last[..Math.Min(90, last.Length)],
                date, dlg.unread_count, kind is ChatKind.Group or ChatKind.Channel,
                kind, muted, dlg.folder_id == 1, isPinned, pIdx));
            if (out_.Count >= limit) break;
        }
        out_.Sort((a, b) =>
        {
            if (a.IsPinned != b.IsPinned) return b.IsPinned ? 1 : -1;
            if (a.IsPinned && b.IsPinned) return a.PinnedIndex.CompareTo(b.PinnedIndex);
            return (b.Date ?? DateTime.MinValue).CompareTo(a.Date ?? DateTime.MinValue);
        });
        return out_;
    }

    public async Task<IReadOnlyList<ChatFolder>> GetFoldersAsync(CancellationToken ct = default)
    {
        if (_c is null) return Array.Empty<ChatFolder>();
        var res = await _c.Messages_GetDialogFilters();
        var out_ = new List<ChatFolder>();
        foreach (var f in res?.filters ?? Array.Empty<DialogFilterBase>())
        {
            if (f is not DialogFilter df) continue;   // DialogFilterChatlist — не папка
            var flags = df.flags;
            var types = new List<string>();
            if ((flags & (DialogFilter.Flags.contacts | DialogFilter.Flags.non_contacts)) != 0) types.Add("user");
            if ((flags & DialogFilter.Flags.bots) != 0) types.Add("bot");
            if ((flags & DialogFilter.Flags.groups) != 0) types.Add("group");
            if ((flags & DialogFilter.Flags.broadcasts) != 0) types.Add("channel");
            var peers = new List<long>();
            foreach (var p in df.include_peers ?? Array.Empty<InputPeer>()) peers.Add(Key(p));
            foreach (var p in df.pinned_peers ?? Array.Empty<InputPeer>()) peers.Add(Key(p));
            var excl = new List<long>();
            foreach (var p in df.exclude_peers ?? Array.Empty<InputPeer>()) excl.Add(Key(p));
            out_.Add(new ChatFolder(df.id, df.title?.text ?? $"#{df.id}",
                peers.Distinct().ToList(), excl.Distinct().ToList(), types,
                (flags & DialogFilter.Flags.exclude_read) != 0,
                (flags & DialogFilter.Flags.exclude_muted) != 0,
                (flags & DialogFilter.Flags.exclude_archived) != 0));
        }
        return out_;
    }

    private async Task<InputPeer?> ResolveAsync(long key)
    {
        if (key == _me) return InputPeer.Self;
        InputPeer? p;
        lock (_gate) _peers.TryGetValue(key, out p);
        if (p is not null) return p;
        await LoadDialogsAsync(force: true);
        lock (_gate) _peers.TryGetValue(key, out p);
        return p;
    }

    private (Dictionary<long, User> users, Dictionary<long, ChatBase> chats) Entities(Messages_MessagesBase h)
        => h is Messages_Messages m
            ? (m.users, m.chats)
            : (new Dictionary<long, User>(), new Dictionary<long, ChatBase>());

    private void AbsorbEntities(Dictionary<long, User> users, Dictionary<long, ChatBase> chats)
    {
        lock (_gate)
        {
            foreach (var u in users.Values)
                if (u is User ux)
                {
                    _names[ux.id] = Display(ux);
                    _kinds[ux.id] = ux.IsBot ? ChatKind.Bot : ChatKind.User;
                }
            foreach (var c in chats.Values)
            {
                long k; ChatKind kind; string name;
                switch (c)
                {
                    case Channel ch: k = -(1_000_000_000_000L + ch.id); kind = ch.IsChannel ? ChatKind.Channel : ChatKind.Group; name = ch.Title; break;
                    case Chat g: k = -g.id; kind = ChatKind.Group; name = g.Title; break;
                    default: continue;
                }
                _names[k] = name; _kinds[k] = kind;
            }
        }
    }

    public async Task<IReadOnlyList<MessageInfo>> GetHistoryAsync(long peerId, int limit = 20, CancellationToken ct = default)
    {
        if (_c is null) return Array.Empty<MessageInfo>();
        var peer = await ResolveAsync(peerId);
        if (peer is null) return Array.Empty<MessageInfo>();
        var hist = await _c.Messages_GetHistory(peer, offset_id: 0, offset_date: default,
            add_offset: 0, limit: limit, max_id: 0, min_id: 0, hash: 0);
        var (users, chats) = Entities(hist);
        AbsorbEntities(users, chats);
        var out_ = new List<MessageInfo>();
        foreach (var mb in hist.Messages)
            if (mb is Message m)
                out_.Add(ToMessageInfo(m, peerId, users, chats));
        out_.Reverse();   // Telegram отдаёт новые первыми — как в Telethon, реверс
        return out_;
    }

    public async Task<IReadOnlyList<MessageInfo>> GetContextAsync(long peerId, int aroundId, int before = 10, int after = 10, CancellationToken ct = default)
    {
        if (_c is null) return Array.Empty<MessageInfo>();
        var peer = await ResolveAsync(peerId);
        if (peer is null) return Array.Empty<MessageInfo>();
        var hist = await _c.Messages_GetHistory(peer, offset_id: 0, offset_date: default,
            add_offset: 0, limit: before + after + 1, max_id: aroundId + after,
            min_id: Math.Max(aroundId - before - 1, 0), hash: 0);
        var (users, chats) = Entities(hist);
        AbsorbEntities(users, chats);
        var out_ = new List<MessageInfo>();
        foreach (var mb in hist.Messages)
            if (mb is Message m)
                out_.Add(ToMessageInfo(m, peerId, users, chats));
        out_.Reverse();
        return out_;
    }

    private MessageInfo ToMessageInfo(Message m, long fallbackChat,
        Dictionary<long, User>? users = null, Dictionary<long, ChatBase>? chats = null)
    {
        var chatKey = Key(m.peer_id);
        if (chatKey == 0) chatKey = fallbackChat;
        var authorKey = m.from_id is null ? 0 : Key(m.from_id);
        var author = "";
        if (authorKey != 0)
        {
            if (users != null && users.TryGetValue(authorKey, out var ux))
                author = Display(ux);
            else
                author = _names.GetValueOrDefault(authorKey, "");
            if (chatKey == authorKey) author = "";   // личка — автора не показываем
        }
        var (kind, label) = DescribeMedia(m);
        var replyTo = (m.reply_to as MessageReplyHeader)?.reply_to_msg_id ?? 0;
        return new MessageInfo(m.id, chatKey, IsOut(m), m.message ?? "",
            m.date, authorKey, author, replyTo, kind, label, "");
    }

    private bool IsOut(Message m) => (m.flags & TL.Message.Flags.out_) != 0 || (m.from_id is PeerUser pu && pu.user_id == _me);

    private static string MediaLabel(MediaKind k, string lbl) => k switch
    {
        MediaKind.Photo => "[photo]",
        MediaKind.Video => "[video]",
        MediaKind.RoundVideo => "[кружочек]",
        MediaKind.Gif => "[GIF]",
        MediaKind.Sticker => $"[{lbl}]",
        MediaKind.Voice => $"[voice {lbl}]",
        MediaKind.Audio => "[audio]",
        MediaKind.File => $"[{lbl}]",
        _ => "[media]"
    };

    private static (MediaKind, string) DescribeMedia(Message m)
    {
        switch (m.media)
        {
            case MessageMediaPhoto:
                return (MediaKind.Photo, "photo");
            case MessageMediaDocument { document: Document doc }:
                var attrs = doc.attributes ?? Array.Empty<DocumentAttribute>();
                var fn = attrs.OfType<DocumentAttributeFilename>().FirstOrDefault()?.file_name ?? "";
                if (attrs.Any(a => a is DocumentAttributeSticker))
                    return (MediaKind.Sticker, attrs.OfType<DocumentAttributeSticker>().First().alt ?? "sticker");
                if (attrs.Any(a => a is DocumentAttributeAnimated))
                    return (MediaKind.Gif, "GIF");
                if (attrs.OfType<DocumentAttributeVideo>().Any(v => (v.flags & DocumentAttributeVideo.Flags.round_message) != 0))
                    return (MediaKind.RoundVideo, "кружочек");
                if (attrs.Any(a => a is DocumentAttributeVideo))
                    return (MediaKind.Video, fn.Length > 0 ? fn : "video");
                if (attrs.OfType<DocumentAttributeAudio>().Any(a => (a.flags & DocumentAttributeAudio.Flags.voice) != 0))
                {
                    var dur = attrs.OfType<DocumentAttributeAudio>().First().duration;
                    return (MediaKind.Voice, dur > 0 ? $"{dur / 60}:{dur % 60:D2}" : "voice");
                }
                if (attrs.Any(a => a is DocumentAttributeAudio))
                    return (MediaKind.Audio, fn.Length > 0 ? fn : "audio");
                if (fn.Length > 0) return (MediaKind.File, fn);
                var mime = doc.mime_type ?? "";
                if (mime.StartsWith("audio/")) return (MediaKind.Audio, "");
                if (mime.StartsWith("video/")) return (MediaKind.Video, "");
                return (MediaKind.File, mime.Length > 0 ? mime : "file");
            case MessageMediaWebPage:
                return (MediaKind.File, "link");
            case MessageMediaContact c:
                return (MediaKind.File, $"contact {c.first_name} {c.last_name}".Trim());
            case MessageMediaGeo:
                return (MediaKind.File, "location");
            case MessageMediaVenue v:
                return (MediaKind.File, v.title ?? "venue");
            case null:
                return (MediaKind.Text, "");
            default:
                return (MediaKind.File, "media");
        }
    }

    private async Task<Message?> GetMessageAsync(InputPeer peer, int msgId)
    {
        if (_c is null) return null;
        var r = await _c.GetMessages(peer, new InputMessage[] { new InputMessageID { id = msgId } });
        return r.Messages.FirstOrDefault() as Message;
    }

    public async Task<Dictionary<long, string>> ResolveAuthorsAsync(IEnumerable<long> senderIds, CancellationToken ct = default)
    {
        var out_ = new Dictionary<long, string>();
        lock (_gate)
            foreach (var id in senderIds.Distinct())
                if (_names.TryGetValue(id, out var n))
                    out_[id] = n;
        await Task.CompletedTask;
        return out_;
    }

    public async Task<string> GetReplyTextAsync(long chatId, int replyId, CancellationToken ct = default)
    {
        try
        {
            var peer = await ResolveAsync(chatId);
            if (peer is null) return "";
            var m = await GetMessageAsync(peer, replyId);
            if (m is null) return "";
            if (!string.IsNullOrWhiteSpace(m.message)) return m.message![..Math.Min(120, m.message!.Length)];
            var (k, lbl) = DescribeMedia(m);
            return MediaLabel(k, lbl);
        }
        catch { return ""; }
    }

    public async Task<string> EnsureThumbAsync(long chatId, int msgId, CancellationToken ct = default)
    {
        var path = MediaCache.ThumbPath(chatId, msgId);
        if (File.Exists(path)) return path;
        try
        {
            var peer = await ResolveAsync(chatId);
            if (peer is null || _c is null) return "";
            var m = await GetMessageAsync(peer, msgId);
            if (m?.media is MessageMediaPhoto { photo: Photo p })
            {
                MediaCache.Ensure();
                using var fs = File.Create(path);
                await _c.DownloadFileAsync(p, fs, photoSize: null);
                return path;
            }
            if (m?.media is MessageMediaDocument { document: Document d2 })
            {
                var thumb = d2.LargestThumbSize;
                if (thumb is not null)
                {
                    MediaCache.Ensure();
                    using var fs = File.Create(path);
                    await _c.DownloadFileAsync(d2, fs, thumb);
                    return path;
                }
                // превью нет (стикер/гиф) — качаем целиком, это и есть превью
                var ext = Path.GetExtension(d2.Filename ?? "") is { Length: > 1 } e ? e : ".bin";
                var p2 = MediaCache.SaveBytes(Array.Empty<byte>(), chatId, msgId, ext);
                using (var fs = File.Create(p2))
                    await _c.DownloadFileAsync(d2, fs, thumbSize: null);
                return p2;
            }
        }
        catch (Exception e) { Console.WriteLine($"thumb err: {e.Message}"); }
        return "";
    }

    public async Task<string?> EnsureAvatarAsync(long peerId, CancellationToken ct = default)
    {
        if (peerId == _me) return null; // Избранное остаётся со специальной иконкой ⭐
        var cacheDir = Path.Combine(MediaCache.Dir, "avatars");
        try { Directory.CreateDirectory(cacheDir); } catch { }
        var path = Path.Combine(cacheDir, $"{peerId}.jpg");
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

        if (_c is null) return null;
        IPeerInfo? peerInfo = null;
        lock (_gate)
        {
            if (_dlg is not null)
            {
                if (peerId > 0 && _dlg.users.TryGetValue(peerId, out var u)) peerInfo = u;
                else if (peerId < 0)
                {
                    var chId = -(peerId + 1_000_000_000_000L);
                    if (_dlg.chats.TryGetValue(chId, out var ch)) peerInfo = ch;
                    else if (_dlg.chats.TryGetValue(-peerId, out var g)) peerInfo = g;
                }
            }
        }
        if (peerInfo is null) return null;

        try
        {
            using var fs = File.Create(path);
            await _c.DownloadProfilePhotoAsync(peerInfo, fs, big: false);
            if (fs.Length == 0)
            {
                fs.Dispose();
                try { File.Delete(path); } catch { }
                return null;
            }
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch { }
            return null;
        }
    }

    public async Task<bool> ToggleChatFolderAsync(int folderId, long peerId, CancellationToken ct = default)
    {
        if (_c is null) return false;
        try
        {
            var res = await _c.Messages_GetDialogFilters();
            var target = res?.filters?.OfType<DialogFilter>().FirstOrDefault(x => x.id == folderId);
            if (target is null) return false;

            var peer = await ResolveAsync(peerId);
            if (peer is null) return false;

            var incList = target.include_peers?.ToList() ?? new List<InputPeer>();
            var exists = incList.Any(p => Key(p) == peerId);
            if (exists)
            {
                incList.RemoveAll(p => Key(p) == peerId);
            }
            else
            {
                incList.Add(peer);
            }
            target.include_peers = incList.ToArray();
            await _c.Messages_UpdateDialogFilter(folderId, target);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"ToggleChatFolder error: {e.Message}");
            return false;
        }
    }

    public async Task<string> DownloadToAsync(long chatId, int msgId, string destPath, IProgress<(long Sent, long Total)>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var peer = await ResolveAsync(chatId);
            if (peer is null || _c is null) return "";
            var m = await GetMessageAsync(peer, msgId);
            if (m is null) return "";
            var dir = Path.GetDirectoryName(Path.GetFullPath(destPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(destPath);
            void P(long sent, long total) => progress?.Report((sent, total));
            switch (m.media)
            {
                case MessageMediaPhoto { photo: Photo p }:
                    await _c.DownloadFileAsync(p, fs, photoSize: null, progress: P);
                    break;
                case MessageMediaDocument { document: Document d }:
                    await _c.DownloadFileAsync(d, fs, thumbSize: null, progress: P);
                    break;
                default:
                    return "";
            }
            return destPath;
        }
        catch (Exception e) { Console.WriteLine($"download err: {e.Message}"); return ""; }
    }

    public async Task<bool> SendAsync(long peerId, string text, int replyTo = 0, CancellationToken ct = default)
    {
        try
        {
            var peer = await ResolveAsync(peerId);
            if (peer is null || _c is null) return false;
            await _c.SendMessageAsync(peer, text, reply_to_msg_id: replyTo);
            return true;
        }
        catch (Exception e) { Console.WriteLine($"send err: {e.Message}"); return false; }
    }

    public async Task<bool> SendFileAsync(long peerId, string path, string caption = "", int replyTo = 0, IProgress<(long Sent, long Total)>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var peer = await ResolveAsync(peerId);
            if (peer is null || _c is null) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var mime = ext switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif",
                ".webp" => "image/webp", ".mp4" => "video/mp4", ".webm" => "video/webm",
                ".mp3" => "audio/mpeg", ".ogg" or ".oga" or ".opus" => "audio/ogg",
                ".pdf" => "application/pdf", ".doc" or ".docx" => "application/msword",
                ".txt" => "text/plain", ".zip" => "application/zip",
                _ => "application/octet-stream"
            };
            void P(long sent, long total) => progress?.Report((sent, total));
            var uploaded = await _c.UploadFileAsync(path, P);
            await _c.SendMediaAsync(peer, caption, uploaded, mime, reply_to_msg_id: replyTo);
            return true;
        }
        catch (Exception e) { Console.WriteLine($"sendfile err: {e.Message}"); return false; }
    }

    public async Task MarkReadAsync(long peerId, CancellationToken ct = default)
    {
        try
        {
            var peer = await ResolveAsync(peerId);
            if (peer is null || _c is null) return;
            await _c.ReadHistory(peer, max_id: int.MaxValue);
        }
        catch { /* ignore */ }
        await Task.CompletedTask;
    }

    private Task HandleUpdates(UpdatesBase u)
    {
        try
        {
            foreach (var upd in u.UpdateList)
            {
                Message? m = upd switch
                {
                    UpdateNewMessage unm => unm.message as Message,
                    UpdateEditMessage uem => uem.message as Message,
                    _ => null
                };
                if (m is null) continue;
                var isOut = IsOut(m);
                var chatKey = Key(m.peer_id);
                var senderKey = m.from_id is null ? 0 : Key(m.from_id);
                string name;
                lock (_gate)
                    name = isOut ? "Вы" : _names.GetValueOrDefault(senderKey, _names.GetValueOrDefault(chatKey, "Telegram"));
                var (kind, label) = DescribeMedia(m);
                var text = m.message ?? "";
                if (string.IsNullOrWhiteSpace(text)) text = MediaLabel(kind, label);
                var isGroup = chatKey < 0;
                var mentioned = !string.IsNullOrEmpty(_meUsername) &&
                                text.Contains("@" + _meUsername, StringComparison.OrdinalIgnoreCase);
                Message?.Invoke(new IncomingMessage(chatKey, m.id, isOut, senderKey, name, text,
                    isGroup, mentioned, m.date, kind, label,
                    isOut ? "Вы" : (kind == MediaKind.Text ? "" : name)));
            }
        }
        catch (Exception e) { Console.WriteLine($"updates err: {e.Message}"); }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _c?.Dispose(); } catch { /* ignore */ }
        _c = null;
    }

    public async Task<bool> LoginQrAsync(Action<byte[]> onQr, CancellationToken ct = default)
    {
        MediaCache.Cleanup();
        // Предыдущий клиент (после неудачного ConnectAsync) ещё держит session-файл —
        // без Dispose второй Client в том же процессе получит IO_SharingViolation_File.
        try { _c?.Dispose(); } catch { /* ignore */ }
        var c = new Client(Config);
        _c = c;
        // qrDisplay получает строку-содержимое QR (~30 сек жизни) — рендерим PNG сами.
        // Возвращает Task<User> (авторизованный юзер) или null, если отменено.
        var user = await c.LoginWithQRCode(content =>
        {
            try { onQr(RenderQrPng(content)); } catch { /* ignore */ }
        }, except_ids: null, logoutFirst: false, ct: ct);
        if (user is null) return false;
        _me = user.id;
        _meUsername = user.MainUsername ?? "";
        MarkAuthed();
        await LoadDialogsAsync(force: true);
        c.OnUpdates += HandleUpdates;
        return true;
    }

    private static byte[] RenderQrPng(string content)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(content, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var gen2 = new QRCoder.PngByteQRCode(data);
        return gen2.GetGraphic(10);
    }
}

