using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ELKA.PowerThrottleControl.Models;

namespace ELKA.PowerThrottleControl.Services;

internal static class RunningApplicationDiscovery
{
    public static IReadOnlyList<ApplicationEntry> Discover()
    {
        var applications = new Dictionary<string, ApplicationEntry>(StringComparer.OrdinalIgnoreCase);
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Win32Exception) { return []; }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var path = ReadExecutablePath(process.Id);
                    if (path is null || applications.ContainsKey(path)) continue;
                    var entry = ExecutableApplication.TryCreate(path);
                    if (entry is not null) applications[entry.ExecutablePath] = entry;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.Security.SecurityException)
                {
                    // A process may have exited or be protected. Manual EXE selection remains available.
                }
            }
        }
        return applications.Values.ToList();
    }

    internal static string? ReadExecutablePath(int processId)
    {
        // Reading the image path requires only limited query access, not process memory access.
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return null;
        var capacity = 32768;
        var path = new StringBuilder(capacity);
        return QueryFullProcessImageName(handle, 0, path, ref capacity) ? path.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder executableName, ref int size);
}
