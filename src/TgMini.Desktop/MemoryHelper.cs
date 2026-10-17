using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TgMini.Desktop;

public static class MemoryHelper
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("libc", EntryPoint = "malloc_trim")]
    private static extern int MallocTrim(int pad);

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(1, GCCollectionMode.Forced, false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);

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
    }
}
