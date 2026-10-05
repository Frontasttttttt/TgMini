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

