using System.Text;

namespace StatusLight.Core.Hook;

public static class SessionIds
{
    /// <summary>Keeps only [A-Za-z0-9._-]; falls back to "default" when nothing is left.</summary>
    public static string Sanitize(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "default";
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-')
                sb.Append(c);
        }
        return sb.Length == 0 ? "default" : sb.ToString();
    }
}
