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
    private static EventWaitHandle? _wakeUpEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool createdNew;
        try
        {
            _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TgMini.WakeUp", out createdNew);
        }
        catch
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            // Второй запуск: будим уже открытый экземпляр и тихо выходим
            try { _wakeUpEvent?.Set(); } catch { /* ignore */ }
            Shutdown();
            return;
        }

        // Фоновый поток ожидает повторных запусков и разворачивает окно
        var wakeThread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _wakeUpEvent?.WaitOne();
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is { } w)
                        {
                            w.Show();
                            if (w.WindowState == WindowState.Minimized)
                                w.WindowState = WindowState.Normal;
                            w.Activate();
                            w.Focus();
                        }
                    });
                }
                catch { break; }
            }
        })
        {
            IsBackground = true
        };
        wakeThread.Start();
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

