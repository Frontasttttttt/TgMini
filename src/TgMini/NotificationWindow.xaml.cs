using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TgMini;

public partial class NotificationWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly long _chatId;
    private readonly Action<long>? _onOpen;
    private readonly DispatcherTimer _timer;
    private int _remainingMs;
    private const int TotalDurationMs = 5000;
    private const int TickIntervalMs = 50;

    public string TitleText { get; }
    public string MessageText { get; }
    public string Initials { get; }
    public string AvatarColor { get; }

    public NotificationWindow(long chatId, string title, string text, string initials, string avatarColor, Action<long>? onOpen = null)
    {
        _chatId = chatId;
        _onOpen = onOpen;
        TitleText = title;
        MessageText = text;
        Initials = initials;
        AvatarColor = avatarColor;

        DataContext = new
        {
            Title = TitleText,
            Text = MessageText,
            Initials = Initials,
            AvatarColor = AvatarColor
        };

        InitializeComponent();

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Top + 12;

        _remainingMs = TotalDurationMs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickIntervalMs) };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();
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
