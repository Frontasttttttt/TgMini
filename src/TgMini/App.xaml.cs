using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TgMini;

public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "crash.log");
    private static readonly string WtLogPath =
        Path.Combine(AppContext.BaseDirectory, "wt.log");
    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Один экземпляр: второй запуск не должен драться за session-файл
        // (IO_SharingViolation_File) — просим закрыть и выходим.
        _instanceMutex = new Mutex(true, @"Local\TgMini.SingleInstance", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show(
                "TgMini уже запущен.\nЕсли окна не видно — проверьте панель задач.",
                "TgMini", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
        }
    }

    public App()
    {
        // NOTE: ProcessRenderMode.SoftwareOnly убран — ломал русский ввод/IME в VerificationWindow
        // (CultureNotFoundException в Invariant-режиме) и рисковал тащить лишнее при скролле.
        DispatcherUnhandledException += OnUi;
        AppDomain.CurrentDomain.UnhandledException += OnDomain;
        TaskScheduler.UnobservedTaskException += (_, e) => Log("task: " + e.Exception);
        // Сетевой лог WTelegramClient в wt.log (в WinExe нет консоли)
        try
        {
            if (File.Exists(WtLogPath)) File.WriteAllText(WtLogPath, "");
        }
        catch { /* ignore */ }
        WTelegram.Helpers.Log = (lvl, msg) =>
        {
            try
            {
                var fi = new FileInfo(WtLogPath);
                if (fi.Exists && fi.Length > 1_000_000) return;
                File.AppendAllText(WtLogPath, $"{DateTime.Now:HH:mm:ss.fff} L{lvl} {msg}\n");
            }
            catch { /* ignore */ }
        };
    }

    private void OnUi(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log("ui: " + e.Exception);
        e.Handled = true;      // не роняем окно из-за одной ошибки
    }

    private void OnDomain(object sender, UnhandledExceptionEventArgs e)
        => Log("domain: " + e.ExceptionObject);

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
        catch { /* ignore */ }
    }
}

