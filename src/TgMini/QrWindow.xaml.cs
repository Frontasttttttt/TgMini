using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TgMini;

/// <summary>Окно QR-входа: показывает код из моста, закрывается после успеха/отмены.</summary>
public partial class QrWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    public CancellationToken Token => _cts.Token;

    public QrWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { try { _cts.Cancel(); } catch { /* ignore */ } };
    }

    public void ShowQr(byte[] png)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new MemoryStream(png);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                QrImage.Source = bmp;
                QrStatus.Text = "Код обновлён — наведите камеру. Действует ~30 сек.";
            }
            catch { /* ignore */ }
        });
    }

    public void Finish(bool ok, string? message = null)
    {
        Dispatcher.Invoke(() =>
        {
            QrStatus.Text = message ?? (ok ? "Готово!" : "Отменено");
            if (!ok) return;
            try { DialogResult = true; } catch { /* ignore */ }
            Close();
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { DialogResult = false; } catch { /* ignore */ }
        Close();
    }
}
