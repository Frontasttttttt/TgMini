using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TgMini.Desktop;

public static class MemoryHelper
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("libc", EntryPoint = "malloc_trim")]
    private static extern int MallocTrim(int pad);

    private static int _isTrimming;

    /// <summary>
    /// Неблокирующий фоновый сброс памяти. Никогда не зависает на UI-потоке,
    /// не выполняет блокирующий compacting GC и не вызывает WaitForPendingFinalizers().
    /// </summary>
    public static void TrimWorkingSetAsync(bool full = false)
    {
        if (Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
            return;

        Task.Run(() =>
        {
            try
            {
                GC.Collect(full ? 2 : 1, GCCollectionMode.Optimized, blocking: false, compacting: false);

                if (OperatingSystem.IsWindows())
                {
                    using var proc = Process.GetCurrentProcess();
                    EmptyWorkingSet(proc.Handle);
                }
                else if (OperatingSystem.IsLinux())
                {
                    MallocTrim(0);
                }
            }
            catch
            {
                // best-effort trim
            }
            finally
            {
                Interlocked.Exchange(ref _isTrimming, 0);
            }
        });
    }

    public static void TrimWorkingSet() => TrimWorkingSetAsync(full: false);
}
