using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StatusLight.Core.Sources;

namespace StatusLight.App.Platform;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsProcessTable : IProcessTable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorAccessDenied = 5;

    public unsafe ProcessLiveness Check(int pid, long pidStart)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (handle == 0)
            return Marshal.GetLastWin32Error() == ErrorAccessDenied ? ProcessLiveness.Unknown : ProcessLiveness.Dead;
        try
        {
            long creation, exit, kernel, user;
            if (GetProcessTimes(handle, &creation, &exit, &kernel, &user) == 0)
                return ProcessLiveness.Unknown;
            // An exited process whose handle is still obtainable has a non-zero exit time.
            if (exit != 0) return ProcessLiveness.Dead;
            if (pidStart != 0 && creation != pidStart) return ProcessLiveness.Dead;
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

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint h);
}
