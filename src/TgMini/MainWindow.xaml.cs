using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TgMini.Core;
using TgMini.Core.Models;

namespace TgMini;

public sealed class DialogRow
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Preview { get; init; } = "";
    public int Unread { get; init; }
    public string NameWeight => Unread > 0 ? "Bold" : "Normal";
    public string BadgeVisible => Unread > 0 ? "Visible" : "Collapsed";
}

public sealed class MsgRow
{
    public int Id { get; init; }
    public string Text { get; init; } = "";
    public string Author { get; init; } = "";
    public string AuthorColor { get; init; } = "#8B949E";
    public string AuthorVisible => Author.Length > 0 ? "Visible" : "Collapsed";
    public string Quote { get; init; } = "";
    public string QuoteVisible => Quote.Length > 0 ? "Visible" : "Collapsed";
    public string MediaHint { get; init; } = "";
    public string MediaVisible => MediaHint.Length > 0 ? "Visible" : "Collapsed";
    public string Bubble { get; init; } = "#1A2230";
    public string Align { get; init; } = "Left";
    public double WrapWidth { get; init; } = 460;
    public string Time { get; init; } = "";
}

public partial class MainWindow : Window
{
    private static readonly string[] AuthorColors =
        { "#2EA6FF", "#7C5CFF", "#00C48C", "#FF9F0A", "#FF5C7A", "#64D2FF", "#E6A23C", "#9ECE6A" };

    private readonly AppConfig _cfg = AppConfig.Load();
    private IBridge? _bridge;
    private readonly ObservableCollection<DialogRow> _rows = new();
    private readonly ObservableCollection<MsgRow> _feed = new();
    private List<DialogInfo> _all = new();
    private List<ChatFolder> _folders = new();
    private Dictionary<int, MessageInfo> _byId = new();
    private string _folder = "all";
    private long _peer;
    private string _peerName = "";
    private int _replyTo;

    public MainWindow()
    {
        try { System.IO.File.WriteAllText(AppConfig.UserConfigPath + ".dbg", "MainWindow ctor at " + DateTime.Now + "\n"); } catch { }
        InitializeComponent();
        DialogList.ItemsSource = _rows;
        FeedList.ItemsSource = _feed;
        Loaded += async (_, _) => await StartAsync();
    }

    private void Status(string s) => Dispatcher.Invoke(() => StatusLabel.Text = s);

    private async Task StartAsync()
    {
        if (Environment.GetEnvironmentVariable("TGMINI_NO_CONNECT") == "1")
        {
            Status("UI-режим без подключения (TGMINI_NO_CONNECT=1)");
            return;
        }
        if (_cfg.Telegram.ApiId == 0 || string.IsNullOrWhiteSpace(_cfg.Telegram.ApiHash))
        {
            var setup = new SetupWindow(_cfg, this) { Owner = this };
            setup.ShowDialog();
        }
        Status("Подключаюсь к Telegram...");
        var bridge = new WTelegramBridge(_cfg);
        bridge.Prompt = AskPrompt;
        bridge.Message += OnLiveMessage;
        _bridge = bridge;
        try
        {
            if (!bridge.HasSession)
            {
                Status("Сессии нет — открываю QR-вход...");
                var ok = await QrLoginAsync(bridge);
                if (ok)
                {
                    return;
                }
                Status("QR отменён — пробую вход по коду (Отправить код 1 раз).");
            }
            await bridge.ConnectAsync();
            Status("Загружаю чаты...");
            await RefreshAsync(force: true);
            Status($"Готово. Чатов: {_all.Count}, папок: {_folders.Count}");
        }
        catch (Exception e)
        {
            Status($"Вход не удался ({e.Message}). Предлагаю QR...");
            await QrLoginAsync(bridge);
        }
    }

    /// <summary>
    /// Запасной путь входа: QR через официальное приложение. Код из SMS/чата не нужен.
    /// </summary>
    private async Task<bool> QrLoginAsync(WTelegramBridge bridge)
    {
        var qr = new QrWindow { Owner = this };
        qr.Show();
        try
        {
            var ok = await ((IBridge)bridge).LoginQrAsync(qr.ShowQr, qr.Token);
            qr.Finish(ok, ok ? "Вход выполнен!" : "Вход не завершён");
            if (!ok) { return false; }
            Status("Загружаю чаты...");
            await RefreshAsync(force: true);
            Status($"Готово. Чатов: {_all.Count}, папок: {_folders.Count}");
            return true;
        }
        catch (OperationCanceledException)
        {
            try { qr.Close(); } catch { /* ignore */ }
            return false;
        }
        catch (Exception e)
        {
            try { qr.Close(); } catch { /* ignore */ }
            var hint = e.Message.Contains("SharingViolation", StringComparison.OrdinalIgnoreCase)
                ? " Закройте другие копии TgMini (панель задач) и перезапустите." : "";
            Status($"QR-вход не удался: {e.Message}.{hint}");
            return false;
        }
    }

