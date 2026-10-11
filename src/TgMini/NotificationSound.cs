using System.IO;
using System.Media;

namespace TgMini;

/// <summary>
/// Проигрывание чистого, приятного системного звука сообщений Windows 10/11
/// (Windows Notify Messaging.wav / Windows Message Nudge.wav).
/// </summary>
public static class NotificationSound
{
    private static readonly string[] SoundCandidates =
    {
        @"C:\Windows\Media\Windows Notify Messaging.wav",
        @"C:\Windows\Media\Windows Message Nudge.wav",
        @"C:\Windows\Media\Windows Notify.wav",
        @"C:\Windows\Media\notify.wav",
        @"C:\Windows\Media\ding.wav"
    };

    public static void Play()
    {
        try
        {
            foreach (var path in SoundCandidates)
            {
                if (File.Exists(path))
                {
                    using var player = new SoundPlayer(path);
                    player.Play();
                    return;
                }
            }
        }
        catch { /* ignore */ }

        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch { /* ignore */ }
    }
}
