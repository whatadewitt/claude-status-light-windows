using System.IO;

namespace StatusLight.App;

/// <summary>
/// A tiny append-only text log. Rolls the file to <c>&lt;name&gt;.1.txt</c> once it reaches
/// <paramref name="maxBytes"/> (keeping one previous file). Thread-safe; never throws.
/// </summary>
public sealed class FileLog(string path, long maxBytes = FileLog.DefaultMaxBytes)
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly Lock _gate = new();

    public string Path => path;

    public string RolledPath => System.IO.Path.ChangeExtension(path, ".1" + System.IO.Path.GetExtension(path));

    public void Info(string message) => Write("INFO", message);

    public void Error(string context, Exception e) => Write("ERROR", $"{context}: {e}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                if (System.IO.Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
                var file = new FileInfo(path);
                if (file.Exists && file.Length >= maxBytes) File.Move(path, RolledPath, overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}

/// <summary>The app's log at %LOCALAPPDATA%\ClaudeStatusLight\log.txt.</summary>
public static class Log
{
    public static FileLog Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeStatusLight", "log.txt"));

    public static void Info(string message) => Default.Info(message);

    public static void Error(string context, Exception e) => Default.Error(context, e);
}
