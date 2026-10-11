using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace TgMini;

/// <summary>
/// Проигрывание прикреплённого пользователем звука iPhone (din-iphone.mp3).
/// Встроен как EmbeddedResource прямо в сборку.
/// </summary>
public static class NotificationSound
{
    private static string? _soundPath;

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    public static void Play()
    {
        try
        {
            if (_soundPath is null || !File.Exists(_soundPath))
            {
                var p = Path.Combine(Path.GetTempPath(), "tgmini_din_iphone.mp3");
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("TgMini.din-iphone.mp3");
                if (s is not null)
                {
                    using var fs = File.Create(p);
                    s.CopyTo(fs);
                    _soundPath = p;
                }
            }

            if (_soundPath is not null && File.Exists(_soundPath))
            {
                mciSendString("close notif_sound", null, 0, IntPtr.Zero);
                mciSendString($"open \"{_soundPath}\" type mpegvideo alias notif_sound", null, 0, IntPtr.Zero);
                mciSendString("play notif_sound from 0", null, 0, IntPtr.Zero);
                return;
            }
        }
        catch { /* fallback */ }

        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch { /* ignore */ }
    }
}
