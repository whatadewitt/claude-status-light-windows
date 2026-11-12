using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StatusLight.Core.Hook;

namespace StatusLight.Hook;

/// <summary>Walks a process's parent chain via one Toolhelp snapshot, enriched with image path and start time.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsProcessChain
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int MAX_PATH = 260;
    private static readonly nint InvalidHandle = -1;

    /// <summary>Returns <paramref name="pid"/> itself first, then its parent, grandparent, … (at most <paramref name="max"/> entries).
    /// Stops on a cycle, an unknown parent, or a parent that started after its child (pid reuse).</summary>
    public static IReadOnlyList<ProcEntry> Ancestors(int pid, int max = 12)
    {
        var table = Snapshot();
        var result = new List<ProcEntry>();
        var seen = new HashSet<int>();
        var current = pid;
        long childStart = 0;

        while (result.Count < max && current > 0 && seen.Add(current) && table.TryGetValue(current, out var row))
        {
            var (path, start) = QueryProcess(current);
            if (result.Count > 0 && start != 0 && childStart != 0 && start > childStart) break;

            result.Add(new ProcEntry(current, row.ParentPid, NormalizeName(row.ExeFile), path, start));
            childStart = start;
            current = row.ParentPid;
        }
        return result;
    }

    public static long ConsoleWindow() => GetConsoleWindow();

    private static string NormalizeName(string exeFile)
    {
        var name = exeFile.ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }

    private static Dictionary<int, (int ParentPid, string ExeFile)> Snapshot()
    {
        var table = new Dictionary<int, (int, string)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == InvalidHandle || snap == 0) return table;
        try
        {
            ProcessEntry32W entry = default;
            entry.dwSize = (uint)sizeof(ProcessEntry32W);
            for (var ok = Process32FirstW(snap, &entry); ok != 0; ok = Process32NextW(snap, &entry))
            {
                var exe = new string(entry.szExeFile, 0, MAX_PATH);
                var nul = exe.IndexOf('\0');
                if (nul >= 0) exe = exe[..nul];
                table[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, exe);
            }
        }
        finally
        {
            CloseHandle(snap);
        }
        return table;
    }

    /// <summary>Image path and creation FILETIME; ("", 0) when the process can't be opened (e.g. access denied).</summary>
    private static (string Path, long StartFileTime) QueryProcess(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, (uint)pid);
        if (h == 0) return ("", 0);
        try
        {
            var path = "";
            const int cap = 1024;
            var buf = stackalloc char[cap];
            uint size = cap;
            if (QueryFullProcessImageNameW(h, 0, buf, &size) != 0) path = new string(buf, 0, (int)size);

            long creation = 0, exit = 0, kernel = 0, user = 0;
            if (GetProcessTimes(h, &creation, &exit, &kernel, &user) == 0) creation = 0;
            return (path, creation);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32W
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
        public fixed char szExeFile[MAX_PATH];
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll")]
    private static partial int Process32FirstW(nint hSnapshot, ProcessEntry32W* lppe);

    [LibraryImport("kernel32.dll")]
    private static partial int Process32NextW(nint hSnapshot, ProcessEntry32W* lppe);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    private static partial int QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, uint* lpdwSize);

    [LibraryImport("kernel32.dll")]
    private static partial int GetProcessTimes(nint hProcess, long* lpCreationTime, long* lpExitTime, long* lpKernelTime, long* lpUserTime);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();
}
