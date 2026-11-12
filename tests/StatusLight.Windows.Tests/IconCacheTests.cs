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
        var gdiBaseline = Win32.GetGuiResources(me.Handle, Win32.GR_GDIOBJECTS);
        var userBaseline = Win32.GetGuiResources(me.Handle, Win32.GR_USEROBJECTS);

        using (var cache = new IconCache(capacity: 64, customIconPath: missingIcon))
        {
            for (var i = 0; i < 5_000; i++)
            {
                // Frames cycle 0-7; sizes, states and agent counts vary so the 64-entry cap evicts constantly.
                var icon = cache.Get(states[i % states.Length], 16 + i % 17, i % 8, i % 3);
                Assert.NotEqual(0, icon.Handle);
            }

            Assert.True(Win32.GetGuiResources(me.Handle, Win32.GR_GDIOBJECTS) <= gdiBaseline + 200);
            Assert.True(Win32.GetGuiResources(me.Handle, Win32.GR_USEROBJECTS) <= userBaseline + 200);
        }

        // Dispose destroys every cached HICON.
        Assert.True(Win32.GetGuiResources(me.Handle, Win32.GR_GDIOBJECTS) <= gdiBaseline + 20);
        Assert.True(Win32.GetGuiResources(me.Handle, Win32.GR_USEROBJECTS) <= userBaseline + 20);
    }
}
