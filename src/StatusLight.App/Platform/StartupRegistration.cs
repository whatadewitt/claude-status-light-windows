using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StatusLight.App.Platform;

/// <summary>
/// Start at login through the per-user Run key (HKCU\...\CurrentVersion\Run), which needs no
/// elevation. Registry errors propagate; the app's caller catches them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupRegistration(string valueName = "ClaudeStatusLight")
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Writes the value <c>"&lt;exePath&gt;"</c> (quoted) when enabled, removes it otherwise.</summary>
    public void Apply(bool enabled, string exePath)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(valueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) is not null;
    }
}
