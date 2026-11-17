using System.Runtime.Versioning;
using StatusLight.App.Platform;

namespace StatusLight.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowStylesTests
{
    [Fact]
    public void Floating_ex_style_never_activates_and_stays_out_of_alt_tab()
    {
        var style = WindowStyles.FloatingExStyle(0);
        Assert.Equal(0x08000000, style & 0x08000000); // WS_EX_NOACTIVATE
        Assert.Equal(0x00000080, style & 0x00000080); // WS_EX_TOOLWINDOW
        Assert.Equal(0x00000008, style & 0x00000008); // WS_EX_TOPMOST
    }

    [Fact]
    public void Floating_ex_style_keeps_existing_bits()
    {
        const int layered = 0x00080000; // WS_EX_LAYERED, set by WPF for AllowsTransparency
        Assert.Equal(layered, WindowStyles.FloatingExStyle(layered) & layered);
    }
}
