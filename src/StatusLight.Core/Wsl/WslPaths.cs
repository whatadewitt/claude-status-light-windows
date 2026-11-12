namespace StatusLight.Core.Wsl;

public static class WslPaths
{
    public static string Map(string root, string linuxPath)
        => Path.Combine([root, .. linuxPath.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
}
