using System.Diagnostics;
using System.Runtime.Versioning;
using StatusLight.App.Platform;
using StatusLight.App.Rendering;
using StatusLight.Core;

namespace StatusLight.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class IconCacheTests
{
    [Fact]
    public void No_gdi_leak_across_many_renders()
    {
        using var me = Process.GetCurrentProcess();
        var missingIcon = Path.Combine(Path.GetTempPath(), $"no-icon-{Guid.NewGuid():N}.png");
        var states = Enum.GetValues<LightState>();
        int Gdi() => (int)Win32.GetGuiResources(me.Handle, Win32.GR_GDIOBJECTS);
        int User() => (int)Win32.GetGuiResources(me.Handle, Win32.GR_USEROBJECTS);

        using var cache = new IconCache(capacity: 64, customIconPath: missingIcon);
        void Render(int from, int count)
        {
            for (var i = from; i < from + count; i++)
            {
                // Frames cycle 0-7; sizes, states and agent counts vary so the 64-entry cap evicts constantly.
                var icon = cache.Get(states[i % states.Length], 16 + i % 17, i % 8, i % 3);
                Assert.NotEqual(0, icon.Handle);
            }
        }

        // Fill the cache first: a full cache legitimately holds 64 HICONs (each a USER object
        // plus colour and mask bitmaps) and GDI+ allocates some objects on first use.
        Render(0, 1_000);
        var gdiFull = Gdi();
        var userFull = User();

        // Steady state: evicting and re-rendering 5 000 more icons must not grow the counts.
        Render(1_000, 5_000);
        var gdiAfter = Gdi();
        var userAfter = User();
        Assert.True(gdiAfter <= gdiFull + 20, $"GDI objects grew from {gdiFull} to {gdiAfter}");
        Assert.True(userAfter <= userFull + 20, $"USER objects grew from {userFull} to {userAfter}");

        // Dispose destroys every cached HICON.
        cache.Dispose();
        var userDisposed = User();
        Assert.True(userDisposed <= userFull - 32,
            $"USER objects went from {userFull} (full cache) to {userDisposed} after Dispose");
        Assert.True(Gdi() <= gdiFull, $"GDI objects went from {gdiFull} (full cache) to {Gdi()} after Dispose");
    }
}
