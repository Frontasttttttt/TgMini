using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TgMini.Core;
using TgMini.Core.Models;

namespace TgMini.Desktop;

public partial class MainWindow : Window
{
    private static readonly string[] AuthorColors =
        { "#2EA6FF", "#7C5CFF", "#00C48C", "#FF9F0A", "#FF5C7A", "#64D2FF", "#E6A23C", "#9ECE6A" };

    private static readonly string[] AvatarColors =
        { "#E17076", "#FAA356", "#A371F7", "#54A0FF", "#00D2D3", "#1DD1A1", "#576574", "#FF6B6B" };

    private readonly AppConfig _cfg = AppConfig.Load();
    private WTelegramBridge? _bridge;

    private readonly ObservableCollection<DialogRow> _rows = new();
    private readonly ObservableCollection<MsgRow> _feed = new();
    private readonly Dictionary<long, DialogRow> _rowCache = new();
    private readonly HashSet<long> _avatarRequested = new();

    private List<DialogInfo> _all = new();
    private List<ChatFolder> _folders = new();
    private Dictionary<int, MessageInfo> _byId = new();
    private readonly List<(string Key, Button Btn)> _folderButtons = new();
    private string _folder = "all";
    private long _peer;
    private string _peerName = "";
    private int _replyTo;
    private int _oldestMsgId;
    private bool _hasMoreHistory = true;
    private bool _isLoadingHistory;
    private bool _isCompact;

    private DateTime _lastUserActivity = DateTime.UtcNow;
    private bool _trimmedForCurrentIdle;

    private void RecordActivity()
    {
        _lastUserActivity = DateTime.UtcNow;
        _trimmedForCurrentIdle = false;
    }

