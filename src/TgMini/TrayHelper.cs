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
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint WM_NULL = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

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

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    private readonly Window _window;
    private IntPtr _hwnd;
    private bool _added;
    private NOTIFYICONDATA _nid;
    private IntPtr _defaultIcon = IntPtr.Zero;
    private IntPtr _customIcon = IntPtr.Zero;
    private int _lastUnread = -1;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayHelper(Window window)
    {
        _window = window;
    }

    public void Init()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        _defaultIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYNOTIFY,
            hIcon = _defaultIcon,
            szTip = "TG Mini"
        };

        _added = Shell_NotifyIcon(NIM_ADD, ref _nid);

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
    }

    public void UpdateUnreadCount(int unread)
    {
        if (!_added || _lastUnread == unread) return;
        _lastUnread = unread;

        _nid.szTip = unread > 0 ? $"TG Mini ({unread} непрочитанных)" : "TG Mini";

        if (unread > 0)
        {
            try
            {
                using var bmp = new System.Drawing.Bitmap(16, 16);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x2E, 0xA6, 0xFF));
                    g.FillEllipse(brush, 0, 0, 15, 15);

                    var txt = unread > 99 ? "99+" : unread.ToString();
                    var fontSize = txt.Length > 2 ? 6.5f : (txt.Length > 1 ? 7.5f : 9f);
                    using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                    using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                    using var sf = new System.Drawing.StringFormat
                    {
                        Alignment = System.Drawing.StringAlignment.Center,
                        LineAlignment = System.Drawing.StringAlignment.Center
                    };
                    g.DrawString(txt, font, textBrush, new System.Drawing.RectangleF(0, 0, 16, 16), sf);
                }

                var newHIcon = bmp.GetHicon();
                if (_customIcon != IntPtr.Zero)
                {
                    DestroyIcon(_customIcon);
                }
                _customIcon = newHIcon;
                _nid.hIcon = _customIcon;
            }
            catch
            {
                _nid.hIcon = _defaultIcon;
            }
        }
        else
        {
            if (_customIcon != IntPtr.Zero)
            {
                DestroyIcon(_customIcon);
                _customIcon = IntPtr.Zero;
            }
            _nid.hIcon = _defaultIcon;
        }

        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
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
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, 1001, "Открыть TG Mini");
        AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
        AppendMenu(hMenu, MF_STRING, 1002, "Выход");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);

        var cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (cmd == 1001)
        {
            OpenRequested?.Invoke();
        }
        else if (cmd == 1002)
        {
            ExitRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
            _added = false;
        }
        if (_customIcon != IntPtr.Zero)
        {
            DestroyIcon(_customIcon);
            _customIcon = IntPtr.Zero;
        }
    }
}
