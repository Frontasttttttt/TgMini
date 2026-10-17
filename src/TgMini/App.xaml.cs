using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace TgMini;

public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "crash.log");
    private static readonly string WtLogPath =
        Path.Combine(AppContext.BaseDirectory, "wt.log");

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var others = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                .Where(p => p.Id != current.Id)
                .ToList();

            if (others.Count > 0)
            {
                // Проверяем видимое главное окно
                foreach (var other in others)
                {
                    if (other.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(other.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(other.MainWindowHandle);
                        Shutdown();
                        return;
                    }
                }

                // Проверяем скрытое в трей окно по заголовку
                var hWnd = FindWindow(null, "TG Mini");
                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                    Shutdown();
                    return;
                }

                // Если окон нет совсем (зомби-процесс) — завершаем
                foreach (var other in others)
                {
                    try { other.Kill(); } catch { }
                }
            }
        }
        catch { /* ignore */ }
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
        if (MainWindow == null || !MainWindow.IsLoaded)
        {
            MessageBox.Show($"Ошибка запуска TG Mini:\n\n{e.Exception.Message}\n\nПодробности в crash.log",
                "TG Mini", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
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