    /// <summary>Мост просит код/пароль — показываем диалог в UI-потоке.</summary>
    private string? AskPrompt(string what)
        => Dispatcher.Invoke(() =>
        {
            var w = new VerificationWindow(what) { Owner = IsLoaded ? this : null };
            return w.ShowDialog() == true ? w.Value : "";
        });

    private void OnLiveMessage(IncomingMessage m)
    {
        Dispatcher.Invoke(() =>
        {
            var d = _all.FirstOrDefault(x => x.Id == m.ChatId);
            if (d is not null)
            {
                _all.Remove(d);
                _all.Insert(0, d with { LastMessage = m.Text, Unread = d.Unread + 1, Date = m.Date });
                PaintDialogs();
            }
            if (_peer == m.ChatId) _ = LoadHistoryAsync();
        });
    }

    private async Task RefreshAsync(bool force = false)
    {
        if (_bridge is null) return;
        _all = (await _bridge.GetDialogsAsync(200)).ToList();
        _folders = (await _bridge.GetFoldersAsync()).ToList();
        BuildFolderButtons();
        PaintDialogs();
    }

    private void BuildFolderButtons()
    {
        FolderBar.Children.Clear();
        AddFolderButton("all", "All");
        foreach (var f in _folders)
            AddFolderButton($"tg:{f.Id}", string.IsNullOrWhiteSpace(f.Title) ? $"#{f.Id}" : f.Title);
    }

