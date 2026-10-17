using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TgMini.Desktop;

public partial class QrWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    public CancellationToken Token => _cts.Token;

    public QrWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { try { _cts.Cancel(); } catch { } };
    }

    public void ShowQr(byte[] png)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            try
            {
                using var ms = new MemoryStream(png);
                QrImage.Source = new Avalonia.Media.Imaging.Bitmap(ms);
                QrStatus.Text = "Код обновлён — наведите камеру. Действует ~30 сек.";
            }
            catch { /* ignore */ }
        });
    }

    public void Finish(bool ok, string? message = null)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            QrStatus.Text = message ?? (ok ? "Готово!" : "Отменено");
            if (!ok) return;
            Close(true);
        });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        Close(false);
    }
}
