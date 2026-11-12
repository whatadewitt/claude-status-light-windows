using System.Globalization;

namespace StatusLight.Core.Wsl;

public sealed record PsRow(int Pid, int Ppid, string Args);

public static class WslOutput
{
    /// <summary>Tolerates UTF-16 output decoded as bytes (embedded NULs) and CRLF line endings.</summary>
    public static IReadOnlyList<string> ParseDistroList(string stdout)
    {
        return stdout.TrimStart('\uFEFF').Replace("\0", "").Replace("\r", "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>Parses `ps -eo pid=,ppid=,args=` output; malformed lines are skipped.</summary>
    public static IReadOnlyList<PsRow> ParsePs(string stdout)
    {
        var rows = new List<PsRow>();
        foreach (var raw in stdout.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimStart();
            var i = line.IndexOf(' ');
            if (i <= 0) continue; // needs at least pid and ppid
            var rest = line[i..].TrimStart();
            var j = rest.IndexOf(' ');
            var ppidText = j < 0 ? rest : rest[..j];
            var args = j < 0 ? "" : rest[(j + 1)..].TrimStart();
            if (int.TryParse(line[..i], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && int.TryParse(ppidText, NumberStyles.None, CultureInfo.InvariantCulture, out var ppid))
                rows.Add(new PsRow(pid, ppid, args));
        }
        return rows;
    }
}
