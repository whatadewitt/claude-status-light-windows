namespace StatusLight.Core.Hook;

/// <summary>One process in the hook's ancestor chain. <see cref="Name"/> is lowercase without ".exe";
/// <see cref="StartFileTime"/> is the creation time as FILETIME UTC ticks (0 = unknown).</summary>
public sealed record ProcEntry(int Pid, int ParentPid, string Name, string ImagePath, long StartFileTime);
