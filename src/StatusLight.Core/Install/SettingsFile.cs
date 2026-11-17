using System.Text;

namespace StatusLight.Core.Install;

/// <summary>File-level wrapper around <see cref="HooksMerger"/>: backup, BOM handling, directory creation.</summary>
public static class SettingsFile
{
    /// <summary>Returns 0 on success (changed or not), 2 for a blank hookPath, invalid JSON, or non-UTF-8 content (nothing written).</summary>
    public static int AddHooks(string settingsPath, string hookPath, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(hookPath) || !TryRead(settingsPath, out var text)
            ? 2
            : Apply(settingsPath, now, HooksMerger.Add(text, hookPath));

    /// <summary>Same return codes as <see cref="AddHooks"/>.</summary>
    public static int RemoveHooks(string settingsPath, string hookPath, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(hookPath) || !TryRead(settingsPath, out var text)
            ? 2
            : Apply(settingsPath, now, HooksMerger.Remove(text, hookPath));

    private static int Apply(string settingsPath, DateTimeOffset now, (MergeOutcome Outcome, string? Json) result)
    {
        switch (result.Outcome)
        {
            case MergeOutcome.InvalidJson: return 2;
            case MergeOutcome.Unchanged: return 0;
        }

        var linkDir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (!string.IsNullOrEmpty(linkDir)) Directory.CreateDirectory(linkDir);

        // Write through a symlinked settings.json rather than replacing the link.
        var target = (File.Exists(settingsPath)
            ? new FileInfo(settingsPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName
            : null) ?? settingsPath;

        var dir = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(target)) File.Copy(target, BackupName(settingsPath, now));

        var tmp = target + ".tmp";
        try
        {
            File.WriteAllText(tmp, result.Json!, new UTF8Encoding(false));
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        return 0;
    }

    private static string BackupName(string settingsPath, DateTimeOffset now)
    {
        var baseName = $"{settingsPath}.bak.{now.ToUnixTimeSeconds()}";
        var name = baseName;
        for (var i = 1; File.Exists(name); i++) name = $"{baseName}.{i}";
        return name;
    }

    /// <summary>Reads strictly as UTF-8 (BOM stripped); false if the bytes aren't valid UTF-8. A missing file yields null text.</summary>
    private static bool TryRead(string path, out string? text)
    {
        text = null;
        if (!File.Exists(path)) return true;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
