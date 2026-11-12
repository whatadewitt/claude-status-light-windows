using System.Text;

namespace StatusLight.Core.Install;

/// <summary>File-level wrapper around <see cref="HooksMerger"/>: backup, BOM handling, directory creation.</summary>
public static class SettingsFile
{
    public static int AddHooks(string settingsPath, string hookPath, DateTimeOffset now) =>
        Apply(settingsPath, now, HooksMerger.Add(ReadOrNull(settingsPath), hookPath));

    public static int RemoveHooks(string settingsPath, string hookPath, DateTimeOffset now) =>
        Apply(settingsPath, now, HooksMerger.Remove(ReadOrNull(settingsPath), hookPath));

    private static int Apply(string settingsPath, DateTimeOffset now, (MergeOutcome Outcome, string? Json) result)
    {
        switch (result.Outcome)
        {
            case MergeOutcome.InvalidJson: return 2;
            case MergeOutcome.Unchanged: return 0;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(settingsPath)) File.Copy(settingsPath, BackupName(settingsPath, now));

        var tmp = settingsPath + ".tmp";
        File.WriteAllText(tmp, result.Json!, new UTF8Encoding(false));
        File.Move(tmp, settingsPath, overwrite: true);
        return 0;
    }

    private static string BackupName(string settingsPath, DateTimeOffset now)
    {
        var baseName = $"{settingsPath}.bak.{now.ToUnixTimeSeconds()}";
        var name = baseName;
        for (var i = 1; File.Exists(name); i++) name = $"{baseName}.{i}";
        return name;
    }

    private static string? ReadOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        return File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
    }
}
