using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TgMini;

public sealed record StackedNotificationItem(string Text, string Time);

public partial class NotificationWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static NotificationWindow? _activeInstance;

    private long _chatId;
    private Action<long>? _onOpen;
    private readonly DispatcherTimer _timer;
    private int _remainingMs;
    private const int TotalDurationMs = 5000;
    private const int TickIntervalMs = 50;

    private readonly ObservableCollection<StackedNotificationItem> _items = new();

    public static void ShowOrStack(long chatId, string title, string text, string initials, string avatarColor, string? avatarPath, Action<long>? onOpen)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeInstance is not null && _activeInstance.IsLoaded)
            {
                _activeInstance.AddMessage(chatId, title, text, initials, avatarColor, avatarPath, onOpen);
            }
            else
            {
                _activeInstance = new NotificationWindow(chatId, title, text, initials, avatarColor, avatarPath, onOpen);
                _activeInstance.Show();
            }
        });
    }

    public NotificationWindow(long chatId, string title, string text, string initials, string avatarColor, string? avatarPath, Action<long>? onOpen)
    {
        _chatId = chatId;
        _onOpen = onOpen;

        InitializeComponent();

        StackedList.ItemsSource = _items;

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Top + 12;

        ApplyMessage(chatId, title, text, initials, avatarColor, avatarPath, onOpen);

        _remainingMs = TotalDurationMs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        Closed += (_, _) =>
        {
            if (_activeInstance == this) _activeInstance = null;
        };
    }

    public void AddMessage(long chatId, string title, string text, string initials, string avatarColor, string? avatarPath, Action<long>? onOpen)
    {
        ApplyMessage(chatId, title, text, initials, avatarColor, avatarPath, onOpen);
        _remainingMs = TotalDurationMs;
        TimeoutBar.Value = 100;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void ApplyMessage(long chatId, string title, string text, string initials, string avatarColor, string? avatarPath, Action<long>? onOpen)
    {
        _chatId = chatId;
        _onOpen = onOpen;

        TitleText.Text = title;
        BodyText.Text = text;
        AvatarInitials.Text = initials;

        try
        {
            AvatarBg.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(avatarColor));
        }
        catch
        {
            AvatarBg.Fill = new SolidColorBrush(Color.FromRgb(46, 166, 255));
        }

        if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(avatarPath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                AvatarPhotoBrush.ImageSource = bmp;
                AvatarPhoto.Visibility = Visibility.Visible;
            }
            catch
            {
                AvatarPhoto.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            AvatarPhoto.Visibility = Visibility.Collapsed;
        }

        _items.Insert(0, new StackedNotificationItem(text, DateTime.Now.ToString("HH:mm")));
        while (_items.Count > 6) _items.RemoveAt(_items.Count - 1);

        if (_items.Count > 1)
        {
            BadgeCount.Visibility = Visibility.Visible;
            BadgeCountText.Text = $"+{_items.Count - 1}";
        }
        else
        {
            BadgeCount.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _remainingMs -= TickIntervalMs;
        if (_remainingMs <= 0)
        {
            _timer.Stop();
            Close();
            return;
        }
        TimeoutBar.Value = (_remainingMs * 100.0) / TotalDurationMs;
    }

    private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _timer.Stop();
        if (_items.Count > 1)
        {
            StackPanelExpanded.Visibility = Visibility.Visible;
        }
    }

    private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        StackPanelExpanded.Visibility = Visibility.Collapsed;
        _timer.Start();
    }

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _timer.Stop();
        Close();
        _onOpen?.Invoke(_chatId);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}
