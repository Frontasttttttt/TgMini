using System.IO;
using System.Media;

namespace TgMini;

/// <summary>
/// Синтез мягкого колокольчика в стиле уведомлений iOS (Tri-tone / Note)
/// Полностью автономный (WAV 44.1 кГц, 16 бит PCM, 380 мс), без внешних файлов.
/// </summary>
public static class NotificationSound
{
    private static byte[]? _wavBytes;

    public static void Play()
    {
        try
        {
            _wavBytes ??= GenerateChimeWav();
            using var ms = new MemoryStream(_wavBytes);
            using var player = new SoundPlayer(ms);
            player.Play();
        }
        catch
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }
    }

    private static byte[] GenerateChimeWav()
    {
        const int sampleRate = 44100;
        const int totalSamples = (int)(sampleRate * 0.38); // 380 мс
        var samples = new short[totalSamples];

        // 3 восходящие ноты (гармоничный трезвук A5 -> C#6 -> E6):
        // Нота 1: 0 - 80 мс, 880 Гц (A5)
        // Нота 2: 70 - 160 мс, 1108 Гц (C#6)
        // Нота 3: 140 - 380 мс, 1318 Гц (E6)
        (double start, double dur, double freq, double amp)[] notes =
        {
            (0.00, 0.08, 880.0, 0.45),
            (0.07, 0.09, 1108.7, 0.55),
            (0.14, 0.24, 1318.5, 0.70)
        };

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sample = 0;
            foreach (var n in notes)
            {
                if (t >= n.start && t < n.start + n.dur)
                {
                    double noteT = t - n.start;
                    // Мягкая атака (7 мс) и экспоненциальное затухание колокола
                    double env = noteT < 0.007
                        ? noteT / 0.007
                        : Math.Exp(-noteT * 11.0);
                    // Основной тон + лёгкий 2-й обертон для бархатного звучания
                    double wave = Math.Sin(2 * Math.PI * n.freq * noteT)
                                + 0.22 * Math.Sin(2 * Math.PI * (n.freq * 2) * noteT);
                    sample += wave * env * n.amp;
                }
            }
            sample = Math.Clamp(sample, -1.0, 1.0);
            samples[i] = (short)(sample * short.MaxValue * 0.85);
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + totalSamples * 2);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // subchunk1 size (PCM)
        bw.Write((short)1); // audio format (PCM)
        bw.Write((short)1); // channels (mono)
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // byte rate
        bw.Write((short)2); // block align
        bw.Write((short)16); // bits per sample
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(totalSamples * 2);
        for (int i = 0; i < totalSamples; i++)
            bw.Write(samples[i]);
        bw.Flush();
        return ms.ToArray();
    }
}
