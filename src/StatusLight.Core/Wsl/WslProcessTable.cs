using StatusLight.Core.Sources;

namespace StatusLight.Core.Wsl;

public sealed class WslProcessTable(IReadOnlyList<PsRow> rows) : IProcessTable
{
    private readonly HashSet<int> _pids = rows.Select(r => r.Pid).ToHashSet();

    public ProcessLiveness Check(int pid, long pidStart)
        => _pids.Contains(pid) ? ProcessLiveness.Alive : ProcessLiveness.Dead;

    public IReadOnlyList<string> ChildCommandLines(int pid)
        => rows.Where(r => r.Ppid == pid).OrderBy(r => r.Pid).Select(r => r.Args).ToList();
}
