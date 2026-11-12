using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatusLight.Core;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// User preferences. Positional so the source-generated deserializer applies these parameter
/// defaults to keys missing from the file (init-only properties would get <c>default</c>).
/// </summary>
/// <param name="FloatingX">Floating window position when not locked to a corner (device-independent units).</param>
/// <param name="FloatingY">See <paramref name="FloatingX"/>.</param>
/// <param name="FloatingMonitor">Device name of the monitor the floating window was dragged onto.</param>
public sealed record AppSettings(
    bool ShowFloatingWindow = true,
    bool LockToCorner = true,
    ScreenCorner Corner = ScreenCorner.TopRight,
    bool AutoHide = false,
    bool GreenBeatsYellow = true,
    bool StartAtLogin = true,
    double? FloatingX = null,
    double? FloatingY = null,
    string? FloatingMonitor = null);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
public sealed partial class AppSettingsJsonContext : JsonSerializerContext;

/// <summary>JSON settings file (%APPDATA%\ClaudeStatusLight\settings.json in the app).</summary>
public sealed class SettingsStore(string path)
{
    public event Action<AppSettings>? Changed;

    /// <summary>A missing, unreadable or corrupt file gives the defaults; never throws.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJsonContext.Default.AppSettings)
                ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes atomically (temp file + move). Persistence is best-effort: an IO failure is swallowed
    /// so a UI toggle never crashes the app, and <see cref="Changed"/> still fires so the running
    /// app applies the change.
    /// </summary>
    public void Save(AppSettings s)
    {
        string? tmp = null;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, AppSettingsJsonContext.Default.AppSettings));
            File.Move(tmp, path, overwrite: true);
            tmp = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            if (tmp is not null)
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }
        Changed?.Invoke(s);
    }
}
