namespace StatusLight.Core.Hook;

public sealed record ProcessIdentity(int Pid, long PidStart, string TermProgram, string Tty, long Hwnd)
{
    public static ProcessIdentity Unknown => new(0, 0, "unknown", "", 0);
}