    public MainWindow()
    {
        InitializeComponent();
        DialogList.ItemsSource = _rows;
        FeedList.ItemsSource = _feed;

        DialogList.SelectionChanged += DialogList_SelectionChanged;
        SearchBox.TextChanged += (_, _) => { RecordActivity(); PaintDialogs(); };

        AddHandler(InputElement.PointerMovedEvent, (_, _) => RecordActivity(), RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, (_, _) => RecordActivity(), RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerPressedEvent, (_, _) => RecordActivity(), RoutingStrategies.Tunnel);

        FeedScroll.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(ScrollViewer.Offset))
            {
                var sv = FeedScroll;
                bool isNearBottom = sv.Offset.Y >= (sv.Extent.Height - sv.Viewport.Height - 120);
                ScrollDownBtn.IsVisible = !isNearBottom && _feed.Count > 5;
            }
        };

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => { try { _bridge?.Dispose(); } catch { } };
        PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(WindowState) && WindowState == WindowState.Minimized)
            {
                ThumbConverter.ClearCache();
                MemoryHelper.TrimWorkingSetAsync(full: true);
            }
        };

        // Периодическая проверка простоя: если пользователь неактивен 30 секунд,
        // производим однократную очистку кэша аватарок и фоновый неблокирующий сброс памяти
        var idleCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        idleCheckTimer.Tick += (_, _) =>
        {
            if (!_trimmedForCurrentIdle && DateTime.UtcNow - _lastUserActivity >= TimeSpan.FromSeconds(30))
            {
                _trimmedForCurrentIdle = true;
                MemoryHelper.TrimWorkingSetAsync(full: true);
            }
        };
        idleCheckTimer.Start();
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

    private void Status(string s) => Dispatcher.UIThread.Invoke(() => StatusLabel.Text = s);

    private int _passwordAttempts;

    private static string FormatTelegramError(string error)
    {
        var match = System.Text.RegularExpressions.Regex.Match(error, @"FLOOD_WAIT_(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
        {
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
                return $"Telegram временно ограничил вход (FLOOD_WAIT). Подождите {(int)span.TotalHours} ч {span.Minutes} мин.";
            if (span.TotalMinutes >= 1)
                return $"Telegram временно ограничил вход (FLOOD_WAIT). Подождите {(int)span.TotalMinutes} мин {span.Seconds} сек.";
            return $"Подождите {seconds} сек. перед повторной попыткой (FLOOD_WAIT).";
        }
        if (error.Contains("AUTH_KEY_UNREGISTERED") || error.Contains("SESSION_REVOKED"))
            return "Сессия Telegram была завершена или отозвана.";
        if (error.Contains("PASSWORD_HASH_INVALID"))
            return "Неверный двухфакторный пароль (2FA).";
        return error;
    }

    private async Task StartAsync()
    {
        ReloginBtn.IsVisible = false;
        Interlocked.Exchange(ref _passwordAttempts, 0);

        if (Environment.GetEnvironmentVariable("TGMINI_NO_CONNECT") == "1")
        {
            Status("UI-режим без подключения (TGMINI_NO_CONNECT=1)");
            return;
        }

        if (_cfg.Telegram.ApiId == 0 || string.IsNullOrWhiteSpace(_cfg.Telegram.ApiHash))
        {
            var setup = new SetupWindow(_cfg);
            await setup.ShowDialog(this);
        }

        // Диспоузим предыдущий мост, чтобы закрыть старый Client, сокеты и отпустить файл сессии
        if (_bridge is not null)
        {
            try { _bridge.Dispose(); } catch { }
            _bridge = null;
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
                if (!ok)
                {
                    ReloginBtn.IsVisible = true;
                }
                return;
            }

            await bridge.ConnectAsync();
            Status("Загружаю чаты...");
            await RefreshAsync();
            Status($"Готово. Чатов: {_all.Count}, папок: {_folders.Count}");
        }
        catch (Exception e)
        {
            var msg = FormatTelegramError(e.Message);
            if (e.Message.Contains("AUTH_KEY_UNREGISTERED") || e.Message.Contains("SESSION_REVOKED"))
            {
                Status($"Сессия недействительна ({msg}). Открываю повторный вход...");
                try { bridge.Dispose(); } catch { }
                bridge.ResetSession();
                var ok = await QrLoginAsync(bridge);
                ReloginBtn.IsVisible = !ok;
            }
            else
            {
                Status($"Ошибка Telegram: {msg}");
                ReloginBtn.IsVisible = true;
            }
        }
    }

    private async void Relogin_Click(object? sender, RoutedEventArgs e)
    {
        ReloginBtn.IsVisible = false;
        if (_bridge is not null)
        {
            try { _bridge.Dispose(); } catch { }
            _bridge.ResetSession();
            _bridge = null;
        }
        await StartAsync();
    }

    private async Task<bool> QrLoginAsync(WTelegramBridge bridge)
    {
        Interlocked.Exchange(ref _passwordAttempts, 0);
        var qr = new QrWindow();
        qr.Show(this);

        try
        {
            var ok = await ((IBridge)bridge).LoginQrAsync(
                qrPng => qr.ShowQr(qrPng),
                qr.Token);

            if (ok)
            {
                qr.Finish(true, "Вход успешен!");
                await RefreshAsync();
                Status($"Вход выполнен. Чатов: {_all.Count}");
                ReloginBtn.IsVisible = false;
                return true;
            }
            else
            {
                qr.Finish(false, "QR отменён.");
                Status("QR-вход отменён.");
                return false;
            }
        }
        catch (Exception ex)
        {
            var formatted = FormatTelegramError(ex.Message);
            qr.Finish(false, $"Ошибка: {formatted}");
            Status($"Ошибка входа: {formatted}");
            return false;
        }
    }

    private string? AskPrompt(string what)
    {
        string? err = null;
        if (what == "password")
        {
            var attempts = Interlocked.Increment(ref _passwordAttempts);
            if (attempts > 3)
            {
                Status("Слишком много неверных попыток ввода пароля (3). Вход остановлен во избежание блокировки.");
                return null;
            }
            if (attempts > 1)
            {
                err = $"Неверный пароль 2FA! Попытка {attempts} из 3.";
            }
        }

        var tcs = new TaskCompletionSource<string?>();

        if (Dispatcher.UIThread.CheckAccess())
        {
            var frame = new DispatcherFrame();
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var dialog = new VerificationWindow(what, err);
                    var res = await dialog.ShowDialog<bool>(this);
                    tcs.TrySetResult(res && !string.IsNullOrEmpty(dialog.Value) ? dialog.Value : null);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
                finally
                {
                    frame.Continue = false;
                }
            });
            Dispatcher.UIThread.PushFrame(frame);
            return tcs.Task.GetAwaiter().GetResult();
        }
        else
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var dialog = new VerificationWindow(what, err);
                    var res = await dialog.ShowDialog<bool>(this);
                    tcs.TrySetResult(res && !string.IsNullOrEmpty(dialog.Value) ? dialog.Value : null);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            });

            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }
    }

    private void UpdateHeaderAvatar(DialogRow r)
    {
        try
        {
            HeaderAvatar.IsVisible = true;
            HeaderAvatarInitials.Text = r.Initials;
            HeaderAvatarBg.Background = new SolidColorBrush(Color.Parse(r.AvatarColor));
            if (r.AvatarVisible && !string.IsNullOrEmpty(r.AvatarPath))
            {
                var bmp = ThumbConverter.Instance.Convert(r.AvatarPath, typeof(Avalonia.Media.IImage), 48, System.Globalization.CultureInfo.InvariantCulture) as Avalonia.Media.IImage;
                if (bmp != null)
                {
                    HeaderAvatarPhoto.Source = bmp;
                    HeaderAvatarPhoto.IsVisible = true;
                    return;
                }
            }
            HeaderAvatarPhoto.IsVisible = false;
        }
        catch
        {
            HeaderAvatarPhoto.IsVisible = false;
        }
    }

    private void ScrollDownBtn_Click(object? sender, RoutedEventArgs e)
    {
        RecordActivity();
        FeedScroll.ScrollToEnd();
        ScrollDownBtn.IsVisible = false;
    }

    private void SearchClearBtn_Click(object? sender, RoutedEventArgs e)
    {
        RecordActivity();
        SearchBox.Text = "";
    }

    private void OnLiveMessage(IncomingMessage m)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var d = _all.FirstOrDefault(x => x.Id == m.ChatId);
            if (d is not null)
            {
                var idx = _all.IndexOf(d);
                var unread = d.Unread + (m.ChatId == _peer ? 0 : 1);
                _all[idx] = d with { LastMessage = m.Text, Unread = unread };
                if (_rowCache.TryGetValue(m.ChatId, out var row))
                {
                    row.Preview = m.Text;
                    row.Unread = unread;
                    row.Time = TimeFormatter.FormatDialogTime(m.Date);
                }
            }

            if (m.ChatId == _peer)
            {
                var row = new MsgRow
                {
                    Id = m.MsgId,
                    Text = m.Text,
                    Author = m.Name,
                    AuthorColor = AuthorColors[(m.Name.GetHashCode() & 0x7FFFFFFF) % AuthorColors.Length],
                    Bubble = "#1A2230",
                    Align = "Left",
                    Time = DateTime.Now.ToString("HH:mm"),
                    Out = false
                };
                _feed.Add(row);
                FeedScroll.ScrollToEnd();
            }
        });
    }

    private void TogglePanel_Click(object? sender, RoutedEventArgs e)
    {
        _isCompact = !_isCompact;
        MainGrid.ColumnDefinitions[0].Width = _isCompact ? new GridLength(0) : new GridLength(280);
        TogglePanelBtn.Content = _isCompact ? "⮞" : "☰";
        ToolTip.SetTip(TogglePanelBtn, _isCompact ? "Развернуть список чатов" : "Компактный режим (скрыть чаты)");
    }

    private async Task RefreshAsync()
    {
        if (_bridge is null) return;
        _all = (await _bridge.GetDialogsAsync(200)).ToList();
        _folders = (await _bridge.GetFoldersAsync()).ToList();
        BuildFolderButtons();
        PaintDialogs();
        MemoryHelper.TrimWorkingSet();
    }

    private void BuildFolderButtons()
    {
        FolderBar.Children.Clear();
        _folderButtons.Clear();
        AddFolderButton("all", "Все");
        foreach (var f in _folders)
            AddFolderButton($"tg:{f.Id}", string.IsNullOrWhiteSpace(f.Title) ? $"#{f.Id}" : f.Title);
        var archivedCount = _all.Count(d => d.Archived);
        if (archivedCount > 0)
            AddFolderButton("archive", $"Архив ({archivedCount})");

        UpdateFolderButtonsVisual();
    }

    private void AddFolderButton(string key, string title)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold },
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(11),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        b.Click += (_, _) => SelectFolder(key);
        _folderButtons.Add((key, b));
        FolderBar.Children.Add(b);
    }

    private void SelectFolder(string key)
    {
        if (_folder == key) return;
        _folder = key;
        UpdateFolderButtonsVisual();
        PaintDialogs();
    }

    private void UpdateFolderButtonsVisual()
    {
        var activeBrush = new SolidColorBrush(Color.Parse("#2EA6FF"));
        var inactiveBrush = new SolidColorBrush(Color.Parse("#1E2936"));
        var inactiveText = new SolidColorBrush(Color.Parse("#708499"));
        foreach (var (k, b) in _folderButtons)
        {
            b.Background = k == _folder ? activeBrush : inactiveBrush;
            b.Foreground = k == _folder ? Brushes.White : inactiveText;
        }
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

    private DialogRow GetOrCreateRow(DialogInfo d)
    {
        if (!_rowCache.TryGetValue(d.Id, out var row))
        {
            row = new DialogRow
            {
                Id = d.Id,
                Name = d.Name,
                Initials = GetInitials(d.Name),
                AvatarColor = GetAvatarColor(d.Name),
                Preview = d.LastMessage,
                Unread = d.Unread,
                IsPinned = d.IsPinned,
                Time = TimeFormatter.FormatDialogTime(d.Date),
                IsMuted = d.Muted
            };
            _rowCache[d.Id] = row;
        }
        else
        {
            row.Preview = d.LastMessage;
            row.Unread = d.Unread;
            row.IsPinned = d.IsPinned;
            row.Time = TimeFormatter.FormatDialogTime(d.Date);
            row.IsMuted = d.Muted;
        }

        if (string.IsNullOrEmpty(row.AvatarPath))
        {
            var cached = MediaCache.GetCachedAvatar(d.Id);
            if (cached is not null)
            {
                row.AvatarPath = cached;
            }
            else if (d.Name is not ("Избранное" or "Saved Messages") && _avatarRequested.Add(d.Id))
            {
                var peerId = d.Id;
                _ = Task.Run(async () =>
                {
                    if (_bridge is null) return;
                    var path = await _bridge.EnsureAvatarAsync(peerId);
                    if (!string.IsNullOrEmpty(path))
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            if (_rowCache.TryGetValue(peerId, out var targetRow))
                                targetRow.AvatarPath = path;
                        });
                    }
                });
            }
        }

        return row;
    }

    private void PaintDialogs()
    {
        var q = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        if (SearchClearBtn is not null)
        {
            SearchClearBtn.IsVisible = !string.IsNullOrEmpty(SearchBox.Text);
        }
        var flt = CurrentFolder();
        var target = new List<DialogRow>(capacity: 60);

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

            var row = GetOrCreateRow(d);
            target.Add(row);
            if (target.Count >= 60) break;
        }

        if (_rows.Count == target.Count && _rows.SequenceEqual(target)) return;

        _rows.Clear();
        foreach (var r in target)
        {
            _rows.Add(r);
        }
    }

    private async void DialogList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        RecordActivity();
        _feed.Clear();
        _byId.Clear();
        ThumbConverter.ClearCache();
        MemoryHelper.TrimWorkingSetAsync(full: false);

        _peer = r.Id;
        _peerName = r.Name;
        ChatTitle.Text = _peerName;
        ChatSub.Text = "";
        _replyTo = 0;
        ReplyBar.IsVisible = false;
        UpdateHeaderAvatar(r);

        var d = _all.FirstOrDefault(x => x.Id == _peer);
        if (d is not null && d.Unread > 0)
        {
            var idx = _all.IndexOf(d);
            _all[idx] = d with { Unread = 0 };
            r.Unread = 0;
            PaintDialogs();
        }

        _oldestMsgId = 0;
        _hasMoreHistory = true;
        await LoadHistoryAsync();
        if (_bridge is not null) await _bridge.MarkReadAsync(_peer);
    }

    private async Task LoadHistoryAsync()
    {
        if (_bridge is null || _peer == 0) return;
        try
        {
            var msgs = await _bridge.GetHistoryAsync(_peer, 60, offsetId: 0);
            _hasMoreHistory = msgs.Count >= 60;
            LoadMoreBtn.IsVisible = _hasMoreHistory;
            if (msgs.Count > 0) _oldestMsgId = msgs[0].Id;
            PaintFeed(msgs);
            ChatSub.Text = $"{msgs.Count} сообщений";
        }
        catch (Exception e) { Status($"history err: {e.Message}"); }
    }

    private async void LoadMore_Click(object? sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0 || _isLoadingHistory || !_hasMoreHistory || _oldestMsgId == 0) return;
        _isLoadingHistory = true;
        LoadMoreBtn.IsEnabled = false;
        LoadMoreBtn.Content = "⏳ Загрузка...";
        try
        {
            var older = await _bridge.GetHistoryAsync(_peer, 60, offsetId: _oldestMsgId);
            if (older.Count == 0)
            {
                _hasMoreHistory = false;
                LoadMoreBtn.IsVisible = false;
            }
            else
            {
                _oldestMsgId = older[0].Id;
                _hasMoreHistory = older.Count >= 60;
                LoadMoreBtn.IsVisible = _hasMoreHistory;

                foreach (var m in older) _byId[m.Id] = m;

                for (var i = older.Count - 1; i >= 0; i--)
                {
                    var m = older[i];
                    var row = ToRow(m);
                    _feed.Insert(0, row);
                    EnsureMediaThumb(row, m);
                }
                ChatSub.Text = $"{_feed.Count} сообщений";
            }
        }
        catch (Exception ex)
        {
            Status($"load more err: {ex.Message}");
        }
        finally
        {
            _isLoadingHistory = false;
            LoadMoreBtn.IsEnabled = true;
            LoadMoreBtn.Content = "⬆ Загрузить более ранние сообщения";
        }
    }

    private void EnsureMediaThumb(MsgRow row, MessageInfo m)
    {
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
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            if (row.Id == msgId) row.ImagePath = path;
                        });
                    }
                });
            }
        }
    }

    private void PaintFeed(IReadOnlyList<MessageInfo> msgs)
    {
        _byId = msgs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());
        _feed.Clear();
        DateTime? lastDate = null;
        foreach (var m in msgs)
        {
            var msgDate = m.Date?.ToLocalTime().Date;
            string dateSep = "";
            if (msgDate.HasValue && msgDate != lastDate)
            {
                dateSep = TimeFormatter.FormatDateSeparator(m.Date);
                lastDate = msgDate;
            }
            var row = ToRow(m, dateSep);
            _feed.Add(row);
            EnsureMediaThumb(row, m);
        }
        FeedScroll.ScrollToEnd();
    }

    private static MsgRow ToRow(MessageInfo m, string dateSeparator = "")
    {
        string hint;
        if (m.Kind is MediaKind.Photo or MediaKind.Sticker or MediaKind.Gif)
        {
            hint = "";
        }
        else
        {
            hint = m.Kind switch
            {
                MediaKind.Voice => $"🎤 {m.MediaLabel}",
                MediaKind.Audio => $"🎵 {m.MediaLabel}",
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
            Quote = m.ReplyTo > 0 ? $"↩ Ответ на #{m.ReplyTo}" : "",
            MediaHint = hint,
            Bubble = m.Out ? "#2B5278" : "#182533",
            Align = m.Out ? "Right" : "Left",
            Time = m.Date?.ToLocalTime().ToString("HH:mm") ?? "",
            Out = m.Out,
            HasMedia = m.Kind != MediaKind.Text,
            DateSeparator = dateSeparator
        };
    }

    private async void Media_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bridge is null || sender is not Control c || c.Tag is not int id) return;
        if (!_byId.TryGetValue(id, out var m)) return;

        switch (m.Kind)
        {
            case MediaKind.Voice:
            case MediaKind.Audio:
                Status("Скачиваю аудио...");
                var ext = m.Kind == MediaKind.Voice ? "ogg" : "mp3";
                var ap = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.{ext}");
                string gotAudio = ap;
                if (!File.Exists(ap) || new FileInfo(ap).Length == 0)
                {
                    gotAudio = await _bridge.DownloadToAsync(_peer, id, ap);
                }
                if (!string.IsNullOrEmpty(gotAudio) && File.Exists(gotAudio))
                {
                    OpenExternal(gotAudio);
                    Status("");
                }
                else Status("Не удалось скачать аудио");
                break;

            case MediaKind.RoundVideo:
            case MediaKind.Video:
                Status("Скачиваю видео...");
                var vp = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.mp4");
                var gotVideo = await _bridge.DownloadToAsync(_peer, id, vp);
                if (gotVideo.Length > 0) OpenExternal(gotVideo);
                Status("");
                break;

            case MediaKind.File:
                Status("Скачиваю файл...");
                var fp = Path.Combine(Path.GetTempPath(), string.IsNullOrEmpty(m.MediaLabel) ? $"file_{id}" : m.MediaLabel);
                var saved = await _bridge.DownloadToAsync(_peer, id, fp);
                if (saved.Length > 0) OpenExternal(saved);
                Status("");
                break;

            default:
                var thumb = await _bridge.EnsureThumbAsync(_peer, id);
                if (thumb.Length > 0) OpenExternal(thumb);
                break;
        }
    }

    private static void OpenExternal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // On Linux fallback to xdg-open
            try
            {
                Process.Start("xdg-open", path);
            }
            catch { /* ignore */ }
        }
    }

    private void CancelReply_Click(object? sender, RoutedEventArgs e)
    {
        _replyTo = 0;
        ReplyBar.IsVisible = false;
    }

    private void MsgBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }

    private async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0) return;
        var text = MsgBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        MsgBox.Text = "";
        try
        {
            var ok = await _bridge.SendAsync(_peer, text, _replyTo);
            _replyTo = 0;
            ReplyBar.IsVisible = false;

            if (ok)
            {
                var row = new MsgRow
                {
                    Id = Environment.TickCount,
                    Text = text,
                    Author = "",
                    Bubble = "#1C3A5E",
                    Align = "Right",
                    Time = DateTime.Now.ToString("HH:mm"),
                    Out = true
                };
                _feed.Add(row);
                FeedScroll.ScrollToEnd();
                Status("");
            }
            else
            {
                Status("Не удалось отправить сообщение");
            }
        }
        catch (Exception ex)
        {
            Status($"Ошибка отправки: {ex.Message}");
        }
    }

    private async void Attach_Click(object? sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите файл для отправки",
            AllowMultiple = false
        });

        if (files.Count == 0) return;
        var file = files[0];
        var localPath = file.Path.LocalPath;

        Status($"Отправляю {Path.GetFileName(localPath)}...");
        try
        {
            var prog = new Progress<(long Sent, long Total)>(p =>
                Status($"Отправка... {(p.Total > 0 ? p.Sent * 100 / p.Total : 0)}%"));

            var ok = await _bridge.SendFileAsync(_peer, localPath, caption: "", replyTo: 0, progress: prog);
            if (ok)
            {
                Status("Файл отправлен");
                var row = new MsgRow
                {
                    Id = Environment.TickCount,
                    Text = Path.GetFileName(localPath),
                    MediaHint = $"📄 {Path.GetFileName(localPath)}",
                    Bubble = "#1C3A5E",
                    Align = "Right",
                    Time = DateTime.Now.ToString("HH:mm"),
                    Out = true,
                    HasMedia = true
                };
                _feed.Add(row);
                FeedScroll.ScrollToEnd();
            }
            else
            {
                Status("Не удалось отправить файл");
            }
        }
        catch (Exception ex)
        {
            Status($"Ошибка отправки файла: {ex.Message}");
        }
    }
}