    private void AddFolderButton(string key, string title)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = title, FontSize = 11 },
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(key == _folder ? "#2EA6FF" : "#1A2230")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = Cursors.Hand
        };
        b.Click += (_, _) =>
        {
            _folder = key;
            BuildFolderButtons();
            PaintDialogs();
        };
        FolderBar.Children.Add(b);
    }

    private ChatFolder? CurrentFolder()
    {
        if (_folder == "all") return null;
        if (!int.TryParse(_folder.AsSpan(3), out var id)) return null;
        return _folders.FirstOrDefault(f => f.Id == id);
    }

    private static string KindStr(ChatKind k) => k switch
    {
        ChatKind.User => "user", ChatKind.Bot => "bot",
        ChatKind.Group => "group", ChatKind.Channel => "channel", _ => "other"
    };

    private void PaintDialogs()
    {
        var q = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        var flt = CurrentFolder();
        _rows.Clear();
        foreach (var d in _all)
        {
            if (q.Length > 0 && !d.Name.ToLowerInvariant().Contains(q)) continue;
            if (!FolderMatcher.Match(d.Id, KindStr(d.Kind), d.Unread, d.Muted, d.Archived, flt)) continue;
            _rows.Add(new DialogRow { Id = d.Id, Name = d.Name, Preview = d.LastMessage, Unread = d.Unread });
            if (_rows.Count >= 60) break;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => PaintDialogs();

    private async void DialogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        _peer = r.Id;
        _peerName = r.Name;
        ChatTitle.Text = _peerName;
        ChatSub.Text = "";
        _replyTo = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
        await LoadHistoryAsync();
        if (_bridge is not null) await _bridge.MarkReadAsync(_peer);
    }

    private async Task LoadHistoryAsync()
    {
        if (_bridge is null || _peer == 0) return;
        try
        {
            var msgs = await _bridge.GetHistoryAsync(_peer, 20);
            PaintFeed(msgs);
            ChatSub.Text = $"{msgs.Count} msgs";
        }
        catch (Exception e) { Status($"history err: {e.Message}"); }
    }

    private void PaintFeed(IReadOnlyList<MessageInfo> msgs)
    {
        _byId = msgs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());
        _feed.Clear();
        foreach (var m in msgs)
            _feed.Add(ToRow(m));
        if (_feed.Count > 0) FeedList.ScrollIntoView(_feed[^1]);
    }

    private MsgRow ToRow(MessageInfo m)
    {
        var hint = m.Kind switch
        {
            MediaKind.Photo => "🖼 photo — click to load",
            MediaKind.Video => "🎬 video — click to load",
            MediaKind.Gif => "🎞 GIF — click to load",
            MediaKind.Sticker => "✨ sticker — click to load",
            MediaKind.Voice => "🎤 voice — click to play",
            MediaKind.Audio => "🎵 audio — click to play",
            MediaKind.File => $"📄 {m.MediaLabel} — click to save",
            _ => ""
        };
        var text = m.Kind == MediaKind.Text ? m.Text : (m.Text.Length > 0 && !m.Text.StartsWith('[') ? m.Text : "");
        return new MsgRow
        {
            Id = m.Id,
            Text = text,
            Author = m.Out ? "" : m.Author,
            AuthorColor = AuthorColors[(m.Author.GetHashCode() & 0x7FFFFFFF) % AuthorColors.Length],
            Quote = m.ReplyTo > 0 ? $"↩ reply to #{m.ReplyTo} (click to jump)" : "",
            MediaHint = hint,
            Bubble = m.Out ? "#1C3A5E" : "#1A2230",
            Align = m.Out ? "Right" : "Left",
            Time = m.Date?.ToLocalTime().ToString("HH:mm") ?? ""
        };
    }

    private async void Media_Click(object sender, MouseButtonEventArgs e)
    {
        if (_bridge is null || sender is not FrameworkElement fe || fe.Tag is not int id) return;
        if (!_byId.TryGetValue(id, out var m)) return;
        switch (m.Kind)
        {
            case MediaKind.File:
                var sfd = new SaveFileDialog { FileName = string.IsNullOrEmpty(m.MediaLabel) ? "file" : m.MediaLabel };
                if (sfd.ShowDialog() != true) return;
                Status("Скачиваю файл...");
                var prog = new Progress<(long Sent, long Total)>(p =>
                    Status($"Скачиваю... {(p.Total > 0 ? p.Sent * 100 / p.Total : 0)}%"));
                var saved = await _bridge.DownloadToAsync(_peer, id, sfd.FileName, prog);
                Status(saved.Length > 0 ? $"Сохранено: {saved}" : "Не удалось скачать");
                break;
            case MediaKind.Voice:
            case MediaKind.Audio:
                Status("Скачиваю аудио...");
                var ap = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.ogg");
                var got = await _bridge.DownloadToAsync(_peer, id, ap);
                if (got.Length > 0) OpenExternal(got);
                Status("");
                break;
            default:
                Status("Загружаю превью...");
                var thumb = await _bridge.EnsureThumbAsync(_peer, id);
                Status(thumb.Length > 0 ? $"Превью: {thumb}" : "Превью не получено");
                break;
        }
    }

    private static void OpenExternal(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private async void Quote_Click(object sender, MouseButtonEventArgs e)
    {
        if (_bridge is null || sender is not FrameworkElement fe || fe.Tag is not int id) return;
        if (!_byId.TryGetValue(id, out var m) || m.ReplyTo == 0) return;
        Status($"Переход к #{m.ReplyTo}...");
        var ctx = await _bridge.GetContextAsync(_peer, m.ReplyTo, 8, 8);
        if (ctx.Count > 0) PaintFeed(ctx);
        Status("");
    }

    private void Reply_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int id) return;
        if (!_byId.TryGetValue(id, out var m)) return;
        _replyTo = id;
        var who = m.Out ? "you" : (m.Author.Length > 0 ? m.Author : _peerName);
        var prev = m.Text.Length > 0 ? m.Text : m.MediaLabel;
        ReplyLabel.Text = $"↩ {who}: {prev[..Math.Min(70, prev.Length)]}";
        ReplyBar.Visibility = Visibility.Visible;
        InputBox.Focus();
    }

    private void ReplyClear_Click(object sender, RoutedEventArgs e)
    {
        _replyTo = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        InputBox.Clear();
        var rt = _replyTo;
        _replyTo = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
        var ok = await _bridge.SendAsync(_peer, text, rt);
        Status(ok ? "" : "Не удалось отправить");
        if (ok) await LoadHistoryAsync();
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0) return;
        var ofd = new OpenFileDialog { Multiselect = true, Title = "Отправить файл" };
        if (ofd.ShowDialog() != true) return;
        var rt = _replyTo;
        _replyTo = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
        foreach (var p in ofd.FileNames)
        {
            var prog = new Progress<(long Sent, long Total)>(x =>
                Status($"Загрузка {Path.GetFileName(p)}: {(x.Total > 0 ? x.Sent * 100 / x.Total : 0)}%"));
            var ok = await _bridge.SendFileAsync(_peer, p, replyTo: rt, progress: prog);
            Status(ok ? "" : $"Не удалось отправить {Path.GetFileName(p)}");
        }
        await LoadHistoryAsync();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }
}

