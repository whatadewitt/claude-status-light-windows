using StatusLight.Core;

namespace StatusLight.Core.Tests;

/// <summary>
/// Ported from the Mac PanelGeometryTests.swift with y flipped: Windows is y-down, so a Mac
/// rect at y becomes <c>screen.Height - (y + height)</c> and Mac maxY/minY become Y/Bottom.
/// </summary>
public sealed class PanelGeometryTests
{
    private static readonly RectD Screen = new(0, 0, 1000, 800);

    [Fact]
    public void Quadrant_follows_the_tile_center()
    {
        Assert.Equal(ScreenCorner.TopRight, PanelGeometry.Quadrant(new RectD(900, 60, 40, 40), Screen));
        Assert.Equal(ScreenCorner.TopLeft, PanelGeometry.Quadrant(new RectD(10, 60, 40, 40), Screen));
        Assert.Equal(ScreenCorner.BottomLeft, PanelGeometry.Quadrant(new RectD(10, 750, 40, 40), Screen));
        Assert.Equal(ScreenCorner.BottomRight, PanelGeometry.Quadrant(new RectD(900, 750, 40, 40), Screen));
    }

    [Fact]
    public void Quadrant_on_a_full_hd_screen()
    {
        var hd = new RectD(0, 0, 1920, 1080);
        Assert.Equal(ScreenCorner.TopRight, PanelGeometry.Quadrant(new RectD(1868, 8, 44, 44), hd));
    }

    [Fact]
    public void Quadrant_respects_a_secondary_monitor_offset()
    {
        // A monitor left of and above the primary: negative origin.
        var left = new RectD(-1920, -200, 1920, 1080);
        Assert.Equal(ScreenCorner.TopRight, PanelGeometry.Quadrant(new RectD(-60, -190, 44, 44), left));
        Assert.Equal(ScreenCorner.BottomLeft, PanelGeometry.Quadrant(new RectD(-1910, 820, 44, 44), left));
    }

    [Fact]
    public void Expanding_keeps_the_anchored_corner_fixed()
    {
        var tile = new RectD(944, 16, 40, 40); // top-right, 16 px margins
        const double w = 220, h = 120;

        var tr = PanelGeometry.Frame(w, h, ScreenCorner.TopRight, tile, Screen);
        Assert.Equal(tile.Right, tr.Right);
        Assert.Equal(tile.Y, tr.Y);
        Assert.Equal((w, h), (tr.Width, tr.Height));

        var bl = PanelGeometry.Frame(w, h, ScreenCorner.BottomLeft, new RectD(16, 744, 40, 40), Screen);
        Assert.Equal(16, bl.X);
        Assert.Equal(784, bl.Bottom);
        Assert.Equal((w, h), (bl.Width, bl.Height));

        var tl = PanelGeometry.Frame(w, h, ScreenCorner.TopLeft, new RectD(16, 16, 40, 40), Screen);
        Assert.Equal(16, tl.X);
        Assert.Equal(16, tl.Y);

        var br = PanelGeometry.Frame(w, h, ScreenCorner.BottomRight, new RectD(944, 744, 40, 40), Screen);
        Assert.Equal(984, br.Right);
        Assert.Equal(784, br.Bottom);
    }

    [Fact]
    public void Top_right_frame_on_a_full_hd_screen_keeps_right_and_top()
    {
        var hd = new RectD(0, 0, 1920, 1080);
        var tile = new RectD(1868, 8, 44, 44);
        var f = PanelGeometry.Frame(300, 160, PanelGeometry.Quadrant(tile, hd), tile, hd);
        Assert.Equal(tile.Right, f.Right);
        Assert.Equal(tile.Y, f.Y);
    }

    [Fact]
    public void Expanded_frame_is_clamped_on_screen()
    {
        // A tile dragged near the middle-top: anchoring top-left would still push a very wide
        // panel past the right edge.
        var tile = new RectD(480, 16, 40, 40);
        var f = PanelGeometry.Frame(700, 900, ScreenCorner.TopLeft, tile, Screen);
        Assert.True(f.Right <= Screen.Right);
        Assert.True(f.Bottom <= Screen.Bottom);
    }

    [Fact]
    public void Frame_that_would_spill_below_the_work_area_is_nudged_up_inside_it()
    {
        // Secondary monitor to the right of a 1920-wide primary, taskbar at its bottom.
        var work = new RectD(1920, 0, 1920, 1040);
        var tile = new RectD(2000, 900, 44, 44); // top-left anchored, but low on the screen
        var f = PanelGeometry.Frame(220, 300, ScreenCorner.TopLeft, tile, work);
        Assert.Equal(work.Bottom, f.Bottom);
        Assert.Equal(740, f.Y);
        Assert.Equal(tile.X, f.X);
        Assert.Equal((220d, 300d), (f.Width, f.Height));
    }

