using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    public string Initials { get; init; } = "";
    public string AvatarColor { get; init; } = "#2EA6FF";
    public string Preview { get; init; } = "";
    public int Unread { get; set; }
    public bool IsPinned { get; init; }
    public string PinnedVisible => IsPinned ? "Visible" : "Collapsed";
    public string NameWeight => Unread > 0 ? "Bold" : "Normal";
    public string BadgeVisible => Unread > 0 ? "Visible" : "Collapsed";
}

public sealed class MsgRow : INotifyPropertyChanged
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

    private string _imagePath = "";
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (_imagePath != value)
            {
                _imagePath = value;
                OnPropertyChanged(nameof(ImagePath));
                OnPropertyChanged(nameof(ImageVisible));
            }
        }
    }
    public string ImageVisible => !string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath) ? "Visible" : "Collapsed";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private static readonly string[] AuthorColors =
        { "#2EA6FF", "#7C5CFF", "#00C48C", "#FF9F0A", "#FF5C7A", "#64D2FF", "#E6A23C", "#9ECE6A" };

    private static readonly string[] AvatarColors =
        { "#E17076", "#FAA356", "#A371F7", "#54A0FF", "#00D2D3", "#1DD1A1", "#576574", "#FF6B6B" };

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

    private TrayHelper? _tray;
    private bool _isQuitting;

    public MainWindow()
    {
        InitializeComponent();
        DialogList.ItemsSource = _rows;
        FeedList.ItemsSource = _feed;
        Loaded += async (_, _) =>
        {
            InitTray();
            await StartAsync();
        };
    }

    private void InitTray()
    {
        if (!_cfg.Tray.Enabled) return;
        try
        {
            _tray = new TrayHelper(this);
            _tray.OpenRequested += () => Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
            _tray.ExitRequested += () => Dispatcher.Invoke(() =>
            {
                _isQuitting = true;
                try { _tray?.Dispose(); } catch { }
                try { Application.Current.Shutdown(); } catch { }
                Environment.Exit(0);
            });
            _tray.Init();
        }
        catch { /* ignore */ }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _cfg.Tray.Enabled)
        {
            Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting && _cfg.Tray.Enabled)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        try { _tray?.Dispose(); } catch { }
        base.OnClosing(e);
        try { Application.Current.Shutdown(); } catch { }
        Environment.Exit(0);
    }

    private static string GetInitials(string name)
    {
        if (name is "Избранное" or "Saved Messages") return "⭐";
        if (string.IsNullOrWhiteSpace(name)) return "TG";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            var first = char.ToUpperInvariant(parts[0][0]);
            var second = char.ToUpperInvariant(parts[1][0]);
            return $"{first}{second}";
        }
        var clean = name.Trim();
        return clean.Length >= 2 ? clean[..2].ToUpperInvariant() : clean.ToUpperInvariant();
    }

    private static string GetAvatarColor(string name)
    {
        if (string.IsNullOrEmpty(name)) return AvatarColors[0];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(name));
        var idx = (BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF) % AvatarColors.Length;
        return AvatarColors[idx];
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
                var unread = _peer == m.ChatId || m.Out ? 0 : d.Unread + 1;
                _all.Insert(0, d with { LastMessage = m.Text, Unread = unread, Date = m.Date });
                PaintDialogs();
            }
            if (_peer == m.ChatId)
            {
                _ = LoadHistoryAsync();
                if (_bridge is not null && !m.Out) _ = _bridge.MarkReadAsync(_peer);
            }

            // Показываем оверлей и играем звук только для входящих чужих сообщений
            if (!m.Out && NotifyFilter.ShouldShow(_cfg.Filter, m.Author, m.SenderId, null, m.IsGroup, m.Mentioned, false))
            {
                PlaySoundAndNotify(m);
            }
        });
    }

    private void PlaySoundAndNotify(IncomingMessage m)
    {
        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch { /* ignore */ }

        try
        {
            var notif = new NotificationWindow(
                m.ChatId,
                m.Name,
                m.Text,
                GetInitials(m.Name),
                GetAvatarColor(m.Name),
                onOpen: chatId => Dispatcher.Invoke(() => SelectChat(chatId)));
            notif.Show();
        }
        catch { /* ignore */ }
    }

    private void SelectChat(long chatId)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        var row = _rows.FirstOrDefault(r => r.Id == chatId);
        if (row is not null)
        {
            DialogList.SelectedItem = row;
        }
        else
        {
            var d = _all.FirstOrDefault(x => x.Id == chatId);
            if (d is not null)
            {
                _peer = d.Id;
                _peerName = d.Name;
                ChatTitle.Text = _peerName;
                _ = LoadHistoryAsync();
            }
        }
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
        AddFolderButton("all", "Все");
        foreach (var f in _folders)
            AddFolderButton($"tg:{f.Id}", string.IsNullOrWhiteSpace(f.Title) ? $"#{f.Id}" : f.Title);
        var archivedCount = _all.Count(d => d.Archived);
        if (archivedCount > 0)
            AddFolderButton("archive", $"Архив ({archivedCount})");
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
        if (_folder == "all" || _folder == "archive") return null;
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
            if (_folder == "archive")
            {
                if (!d.Archived) continue;
            }
            else if (_folder == "all")
            {
                if (d.Archived) continue;
            }
            else
            {
                if (!FolderMatcher.Match(d.Id, KindStr(d.Kind), d.Unread, d.Muted, d.Archived, flt)) continue;
            }

            _rows.Add(new DialogRow
            {
                Id = d.Id,
                Name = d.Name,
                Initials = GetInitials(d.Name),
                AvatarColor = GetAvatarColor(d.Name),
                Preview = d.LastMessage,
                Unread = d.Unread,
                IsPinned = d.IsPinned
            });
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

        var d = _all.FirstOrDefault(x => x.Id == _peer);
        if (d is not null && d.Unread > 0)
        {
            var idx = _all.IndexOf(d);
            _all[idx] = d with { Unread = 0 };
            r.Unread = 0;
            PaintDialogs();
        }

        await LoadHistoryAsync();
        if (_bridge is not null) await _bridge.MarkReadAsync(_peer);
    }

    private async Task LoadHistoryAsync()
    {
        if (_bridge is null || _peer == 0) return;
        try
        {
            var msgs = await _bridge.GetHistoryAsync(_peer, 30);
            PaintFeed(msgs);
            ChatSub.Text = $"{msgs.Count} сообщений";
        }
        catch (Exception e) { Status($"history err: {e.Message}"); }
    }

    private void PaintFeed(IReadOnlyList<MessageInfo> msgs)
    {
        _byId = msgs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());
        _feed.Clear();
        foreach (var m in msgs)
        {
            var row = ToRow(m);
            _feed.Add(row);

            if (m.Kind is MediaKind.Photo or MediaKind.Sticker or MediaKind.Gif or MediaKind.RoundVideo or MediaKind.Video)
            {
                var cachedThumb = MediaCache.ThumbPath(m.ChatId, m.Id);
                if (File.Exists(cachedThumb))
                {
                    row.ImagePath = cachedThumb;
                }
                else
                {
                    var peerId = _peer;
                    var msgId = m.Id;
                    _ = Task.Run(async () =>
                    {
                        if (_bridge is null) return;
                        var path = await _bridge.EnsureThumbAsync(peerId, msgId);
                        if (!string.IsNullOrEmpty(path))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (row.Id == msgId) row.ImagePath = path;
                            });
                        }
                    });
                }
            }
        }
        if (_feed.Count > 0) FeedList.ScrollIntoView(_feed[^1]);
    }

    private MsgRow ToRow(MessageInfo m)
    {
        string hint;
        if (m.Kind is MediaKind.Photo or MediaKind.Sticker or MediaKind.Gif)
        {
            // Картинки и стикеры видны прямо в чате — убираем лишний текст
            hint = "";
        }
        else
        {
            hint = m.Kind switch
            {
                MediaKind.Voice => $"🎤 Голосовое {m.MediaLabel}",
                MediaKind.Audio => $"🎵 Аудио {m.MediaLabel}",
                MediaKind.RoundVideo => "⭕ Кружочек",
                MediaKind.Video => "🎬 Видео",
                MediaKind.File => $"📄 {m.MediaLabel} — скачать",
                _ => ""
            };
        }
        var text = m.Kind == MediaKind.Text ? m.Text : (m.Text.Length > 0 && !m.Text.StartsWith('[') ? m.Text : "");
        return new MsgRow
        {
            Id = m.Id,
            Text = text,
            Author = m.Out ? "" : m.Author,
            AuthorColor = AuthorColors[(m.Author.GetHashCode() & 0x7FFFFFFF) % AuthorColors.Length],
            Quote = m.ReplyTo > 0 ? $"↩ Ответ на #{m.ReplyTo} (клик для перехода)" : "",
            MediaHint = hint,
            Bubble = m.Out ? "#1C3A5E" : "#1A2230",
            Align = m.Out ? "Right" : "Left",
            Time = m.Date?.ToLocalTime().ToString("HH:mm") ?? ""
        };
    }

    private void Image_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int id) return;
        var thumb = MediaCache.ThumbPath(_peer, id);
        if (File.Exists(thumb)) OpenExternal(thumb);
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
                var gotAudio = await _bridge.DownloadToAsync(_peer, id, ap);
                if (gotAudio.Length > 0) OpenExternal(gotAudio);
                Status("");
                break;
            case MediaKind.RoundVideo:
            case MediaKind.Video:
                Status("Скачиваю видео...");
                var vp = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.mp4");
                var gotVideo = await _bridge.DownloadToAsync(_peer, id, vp);
                if (gotVideo.Length > 0) OpenExternal(gotVideo);
                Status("");
                break;
            default:
                Status("Загружаю превью...");
                var thumb = await _bridge.EnsureThumbAsync(_peer, id);
                if (thumb.Length > 0)
                {
                    Status("Превью готово");
                    OpenExternal(thumb);
                }
                else Status("Превью не получено");
                break;
        }
    }

    private static void OpenExternal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{path}\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                catch { /* ignore */ }
            }
        }
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
        var who = m.Out ? "вы" : (m.Author.Length > 0 ? m.Author : _peerName);
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
        if (!ok)
        {
            InputBox.Text = text;
            Status("Не удалось отправить сообщение (проверьте права в чате)");
        }
        else
        {
            Status("");
            await LoadHistoryAsync();
        }
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
            if (!ok) Status($"Не удалось отправить {Path.GetFileName(p)}");
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

    private void DialogArchive_Click(object sender, RoutedEventArgs e)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        var d = _all.FirstOrDefault(x => x.Id == r.Id);
        if (d is null) return;
        var idx = _all.IndexOf(d);
        var newArchived = !d.Archived;
        _all[idx] = d with { Archived = newArchived };
        BuildFolderButtons();
        PaintDialogs();
        Status(newArchived ? $"Чат '{d.Name}' перемещён в архив" : $"Чат '{d.Name}' извлечён из архива");
    }

    private void DialogMute_Click(object sender, RoutedEventArgs e)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        var d = _all.FirstOrDefault(x => x.Id == r.Id);
        if (d is null) return;
        var idx = _all.IndexOf(d);
        var newMuted = !d.Muted;
        _all[idx] = d with { Muted = newMuted };
        PaintDialogs();
        Status(newMuted ? $"Чат '{d.Name}' заглушён" : $"Звук чата '{d.Name}' включён");
    }
}
