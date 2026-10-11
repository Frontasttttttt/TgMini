using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TgMini;

public sealed class TrayHelper : IDisposable
{
    private const int WM_APP = 0x8000;
    public const int WM_TRAYNOTIFY = WM_APP + 42;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    private readonly Window _window;
    private IntPtr _hwnd;
    private bool _added;
    private NOTIFYICONDATA _nid;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayHelper(Window window)
    {
        _window = window;
    }

    public void Init()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        var hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYNOTIFY,
            hIcon = hIcon,
            szTip = "TG Mini"
        };

        _added = Shell_NotifyIcon(NIM_ADD, ref _nid);

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYNOTIFY)
        {
            var l = lParam.ToInt32();
            if (l is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke();
                handled = true;
            }
            else if (l == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var openItem = new System.Windows.Controls.MenuItem { Header = "Открыть TG Mini" };
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _added = false;
        }
    }
}
