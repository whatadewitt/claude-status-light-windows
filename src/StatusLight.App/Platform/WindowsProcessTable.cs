using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StatusLight.Core.Sources;

namespace StatusLight.App.Platform;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessTable : IProcessTable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInvalidParameter = 87;
    private const uint StillActive = 259;

    public unsafe ProcessLiveness Check(int pid, long pidStart)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (handle == 0)
            // Only "no such pid" proves death; access denied or anything else is unknown.
            return Marshal.GetLastWin32Error() == ErrorInvalidParameter ? ProcessLiveness.Dead : ProcessLiveness.Unknown;
        try
        {
            if (GetExitCodeProcess(handle, out var code) == 0) return ProcessLiveness.Unknown;
            if (code != StillActive) return ProcessLiveness.Dead;
            if (pidStart != 0)
            {
                long creation, exit, kernel, user;
                if (GetProcessTimes(handle, &creation, &exit, &kernel, &user) == 0)
                    return ProcessLiveness.Unknown;
                if (creation != pidStart) return ProcessLiveness.Dead;
            }
            return ProcessLiveness.Alive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public IReadOnlyList<string> ChildCommandLines(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ParentProcessId = {pid}");
            var lines = new List<string>();
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["CommandLine"] is string cl && cl.Length > 0) lines.Add(cl);
                }
            }
            return lines;
        }
        catch (Exception)
        {
            return [];
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, int inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetProcessTimes(nint h, long* creation, long* exit, long* kernel, long* user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetExitCodeProcess(nint h, out uint code);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint h);
}
