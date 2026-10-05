// Autotest3 - UIAutomation-драйвер для первого запуска TgMini (SetupWindow).
//
// Назначение: заполнить окно "Первый запуск" (ApiId / ApiHash / Телефон) и нажать "Сохранить",
// затем убедиться, что config.toml записан корректно.
//
// НИКАКИХ учётных данных в коде нет. Они подаются снаружи — одним из способов:
//   1) Аргументы командной строки:
//        Autotest3.exe --apiid <id> --apihash <hash> --phone <phone>
//   2) Файл конфигурации (JSON):
//        Autotest3.exe --config creds.json
//   3) Переменные окружения:
//        TGMINI_APIID / TGMINI_APIHASH / TGMINI_PHONE
//
// Файл-подсказку для своих данных можно получить так:
//        copy creds.example.json creds.json
//
// Коды выхода: 0 = окно заполнено, сохранено, проверка config.toml пройдена;
//              1 = неверное использование / ошибка запуска / проверка не пройдена.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Autotest3;

public static class Program
{
    // Путь к TgMini.exe: по умолчанию рядом с самим драйвером (портативно).
    // Если там его нет — используется прежний путь из среды разработки.
    private static readonly string DefaultTgMiniPath = ResolveDefaultTgMiniPath();

    // config.toml профиля пользователя (TgMini пишет его в %LOCALAPPDATA%\TgMini\config.toml).
    private static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TgMini", "config.toml");

    private static string ResolveDefaultTgMiniPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "TgMini.exe");
        if (File.Exists(local)) return local;
        const string legacy = @"C:\AI\TgMini\dist\TgMini.exe";
        return File.Exists(legacy) ? legacy : local;
    }

    private const int TimeoutWindowFound = 60 * 1000;
    private const int TimeoutWindowClosed = 30 * 1000;
    private const int IntervalPoll = 2000;
    private const int ExitError = 1;

    public static int Main(string[] args)
    {
        var options = ParseOptions(args);
        if (options == null)
        {
            PrintUsage();
            return ExitError;
        }
        var creds = ResolveCredentials(options);
        if (!creds.IsValid)
        {
            Console.Error.WriteLine("Ошибка: не переданы ApiId / ApiHash / Телефон.");
            Console.Error.WriteLine("Подайте их через --apiid / --apihash / --phone или через файл --config creds.json.");
            PrintUsage();
            return ExitError;
        }

        Console.WriteLine("=== Autotest3 - UIAutomation SetupWindow driver ===");
        Console.WriteLine("TgMini path : " + options.TgMiniPath);
        Console.WriteLine("Config path : " + options.ConfigPath);

        try
        {
            if (options.LaunchApp && !EnsureTgMiniExists(options.TgMiniPath)) return ExitError;
            var process = options.LaunchApp ? LaunchTgMini(options.TgMiniPath) : null;
            try
            {
                var window = WaitForSetupWindow();
                if (window == null) return ExitError;
                FillAndSave(window, creds);
                WaitForClose(window);
                if (options.Verify && !VerifyConfig(options.ConfigPath, creds)) return ExitError;
                Console.WriteLine("=== OK: SetupWindow заполнен, сохранено, config.toml проверен ===");
                return 0;
            }
            finally
            {
                if (process != null && !process.HasExited)
                {
                    try { process.Kill(); process.WaitForExit(5000); }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка: " + ex.GetType().Name + ": " + ex.Message);
            return ExitError;
        }
    }

    private static Options? ParseOptions(string[] args)
    {
        var opts = new Options { LaunchApp = true, Verify = true };
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--apiid": if (++i >= args.Length) return null; opts.ApiId = args[i]; break;
                case "--apihash": if (++i >= args.Length) return null; opts.ApiHash = args[i]; break;
                case "--phone": if (++i >= args.Length) return null; opts.Phone = args[i]; break;
                case "--app": if (++i >= args.Length) return null; opts.TgMiniPath = args[i]; break;
                case "--config": if (++i >= args.Length) return null; opts.ConfigPath = args[i]; break;
                case "--no-launch": opts.LaunchApp = false; break;
                case "--no-verify": opts.Verify = false; break;
                case "--help": case "-h": PrintUsage(); Environment.Exit(0); break;
                default: Console.Error.WriteLine("Неизвестный аргумент: " + args[i]); return null;
            }
        }
        return opts;
    }

    private static Credentials ResolveCredentials(Options options)
    {
        var creds = new Credentials();
        if (!string.IsNullOrWhiteSpace(options.ApiId)) creds.ApiId = options.ApiId;
        if (!string.IsNullOrWhiteSpace(options.ApiHash)) creds.ApiHash = options.ApiHash;
        if (!string.IsNullOrWhiteSpace(options.Phone)) creds.Phone = options.Phone;
        if (!string.IsNullOrWhiteSpace(options.ConfigPath) && File.Exists(options.ConfigPath))
        {
            var json = File.ReadAllText(options.ConfigPath);
            var f = CredsJson.Parse(json);
            if (f != null)
            {
                if (string.IsNullOrWhiteSpace(creds.ApiId)) creds.ApiId = f.ApiId;
                if (string.IsNullOrWhiteSpace(creds.ApiHash)) creds.ApiHash = f.ApiHash;
                if (string.IsNullOrWhiteSpace(creds.Phone)) creds.Phone = f.Phone;
            }
        }
        if (string.IsNullOrWhiteSpace(creds.ApiId)) creds.ApiId = Environment.GetEnvironmentVariable("TGMINI_APIID");
        if (string.IsNullOrWhiteSpace(creds.ApiHash)) creds.ApiHash = Environment.GetEnvironmentVariable("TGMINI_APIHASH");
        if (string.IsNullOrWhiteSpace(creds.Phone)) creds.Phone = Environment.GetEnvironmentVariable("TGMINI_PHONE");
        return creds;
    }

    private static bool EnsureTgMiniExists(string path)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("TgMini не найден по пути: " + path);
            Console.Error.WriteLine("Укажите правильный путь аргументом --app.");
            return false;
        }
        return true;
    }

    private static Process LaunchTgMini(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            CreateNoWindow = true
        };
        var proc = Process.Start(startInfo)!;
        Console.WriteLine("Запущен TgMini, PID = " + proc.Id);
        return proc;
    }

    private static AutomationElement? WaitForSetupWindow()
    {
        Console.WriteLine("Ожидаю SetupWindow...");
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutWindowFound);
        while (DateTime.UtcNow < deadline)
        {
            var root = AutomationElement.RootElement;
            var windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            foreach (AutomationElement main in windows.Cast<AutomationElement>())
            {
                // окно должно относиться к TgMini
                if (main.Current.Name != null && main.Current.Name.Contains("TG Mini", StringComparison.OrdinalIgnoreCase))
                {
                    // SetupWindow — это дочерний элемент (панель/группа) главного окна
                    var setup = FindControl(main, el =>
                        el.Current.Name != null &&
                        (el.Current.Name.Contains("Первый запуск", StringComparison.OrdinalIgnoreCase) ||
                         el.Current.Name.Contains("Настройка Telegram API", StringComparison.OrdinalIgnoreCase)));
                    if (setup != null)
                    {
                        Console.WriteLine("SetupWindow найден: name=[" + setup.Current.Name + "]");
                        return setup;
                    }
                }
                // на случай, если окно первого запуска имеет собственное имя
                if (main.Current.Name != null &&
                    (main.Current.Name.Contains("Первый запуск", StringComparison.OrdinalIgnoreCase) ||
                     main.Current.Name.Contains("Настройка Telegram API", StringComparison.OrdinalIgnoreCase)))
                {
                    return main;
                }
            }
            Thread.Sleep(IntervalPoll);
        }
        Console.Error.WriteLine("SetupWindow не появился в течение заданного времени.");
        return null;
    }

    private static void FillAndSave(AutomationElement setupWindow, Credentials creds)
    {
        Console.WriteLine("Заполняю поля...");
        var allChildren = setupWindow.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>().ToList();

        int filled = 0;
        for (int i = 0; i < allChildren.Count - 1; i++)
        {
            var labelEl = allChildren[i];
            var name = labelEl.Current.Name ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;  // Edit-поля без имени пропускаем

            bool isApiId = name.Contains("API ID", StringComparison.OrdinalIgnoreCase) || name.Contains("ApiId", StringComparison.OrdinalIgnoreCase);
            bool isApiHash = name.Contains("API Hash", StringComparison.OrdinalIgnoreCase) || name.Contains("ApiHash", StringComparison.OrdinalIgnoreCase);
            bool isPhone = name.Contains("Телефон", StringComparison.OrdinalIgnoreCase) || name.Contains("Phone", StringComparison.OrdinalIgnoreCase);

            if (isApiId || isApiHash || isPhone)
            {
                var edit = allChildren[i + 1];
                if (edit.GetCurrentPattern(ValuePattern.Pattern) is ValuePattern)
                {
                    string? value = isApiId ? creds.ApiId : (isApiHash ? creds.ApiHash : creds.Phone);
                    if (value != null && trySetValue(edit, value))
                    {
                        filled++;
                        Console.WriteLine("  заполнено (" + filled + "/3): [" + value + "]");
                    }
                }
            }
        }
        Console.WriteLine("Заполнено " + filled + "/3 поля.");
        if (filled != 3)
        {
            Console.Error.WriteLine("Не удалось заполнить все три текстовых поля.");
            throw new InvalidOperationException("Не все поля заполнены.");
        }

        InvokeButton(setupWindow, "Сохранить и продолжить");
    }

    private static AutomationElement? FindControl(AutomationElement root, Predicate<AutomationElement> match)
    {
        var all = root.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>();
        foreach (var el in all) if (match(el)) return el;
        return null;
    }

    // рекурсивный поиск потомка по предикату (нужен для кнопки, которая может быть вложенной)
    private static AutomationElement? FindControlDescendant(AutomationElement root, Predicate<AutomationElement> match)
    {
        try
        {
            var stack = new Stack<AutomationElement>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var el = stack.Pop();
                if (match(el)) return el;
                try
                {
                    var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
                    foreach (AutomationElement c in children.Cast<AutomationElement>())
                        stack.Push(c);
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    // навигация и клик по кнопке (находится по имени, так как ControlType в этом приложении не рендерится)
    private static void InvokeButton(AutomationElement root, string buttonName)
    {
        Console.WriteLine("Ищу кнопку \"" + buttonName + "\"...");
        var btn = FindControl(root, el => el.Current.Name != null && el.Current.Name.Contains(buttonName, StringComparison.OrdinalIgnoreCase));
        if (btn == null)
            btn = FindControlDescendant(root, el => el.Current.Name != null && el.Current.Name.Contains(buttonName, StringComparison.OrdinalIgnoreCase));
        if (btn == null)
        {
            Console.Error.WriteLine("Кнопка \"" + buttonName + "\" не найдена.");
            throw new InvalidOperationException("Кнопка не найдена.");
        }
        object? pattern = btn.GetCurrentPattern(InvokePattern.Pattern);
        if (pattern is InvokePattern inv)
        {
            inv.Invoke();
            Console.WriteLine("Нажата кнопка \"" + buttonName + "\".");
            return;
        }
        // если сам элемент не поддерживает InvokePattern, пробуем детей
        Console.Error.WriteLine("Кнопка не поддерживает InvokePattern, пробуем детей...");
        try
        {
            var children = btn.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>();
            foreach (var c in children)
            {
                var p = c.GetCurrentPattern(InvokePattern.Pattern);
                if (p is InvokePattern invc)
                {
                    invc.Invoke();
                    Console.WriteLine("Нажата кнопка \"" + buttonName + "\" (через ребёнка).");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Ошибка при попытке клика: " + ex.Message);
        }
        throw new InvalidOperationException("Не удалось нажать кнопку \"" + buttonName + "\".");
    }

    private static bool trySetValue(AutomationElement el, string value)
    {
        try
        {
            object? pattern = el.GetCurrentPattern(ValuePattern.Pattern);
            if (pattern is ValuePattern vp) { vp.SetValue(value); return true; }
        }
        catch (Exception ex) { Console.Error.WriteLine("  ошибка setValue: " + ex.Message); }
        return false;
    }

    private static void WaitForClose(AutomationElement window)
    {
        Console.WriteLine("Ожидаю закрытия SetupWindow...");
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutWindowClosed);
        while (DateTime.UtcNow < deadline)
        {
            bool available = false;
            try { available = IsElementAvailable(window.Current.NativeWindowHandle); }
            catch { available = false; }
            if (!available)
            {
                Console.WriteLine("SetupWindow закрылся (ElementNotAvailable).");
                return;
            }
            // окно доступно по хендлу — проверяю, не исчезло ли оно из дерева (диалог может быть заменён другим)
            try
            {
                var root = AutomationElement.RootElement;
                var matches = root.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>();
                bool stillExists = false;
                foreach (var m in matches)
                {
                    var nm = m.Current.Name;
                    if (!string.IsNullOrWhiteSpace(nm) &&
                        (nm.Contains("Первый запуск", StringComparison.OrdinalIgnoreCase) ||
                         nm.Contains("Настройка Telegram API", StringComparison.OrdinalIgnoreCase)))
                    {
                        stillExists = true;
                        break;
                    }
                }
                if (!stillExists)
                {
                    Console.WriteLine("SetupWindow закрылся (исчез из дерева).");
                    return;
                }
            }
            catch { }
            Thread.Sleep(IntervalPoll);
        }
        Console.Error.WriteLine("SetupWindow не закрылось в течение заданного времени.");
        throw new TimeoutException("Окно не закрылось.");
    }

    private static bool IsElementAvailable(IntPtr handle)
    {
        try { AutomationElement.FromHandle(handle); return true; }
        catch (ElementNotAvailableException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static bool VerifyConfig(string configPath, Credentials creds)
    {
        Console.WriteLine("Проверяю config.toml...");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("config.toml не найден: " + configPath);
            return false;
        }
        var lines = File.ReadAllLines(configPath);
        bool foundId = false, foundHash = false, foundPhone = false;
        foreach (var line in lines)
        {
            var l = line.Trim();
            if (l.StartsWith("api_id", StringComparison.OrdinalIgnoreCase))
            {
                var val = l.Split('=')[1].Trim().Trim('"');
                foundId = val == creds.ApiId;
                Console.WriteLine("  api_id = " + val + " -> " + (foundId ? "OK" : "НЕ СОВПАДАЕТ с " + creds.ApiId));
            }
            else if (l.StartsWith("api_hash", StringComparison.OrdinalIgnoreCase))
            {
                var val = l.Split('=')[1].Trim().Trim('"');
                foundHash = val == creds.ApiHash;
                Console.WriteLine("  api_hash = " + val + " -> " + (foundHash ? "OK" : "НЕ СОВПАДАЕТ с " + creds.ApiHash));
            }
            else if (l.StartsWith("phone", StringComparison.OrdinalIgnoreCase))
            {
                var val = l.Split('=')[1].Trim().Trim('"');
                foundPhone = val == creds.Phone;
                Console.WriteLine("  phone = " + val + " -> " + (foundPhone ? "OK" : "НЕ СОВПАДАЕТ с " + creds.Phone));
            }
        }
        if (!foundId || !foundHash || !foundPhone)
        {
            Console.Error.WriteLine("Проверка config.toml не пройдена: некоторые поля отсутствуют.");
            return false;
        }
        Console.WriteLine("config.toml содержит api_id, api_hash и телефон.");
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Autotest3 - UIAutomation драйвер для первого запуска TgMini.");
        Console.WriteLine();
        Console.WriteLine("Использование:");
        Console.WriteLine("  Autotest3.exe --apiid <ID> --apihash <HASH> --phone <ТЕЛЕФОН>  [опции]");
        Console.WriteLine("  Autotest3.exe --config <путь_к_файлу.json>                      [опции]");
        Console.WriteLine();
        Console.WriteLine("Аргументы:");
        Console.WriteLine("  --apiid     Telegram API ID (например 26488779)");
        Console.WriteLine("  --apihash   Telegram API Hash (32 символа)");
        Console.WriteLine("  --phone     Номер телефона в международном формате (начинается с +, например +79991234567)");
        Console.WriteLine();
        Console.WriteLine("Опции:");
        Console.WriteLine("  --app <путь>      Путь к TgMini.exe (по умолчанию C:\\AI\\TgMini\\dist\\TgMini.exe)");
        Console.WriteLine("  --config <путь>   JSON-файл с credentials (см. creds.example.json)");
        Console.WriteLine("  --no-launch       Не запускать TgMini, предположить, что SetupWindow уже открыто");
        Console.WriteLine("  --no-verify       Не проверять config.toml после сохранения");
        Console.WriteLine("  --help, -h        Показать эту справку");
        Console.WriteLine();
        Console.WriteLine("Переменные окружения (резерв): TGMINI_APIID, TGMINI_APIHASH, TGMINI_PHONE");
        Console.WriteLine("Требования: Windows, .NET 9 runtime, права доступа к UI другого окна.");
        Console.WriteLine("Коды выхода: 0 = успех, 1 = ошибка / неверное использование.");
    }

    private sealed class Options
    {
        public string? ApiId;
        public string? ApiHash;
        public string? Phone;
        public string TgMiniPath = DefaultTgMiniPath;
        public string ConfigPath = DefaultConfigPath;
        public bool LaunchApp;
        public bool Verify;
    }

    private sealed class Credentials
    {
        public string? ApiId;
        public string? ApiHash;
        public string? Phone;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(ApiId) &&
            !string.IsNullOrWhiteSpace(ApiHash) &&
            !string.IsNullOrWhiteSpace(Phone);
    }

    private sealed class CredsJson
    {
        public string? ApiId;
        public string? ApiHash;
        public string? Phone;

        public static CredsJson? Parse(string json)
        {
            try
            {
                var creds = new CredsJson();
                var rest = json.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                foreach (var field in new[] { "apiid", "apihash", "phone" })
                {
                    var pattern = $"\"{field}\"\\s*:\\s*\"([^\"]*)\"";
                    var match = Regex.Match(rest, pattern);
                    if (match.Success)
                    {
                        var val = match.Groups[1].Value;
                        if (field == "apiid") creds.ApiId = val;
                        else if (field == "apihash") creds.ApiHash = val;
                        else creds.Phone = val;
                    }
                }
                return (creds.ApiId != null || creds.ApiHash != null || creds.Phone != null) ? creds : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static class UnsafeNativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
    }
}