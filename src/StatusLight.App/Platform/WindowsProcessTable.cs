using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StatusLight.Core;
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

    /// <summary>
    /// Command lines of <paramref name="pid"/>'s direct shell children. One Toolhelp snapshot finds
    /// the children; WMI is queried only per shell child, so an idle session with no shells costs
    /// no WMI at all. Never throws; failures yield an empty list.
    /// </summary>
    public IReadOnlyList<string> ChildCommandLines(int pid)
    {
        try
        {
            var shells = ShellCommands.ShellChildren(Snapshot(), pid);
            if (shells.Count == 0) return [];
            var lines = new List<string>();
            foreach (var child in shells)
            {
                if (CommandLineOf(child) is { Length: > 0 } cl) lines.Add(cl);
            }
            return lines;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? CommandLineOf(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["CommandLine"] is string cl) return cl;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static unsafe List<(int Pid, int ParentPid, string ExeFile)> Snapshot()
    {
        var rows = new List<(int, int, string)>();
        var snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap == -1 || snap == 0) return rows;
        try
        {
            ProcessEntry32W entry = default;
            entry.dwSize = (uint)sizeof(ProcessEntry32W);
            for (var ok = Process32FirstW(snap, &entry); ok != 0; ok = Process32NextW(snap, &entry))
            {
                var exe = new string(entry.szExeFile, 0, MaxPath);
                var nul = exe.IndexOf('\0');
                if (nul >= 0) exe = exe[..nul];
                rows.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, exe));
            }
        }
        finally
        {
            CloseHandle(snap);
        }
        return rows;
    }

    private const uint Th32csSnapProcess = 0x00000002;
    private const int MaxPath = 260;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ProcessEntry32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[MaxPath];
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint pid);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int Process32FirstW(nint snapshot, ProcessEntry32W* entry);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int Process32NextW(nint snapshot, ProcessEntry32W* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, int inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetProcessTimes(nint h, long* creation, long* exit, long* kernel, long* user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetExitCodeProcess(nint h, out uint code);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint h);
}
