using System.Runtime.InteropServices;
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

public sealed class DialogRow : INotifyPropertyChanged
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string AvatarColor { get; init; } = "#2EA6FF";

    private string _preview = "";
    public string Preview
    {
        get => _preview;
        set
        {
            if (_preview != value)
            {
                _preview = value;
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    private int _unread;
    public int Unread
    {
        get => _unread;
        set
        {
            if (_unread != value)
            {
                _unread = value;
                OnPropertyChanged(nameof(Unread));
                OnPropertyChanged(nameof(NameWeight));
                OnPropertyChanged(nameof(BadgeVisible));
            }
        }
    }

    private bool _isPinned;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged(nameof(IsPinned));
                OnPropertyChanged(nameof(PinnedVisible));
            }
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(MutedVisible));
                OnPropertyChanged(nameof(BadgeBackground));
            }
        }
    }

    private string _time = "";
    public string Time
    {
        get => _time;
        set
        {
            if (_time != value)
            {
                _time = value;
                OnPropertyChanged(nameof(Time));
            }
        }
    }

    public string MutedVisible => IsMuted ? "Visible" : "Collapsed";
    public string BadgeBackground => IsMuted ? "#708499" : "#2EA6FF";
    public string PinnedVisible => IsPinned ? "Visible" : "Collapsed";
    public string NameWeight => Unread > 0 ? "Bold" : "Normal";
    public string BadgeVisible => Unread > 0 ? "Visible" : "Collapsed";

    private string _avatarPath = "";
    public string AvatarPath
    {
        get => _avatarPath;
        set
        {
            if (_avatarPath != value)
            {
                _avatarPath = value;
                OnPropertyChanged(nameof(AvatarPath));
                OnPropertyChanged(nameof(AvatarVisible));
            }
        }
    }
    public string AvatarVisible => !string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath) ? "Visible" : "Collapsed";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    public bool Out { get; init; }
    public string CanEdit => Out ? "Visible" : "Collapsed";
    public bool HasMedia { get; init; }
    public string MediaActionsVisible => HasMedia ? "Visible" : "Collapsed";
    public string DateSeparator { get; init; } = "";
    public string DateSeparatorVisible => !string.IsNullOrEmpty(DateSeparator) ? "Visible" : "Collapsed";
    public string BubbleRadius => Out ? "14,14,4,14" : "14,14,14,4";
    public string StatusIcon => Out ? "✓✓" : "";
    public string StatusVisible => Out ? "Visible" : "Collapsed";
    public string TimeColor => Out ? "#A0C2E8" : "#708499";

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
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int trueValue = 1;
            if (DwmSetWindowAttribute(handle, 20, ref trueValue, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref trueValue, sizeof(int));
            }
        }
        catch { /* ignore */ }
    }
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
    private readonly Dictionary<long, DialogRow> _rowCache = new();
    private readonly HashSet<long> _avatarRequested = new();
    private readonly List<(string Key, Button Btn)> _folderButtons = new();
    private string _folder = "all";
    private long _peer;
    private string _peerName = "";
    private int _replyTo;
    private int _editMsgId = 0;
    private int _oldestMsgId = 0;
    private bool _hasMoreHistory = true;
    private bool _isLoadingHistory = false;
    private MediaPlayer? _mediaPlayer;
    private int _playingVoiceId;
    private bool _isCompact;

    private TrayHelper? _tray;
    private bool _isQuitting;

    public MainWindow()
    {
        InitializeComponent();
        ApplyAppIcon();
        DialogList.ItemsSource = _rows;
        FeedList.ItemsSource = _feed;
        FeedList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(FeedList_ScrollChanged));
        Loaded += async (_, _) =>
        {
            InitTray();
            await StartAsync();
        };
    }

    private void ApplyAppIcon()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                }
            }
        }
        catch { /* ignore */ }
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
                try { _bridge?.Dispose(); } catch { }
                try { _tray?.Dispose(); } catch { }
                try { Application.Current.Shutdown(); } catch { }
                Environment.Exit(0);
            });
            _tray.Init();
        }
        catch { /* ignore */ }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting && _cfg.Tray.Enabled)
        {
            e.Cancel = true;
            Hide();
            MemoryHelper.TrimWorkingSet();
            return;
        }
        StopVoicePlayer();
        try { _bridge?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        base.OnClosing(e);
        try { Application.Current.Shutdown(); } catch { }
        Environment.Exit(0);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            MemoryHelper.TrimWorkingSet();
        }
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
            Status($"Сессия недействительна ({e.Message}). Открываю QR...");
            bridge.ResetSession();
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

    private void UpdateHeaderAvatar(DialogRow r)
    {
        try
        {
            HeaderAvatar.Visibility = Visibility.Visible;
            HeaderAvatarInitials.Text = r.Initials;
            HeaderAvatarBg.Fill = (Brush)new BrushConverter().ConvertFromString(r.AvatarColor)!;
            if (r.AvatarVisible == "Visible" && !string.IsNullOrEmpty(r.AvatarPath))
            {
                var conv = (ThumbConverter)Resources["ThumbConv"];
                var bmp = conv.Convert(r.AvatarPath, typeof(ImageSource), 48, System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
                if (bmp != null)
                {
                    HeaderAvatarBrush.ImageSource = bmp;
                    HeaderAvatarPhoto.Visibility = Visibility.Visible;
                    return;
                }
            }
            HeaderAvatarPhoto.Visibility = Visibility.Collapsed;
        }
        catch
        {
            HeaderAvatarPhoto.Visibility = Visibility.Collapsed;
        }
    }

    private void FeedList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;
        var sv = FindVisualChild<ScrollViewer>(FeedList);
        if (sv is null) return;
        bool isNearBottom = sv.VerticalOffset >= (sv.ScrollableHeight - 120);
        ScrollDownBtn.Visibility = (!isNearBottom && _feed.Count > 5) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollDownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_feed.Count > 0) FeedList.ScrollIntoView(_feed[^1]);
        ScrollDownBtn.Visibility = Visibility.Collapsed;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (InputPlaceholder is not null)
        {
            InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SearchClearBtn_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private void OnLiveMessage(IncomingMessage m)
    {
        Dispatcher.Invoke(() =>
        {
            var d = _all.FirstOrDefault(x => x.Id == m.ChatId);
            var unread = _peer == m.ChatId || m.Out ? 0 : 1;
            if (d is not null)
            {
                _all.Remove(d);
                unread = _peer == m.ChatId || m.Out ? 0 : d.Unread + 1;
                _all.Insert(0, d with { LastMessage = m.Text, Unread = unread, Date = m.Date });
            }
            else
            {
                // Новый чат, которого ещё не было в загруженном списке!
                var chatName = !string.IsNullOrWhiteSpace(m.Name) && m.Name != "Telegram" ? m.Name : $"Чат {m.ChatId}";
                var kind = m.IsGroup ? ChatKind.Group : ChatKind.User;
                var newDlg = new DialogInfo(
                    m.ChatId,
                    chatName,
                    m.Text,
                    m.Date,
                    unread,
                    m.IsGroup,
                    kind,
                    false,
                    false,
                    false,
                    int.MaxValue
                );
                _all.Insert(0, newDlg);
            }

            if (_rowCache.TryGetValue(m.ChatId, out var cachedRow))
            {
                cachedRow.Preview = m.Text;
                cachedRow.Unread = unread;
                cachedRow.Time = TimeFormatter.FormatDialogTime(m.Date);
            }

            PaintDialogs();

            if (_peer == m.ChatId)
            {
                _ = LoadHistoryAsync();
                if (_bridge is not null && !m.Out) _ = _bridge.MarkReadAsync(_peer);
            }

            // Показываем оверлей и играем звук только для входящих чужих сообщений из НЕЗАГЛУШЕННЫХ чатов
            var isMuted = d?.Muted == true;
            if (!m.Out && !isMuted && NotifyFilter.ShouldShow(_cfg.Filter, m.Author, m.SenderId, null, m.IsGroup, m.Mentioned, false))
            {
                PlaySoundAndNotify(m);
            }
        });
    }

    private void PlaySoundAndNotify(IncomingMessage m)
    {
        NotificationSound.Play();

        _ = Task.Run(async () =>
        {
            string? avatarPath = null;
            if (_bridge is not null)
            {
                var id = m.SenderId != 0 ? m.SenderId : m.ChatId;
                avatarPath = await _bridge.EnsureAvatarAsync(id);
            }

            Dispatcher.Invoke(() =>
            {
                NotificationWindow.ShowOrStack(
                    m.ChatId,
                    m.Name,
                    m.Text,
                    GetInitials(m.Name),
                    GetAvatarColor(m.Name),
                    avatarPath,
                    onOpen: chatId => Dispatcher.Invoke(() => SelectChat(chatId)));
            });
        });
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
            else
            {
                _peer = chatId;
                _peerName = $"Чат {chatId}";
                ChatTitle.Text = _peerName;
                _ = LoadHistoryAsync();
            }
        }
    }

    private void TogglePanel_Click(object sender, RoutedEventArgs e)
    {
        _isCompact = !_isCompact;
        LeftCol.Width = _isCompact ? new GridLength(0) : new GridLength(280);
        TogglePanelBtn.Content = _isCompact ? "⮞" : "☰";
        TogglePanelBtn.ToolTip = _isCompact ? "Развернуть список чатов" : "Компактный режим (скрыть чаты)";
    }

    private async Task RefreshAsync(bool force = false)
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
            Content = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold },
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = Cursors.Hand,
            Style = (Style)FindResource("FolderBtn")
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
        var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA6FF"));
        var inactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2936"));
        var inactiveText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#708499"));
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

        // Мгновенная проверка локального кэша аватарки без создания фоновых тасок и блокировок
        if (string.IsNullOrEmpty(row.AvatarPath))
        {
            var cached = MediaCache.GetCachedAvatar(d.Id);
            if (cached is not null)
            {
                row.AvatarPath = cached;
            }
            else if (d.Name is not ("Избранное" or "Saved Messages") && _avatarRequested.Add(d.Id))
            {
                // Запрашиваем загрузку аватарки только один раз за сессию
                var peerId = d.Id;
                _ = Task.Run(async () =>
                {
                    if (_bridge is null) return;
                    var path = await _bridge.EnsureAvatarAsync(peerId);
                    if (!string.IsNullOrEmpty(path))
                    {
                        Dispatcher.Invoke(() =>
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

        var totalUnread = _all.Sum(d => d.Unread);
        _tray?.UpdateUnreadCount(totalUnread);

        if (_rows.Count == target.Count && _rows.SequenceEqual(target)) return;

        _rows.Clear();
        foreach (var r in target)
        {
            _rows.Add(r);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder is not null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SearchClearBtn is not null)
        {
            SearchClearBtn.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        PaintDialogs();
    }

    private async void DialogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        StopVoicePlayer();
        _feed.Clear();
        _byId.Clear();
        MemoryHelper.TrimWorkingSet();

        _peer = r.Id;
        _peerName = r.Name;
        ChatTitle.Text = _peerName;
        ChatSub.Text = "";
        _replyTo = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
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
            LoadMoreBtn.Visibility = _hasMoreHistory ? Visibility.Visible : Visibility.Collapsed;
            if (msgs.Count > 0) _oldestMsgId = msgs[0].Id;
            PaintFeed(msgs);
            ChatSub.Text = $"{msgs.Count} сообщений";
        }
        catch (Exception e) { Status($"history err: {e.Message}"); }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
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
                LoadMoreBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                _oldestMsgId = older[0].Id;
                _hasMoreHistory = older.Count >= 60;
                LoadMoreBtn.Visibility = _hasMoreHistory ? Visibility.Visible : Visibility.Collapsed;

                foreach (var m in older)
                {
                    _byId[m.Id] = m;
                }

                // Вставляем более старые сообщения в начало _feed
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
                        Dispatcher.Invoke(() =>
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
        if (_feed.Count > 0) FeedList.ScrollIntoView(_feed[^1]);
    }

    private MsgRow ToRow(MessageInfo m, string dateSeparator = "")
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
            Quote = m.ReplyTo > 0 ? $"↩ Ответ на #{m.ReplyTo} (клик для перехода)" : "",
            MediaHint = hint,
            Bubble = m.Out ? "#2B5278" : "#182533",
            Align = m.Out ? "Right" : "Left",
            Time = m.Date?.ToLocalTime().ToString("HH:mm") ?? "",
            Out = m.Out,
            HasMedia = m.Kind != MediaKind.Text,
            DateSeparator = dateSeparator
        };
    }

    private async void Image_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int id) return;
        if (_byId.TryGetValue(id, out var m) && m.Kind is MediaKind.RoundVideo or MediaKind.Video)
        {
            if (_bridge is null) return;
            Status("Скачиваю видео...");
            var vp = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.mp4");
            if (File.Exists(vp) && new FileInfo(vp).Length > 0)
            {
                OpenExternal(vp);
                Status("");
                return;
            }
            var gotVideo = await _bridge.DownloadToAsync(_peer, id, vp);
            if (gotVideo.Length > 0) OpenExternal(gotVideo);
            Status("");
            return;
        }
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
                if (_playingVoiceId == id)
                {
                    StopVoicePlayer();
                    Status("Воспроизведение остановлено");
                    return;
                }
                Status("Загружаю аудио...");
                var ext = m.Kind == MediaKind.Voice ? "ogg" : "mp3";
                var ap = Path.Combine(Path.GetTempPath(), $"tgmini_{id}.{ext}");
                string gotAudio = ap;
                if (!File.Exists(ap) || new FileInfo(ap).Length == 0)
                {
                    gotAudio = await _bridge.DownloadToAsync(_peer, id, ap);
                }
                if (string.IsNullOrEmpty(gotAudio) || !File.Exists(gotAudio))
                {
                    Status("Не удалось скачать аудио");
                    return;
                }
                PlayVoice(gotAudio, id, string.IsNullOrEmpty(m.MediaLabel) ? "Голосовое" : m.MediaLabel);
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

    private void PlayVoice(string path, int msgId, string label)
    {
        StopVoicePlayer();
        try
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += (_, _) => Dispatcher.Invoke(StopVoicePlayer);
            _mediaPlayer.MediaFailed += (_, args) => Dispatcher.Invoke(() =>
            {
                Status($"Ошибка воспроизведения ({args.ErrorException?.Message ?? "формат не поддерживается"}), открываю во внешнем плеере...");
                StopVoicePlayer();
                OpenExternal(path);
            });
            _mediaPlayer.Open(new Uri(path));
            _mediaPlayer.Play();
            _playingVoiceId = msgId;
            Status($"▶ Играет {label} (кликните повторно, чтобы остановить)");
        }
        catch (Exception ex)
        {
            Status($"Ошибка плеера: {ex.Message}");
            OpenExternal(path);
        }
    }

    private void StopVoicePlayer()
    {
        if (_mediaPlayer is not null)
        {
            try
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Close();
            }
            catch { /* ignore */ }
            _mediaPlayer = null;
        }
        _playingVoiceId = 0;
        if (StatusLabel.Text.StartsWith("▶ Играет"))
        {
            Status("");
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
        _editMsgId = 0;
        ReplyBar.Visibility = Visibility.Collapsed;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_bridge is null || _peer == 0) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        InputBox.Clear();

        if (_editMsgId > 0)
        {
            var editId = _editMsgId;
            _editMsgId = 0;
            ReplyBar.Visibility = Visibility.Collapsed;
            var editOk = await _bridge.EditMessageAsync(_peer, editId, text);
            if (!editOk)
            {
                InputBox.Text = text;
                Status("Не удалось отредактировать сообщение");
            }
            else
            {
                Status("");
                await LoadHistoryAsync();
            }
            return;
        }

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
        _editMsgId = 0;
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

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            await PasteFromClipboardAsync();
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Send_Click(sender, e);
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            // 1. Картинка из буфера (скриншоты Win+Shift+S, скопированные изображения)
            if (Clipboard.ContainsImage())
            {
                await PasteImageFromClipboardAsync();
                return;
            }

            // 2. Файлы из проводника
            if (Clipboard.ContainsFileDropList())
            {
                await PasteFilesFromClipboardAsync();
                return;
            }

            // 3. Текст
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    var selStart = InputBox.SelectionStart;
                    var selLen = InputBox.SelectionLength;
                    var cur = InputBox.Text ?? "";
                    if (selStart > cur.Length) selStart = cur.Length;
                    InputBox.Text = cur.Remove(selStart, selLen).Insert(selStart, text);
                    InputBox.SelectionStart = selStart + text.Length;
                    InputBox.SelectionLength = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Status($"Ошибка буфера обмена: {ex.Message}");
        }
    }

    private async Task PasteImageFromClipboardAsync()
    {
        if (_bridge is null || _peer == 0) return;
        try
        {
            var image = Clipboard.GetImage();
            if (image is null) return;

            var tempPath = Path.Combine(Path.GetTempPath(), $"tgmini_clip_{DateTime.UtcNow.Ticks}.png");
            using (var fs = new FileStream(tempPath, FileMode.Create))
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                encoder.Save(fs);
            }

            Status("Отправка изображения из буфера обмена...");
            var rt = _replyTo;
            _replyTo = 0;
            _editMsgId = 0;
            ReplyBar.Visibility = Visibility.Collapsed;

            var prog = new Progress<(long Sent, long Total)>(x =>
                Status($"Загрузка изображения: {(x.Total > 0 ? x.Sent * 100 / x.Total : 0)}%"));
            var ok = await _bridge.SendFileAsync(_peer, tempPath, replyTo: rt, progress: prog);
            if (!ok) Status("Не удалось отправить изображение");
            else
            {
                Status("");
                await LoadHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            Status($"Ошибка отправки картинки: {ex.Message}");
        }
    }

    private async Task PasteFilesFromClipboardAsync()
    {
        if (_bridge is null || _peer == 0) return;
        try
        {
            var files = Clipboard.GetFileDropList();
            if (files == null || files.Count == 0) return;

            var rt = _replyTo;
            _replyTo = 0;
            _editMsgId = 0;
            ReplyBar.Visibility = Visibility.Collapsed;

            foreach (string? p in files)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                var prog = new Progress<(long Sent, long Total)>(x =>
                    Status($"Загрузка {Path.GetFileName(p)}: {(x.Total > 0 ? x.Sent * 100 / x.Total : 0)}%"));
                var ok = await _bridge.SendFileAsync(_peer, p, replyTo: rt, progress: prog);
                if (!ok) Status($"Не удалось отправить {Path.GetFileName(p)}");
            }
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Status($"Ошибка отправки файлов: {ex.Message}");
        }
    }

    private async void InputContextMenu_Paste_Click(object sender, RoutedEventArgs e) =>
        await PasteFromClipboardAsync();

    private void InputContextMenu_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(InputBox.SelectedText))
            Clipboard.SetText(InputBox.SelectedText);
    }

    private void InputContextMenu_Cut_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(InputBox.SelectedText))
        {
            Clipboard.SetText(InputBox.SelectedText);
            var selStart = InputBox.SelectionStart;
            InputBox.Text = InputBox.Text.Remove(selStart, InputBox.SelectionLength);
            InputBox.SelectionStart = selStart;
        }
    }

    private void InputContextMenu_Clear_Click(object sender, RoutedEventArgs e) =>
        InputBox.Clear();

    private static MsgRow? GetMsgRowFromSender(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is MsgRow r) return r;
            if (fe.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt && pt.DataContext is MsgRow r2) return r2;
        }
        return null;
    }

    private void MsgMenuReply_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null) return;
        if (!_byId.TryGetValue(row.Id, out var m)) return;
        _replyTo = row.Id;
        _editMsgId = 0;
        var who = m.Out ? "вы" : (m.Author.Length > 0 ? m.Author : _peerName);
        var prev = m.Text.Length > 0 ? m.Text : m.MediaLabel;
        ReplyLabel.Text = $"↩ {who}: {prev[..Math.Min(70, prev.Length)]}";
        ReplyBar.Visibility = Visibility.Visible;
        InputBox.Focus();
    }

    private void MsgMenuEdit_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null) return;
        if (!_byId.TryGetValue(row.Id, out var m) || !m.Out) return;
        _editMsgId = row.Id;
        _replyTo = 0;
        ReplyLabel.Text = $"✏️ Редактирование #{row.Id}";
        ReplyBar.Visibility = Visibility.Visible;
        InputBox.Text = m.Text;
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private async void MsgMenuSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null || _bridge is null) return;
        if (!_byId.TryGetValue(row.Id, out var m)) return;

        var defaultName = !string.IsNullOrEmpty(m.MediaLabel) ? m.MediaLabel : (m.Kind == MediaKind.RoundVideo || m.Kind == MediaKind.Video ? $"video_{row.Id}.mp4" : $"media_{row.Id}.bin");
        var sfd = new SaveFileDialog { FileName = defaultName };
        if (sfd.ShowDialog() != true) return;

        Status("Сохраняю медиа...");
        var prog = new Progress<(long Sent, long Total)>(p =>
            Status($"Сохраняю... {(p.Total > 0 ? p.Sent * 100 / p.Total : 0)}%"));
        var saved = await _bridge.DownloadToAsync(_peer, row.Id, sfd.FileName, prog);
        Status(saved.Length > 0 ? $"Файл сохранён: {saved}" : "Не удалось сохранить");
    }

    private async void MsgMenuShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null || _bridge is null) return;
        if (!_byId.TryGetValue(row.Id, out var m)) return;

        var cached = MediaCache.ThumbPath(_peer, row.Id);
        var tempVid = Path.Combine(Path.GetTempPath(), $"tgmini_{row.Id}.mp4");
        var tempAudio = Path.Combine(Path.GetTempPath(), $"tgmini_{row.Id}.ogg");
        string targetPath = "";

        if (File.Exists(tempVid)) targetPath = tempVid;
        else if (File.Exists(tempAudio)) targetPath = tempAudio;
        else if (File.Exists(cached)) targetPath = cached;
        else
        {
            Status("Загружаю для показа...");
            if (m.Kind is MediaKind.RoundVideo or MediaKind.Video)
                targetPath = await _bridge.DownloadToAsync(_peer, row.Id, tempVid);
            else if (m.Kind is MediaKind.Voice or MediaKind.Audio)
                targetPath = await _bridge.DownloadToAsync(_peer, row.Id, tempAudio);
            else
                targetPath = await _bridge.EnsureThumbAsync(_peer, row.Id);
            Status("");
        }

        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
            }
            catch { /* ignore */ }
        }
        else
        {
            Status("Файл ещё не загружен на диск");
        }
    }

    private void MsgMenuCopy_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null) return;
        try
        {
            var text = !string.IsNullOrEmpty(row.Text) ? row.Text : row.MediaHint;
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                Status("Текст скопирован в буфер");
            }
        }
        catch { /* ignore */ }
    }

    private async void MsgMenuDelete_Click(object sender, RoutedEventArgs e)
    {
        var row = GetMsgRowFromSender(sender);
        if (row is null || _bridge is null) return;
        var res = MessageBox.Show("Удалить это сообщение для всех?", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        Status("Удаляю сообщение...");
        var ok = await _bridge.DeleteMessagesAsync(_peer, new[] { row.Id });
        if (ok)
        {
            Status("Сообщение удалено");
            _feed.Remove(row);
            _byId.Remove(row.Id);
            ChatSub.Text = $"{_feed.Count} сообщений";
        }
        else
        {
            Status("Не удалось удалить сообщение");
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

    private async Task ApplyChatMuteAsync(TimeSpan? duration, string label)
    {
        if (DialogList.SelectedItem is not DialogRow r) return;
        var d = _all.FirstOrDefault(x => x.Id == r.Id);
        if (d is null) return;
        var idx = _all.IndexOf(d);
        var isMuted = duration.HasValue;
        _all[idx] = d with { Muted = isMuted };
        PaintDialogs();
        Status(isMuted ? $"Чат '{d.Name}' заглушён {label}" : $"Звук чата '{d.Name}' включён");

        if (_bridge is not null)
        {
            await _bridge.MuteChatAsync(d.Id, duration);
        }
    }

    private async void DialogMute_Unmute_Click(object sender, RoutedEventArgs e) =>
        await ApplyChatMuteAsync(null, "");

    private async void DialogMute_1h_Click(object sender, RoutedEventArgs e) =>
        await ApplyChatMuteAsync(TimeSpan.FromHours(1), "на 1 час");

    private async void DialogMute_8h_Click(object sender, RoutedEventArgs e) =>
        await ApplyChatMuteAsync(TimeSpan.FromHours(8), "на 8 часов");

    private async void DialogMute_2d_Click(object sender, RoutedEventArgs e) =>
        await ApplyChatMuteAsync(TimeSpan.FromDays(2), "на 2 дня");

    private async void DialogMute_Forever_Click(object sender, RoutedEventArgs e) =>
        await ApplyChatMuteAsync(TimeSpan.MaxValue, "навсегда");

    private void DialogContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        MenuFolders.Items.Clear();
        if (DialogList.SelectedItem is not DialogRow r)
        {
            MenuFolders.IsEnabled = false;
            MenuMute.IsEnabled = false;
            return;
        }
        MenuFolders.IsEnabled = true;
        MenuMute.IsEnabled = true;

        var curChat = _all.FirstOrDefault(x => x.Id == r.Id);
        var isMuted = curChat?.Muted == true;
        MenuMute.Header = isMuted ? "🔇 Уведомления (заглушен)" : "🔔 Уведомления (со звуком)";
        MenuMuteUnmute.IsEnabled = isMuted;
        if (_folders.Count == 0)
        {
            MenuFolders.Items.Add(new MenuItem { Header = "(нет папок)", IsEnabled = false });
            return;
        }
        foreach (var f in _folders)
        {
            var inFolder = f.Peers.Contains(r.Id);
            var mi = new MenuItem
            {
                Header = f.Title,
                IsCheckable = true,
                IsChecked = inFolder
            };
            var folderId = f.Id;
            var peerId = r.Id;
            var title = f.Title;
            var name = r.Name;
            mi.Click += async (_, _) =>
            {
                if (_bridge is null) return;
                var ok = await _bridge.ToggleChatFolderAsync(folderId, peerId);
                if (ok)
                {
                    Status(inFolder
                        ? $"Чат '{name}' удалён из папки '{title}'"
                        : $"Чат '{name}' добавлен в папку '{title}'");
                    await RefreshAsync(force: true);
                }
                else
                {
                    Status($"Не удалось обновить папку '{title}'");
                }
            };
            MenuFolders.Items.Add(mi);
        }
    }
}