    [Fact]
    public void Pops_out_only_for_newly_waiting_sessions()
    {
        Assert.True(PanelGeometry.ShouldPopOut(Set("a"), Set()));
        Assert.False(PanelGeometry.ShouldPopOut(Set("a"), Set("a")));
        Assert.True(PanelGeometry.ShouldPopOut(Set("a", "b"), Set("a")));
        Assert.False(PanelGeometry.ShouldPopOut(Set(), Set("a")));
    }

    [Fact]
    public void RectD_bottom_is_y_plus_height()
    {
        var r = new RectD(10, 20, 30, 40);
        Assert.Equal(60, r.Bottom);
        Assert.Equal(40, r.Right);
    }

    // Home: where the window rests, in physical pixels. A 1920x1080 primary (taskbar 40 px) and a
    // larger monitor to its right, as with a 150 % 4K-ish screen.
    private const string Primary = @"\\.\DISPLAY1";
    private const string Second = @"\\.\DISPLAY2";
    private static readonly RectD PrimaryWork = new(0, 0, 1920, 1040);
    private static readonly RectD SecondWork = new(1920, 0, 2560, 1400);
    private static readonly MonitorArea[] Monitors = [new(Primary, PrimaryWork), new(Second, SecondWork)];

    private static PanelSpot Home(AppSettings s, double w = 200, double h = 100) =>
        PanelGeometry.Home(w, h, s, Monitors, currentWork: SecondWork, primaryWork: PrimaryWork, margin: 8);

    [Theory]
    [InlineData(ScreenCorner.TopLeft, 1928, 8)]
    [InlineData(ScreenCorner.TopRight, 4272, 8)]
    [InlineData(ScreenCorner.BottomLeft, 1928, 1292)]
    [InlineData(ScreenCorner.BottomRight, 4272, 1292)]
    public void Locked_pins_to_the_chosen_corner_of_the_current_work_area(ScreenCorner corner, double x, double y)
    {
        // A saved drag position is ignored while locked.
        var spot = Home(new AppSettings(LockToCorner: true, Corner: corner, FloatingX: 100, FloatingY: 100, FloatingMonitor: Primary));
        Assert.Equal(new RectD(x, y, 200, 100), spot.Rect);
        Assert.Equal(SecondWork, spot.Work);
        Assert.Equal(corner, spot.Anchor);
    }

    [Fact]
    public void Unlocked_and_never_dragged_stays_in_the_chosen_corner()
    {
        var spot = Home(new AppSettings(LockToCorner: false, Corner: ScreenCorner.BottomLeft));
        Assert.Equal(new RectD(1928, 1292, 200, 100), spot.Rect);
        Assert.Equal(ScreenCorner.BottomLeft, spot.Anchor);
    }

    [Fact]
    public void Unlocked_restores_the_saved_pixel_position_on_its_monitor()
    {
        var spot = Home(new AppSettings(LockToCorner: false, FloatingX: 2500, FloatingY: 300, FloatingMonitor: Second));
        Assert.Equal(new RectD(2500, 300, 200, 100), spot.Rect);
        Assert.Equal(SecondWork, spot.Work);
        Assert.Equal(ScreenCorner.TopLeft, spot.Anchor); // the quadrant the spot sits in
    }

    [Theory]
    [InlineData(@"\\.\DISPLAY3", 2500, 300)] // monitor unplugged
    [InlineData(null, 2500, 300)]               // no monitor recorded
    [InlineData(Second, 100, 100)]              // point is on another monitor, not the named one
    [InlineData(Second, 4480, 300)]             // right edge is outside the work area
    [InlineData(Second, 2500, -50)]             // above the work area
    public void Unlocked_falls_back_to_the_primary_top_right_when_the_saved_spot_is_gone(string? monitor, double x, double y)
    {
        var spot = Home(new AppSettings(LockToCorner: false, Corner: ScreenCorner.BottomLeft,
            FloatingX: x, FloatingY: y, FloatingMonitor: monitor));
        Assert.Equal(new RectD(1712, 8, 200, 100), spot.Rect);
        Assert.Equal(PrimaryWork, spot.Work);
        Assert.Equal(ScreenCorner.TopRight, spot.Anchor);
    }

    [Fact]
    public void A_saved_spot_is_clamped_inside_its_work_area_as_the_panel_grows()
    {
        var spot = Home(new AppSettings(LockToCorner: false, FloatingX: 4300, FloatingY: 1300, FloatingMonitor: Second),
            w: 300, h: 200);
        Assert.Equal(new RectD(4180, 1200, 300, 200), spot.Rect);
        Assert.Equal(ScreenCorner.BottomRight, spot.Anchor);
    }

    private static HashSet<string> Set(params string[] ids) => [.. ids];
}
