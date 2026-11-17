using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StatusLight.App.Platform;

/// <summary>Whether this user has any WSL distro registered (HKCU\...\Lxss has a subkey per distro).</summary>
[SupportedOSPlatform("windows")]
public static class WslPresence
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>Never throws; an unreadable key counts as no WSL.</summary>
    public static bool HasDistros()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LxssKey);
            return key is not null && key.SubKeyCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
