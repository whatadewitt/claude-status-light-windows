namespace StatusLight.Core.Sources;

public enum ProcessLiveness { Alive, Dead, Unknown }

public interface IProcessTable
{
    /// <summary>pidStart == 0 skips the start-time comparison.</summary>
    ProcessLiveness Check(int pid, long pidStart);

    IReadOnlyList<string> ChildCommandLines(int pid);
}
