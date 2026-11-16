using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamepadEnhancer
{
    /// <summary>Name of the process that owns the foreground window (cached per process id).</summary>
    internal sealed class ForegroundWindow
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private int cachedPid = -1;
        private string cachedName;

        /// <summary>Returns false when there is no foreground window or the process can't be read.</summary>
        public bool TryGet(out int pid, out string processName)
        {
            pid = 0;
            processName = null;
            if (!OperatingSystem.IsWindows()) return false;
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint raw);
            pid = (int)raw;
            if (pid == cachedPid) { processName = cachedName; return cachedName != null; }
            try
            {
                using Process p = Process.GetProcessById(pid);
                cachedName = p.ProcessName;
            }
            catch { cachedName = null; }
            cachedPid = pid;
            processName = cachedName;
            return cachedName != null;
        }
    }
}
