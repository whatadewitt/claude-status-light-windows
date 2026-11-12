namespace StatusLight.Core;

/// <summary>A rectangle in y-down screen coordinates (device-independent units in the app).</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
}

/// <summary>
/// Pure placement logic for the auto-hiding floating window (port of the Mac PanelGeometry, with
/// y flipped for Windows' y-down coordinates): the collapsed tile keeps one corner pinned and the
/// full panel grows away from it, toward the middle of the screen.
/// </summary>
public static class PanelGeometry
{
    /// <summary>
    /// The screen quadrant the tile's center sits in: the corner the panel should stay pinned to so
    /// it expands inward. A center exactly on a midline counts as top / right, as on the Mac.
    /// </summary>
    public static ScreenCorner Quadrant(RectD rect, RectD screen)
    {
        var top = rect.MidY <= screen.MidY;
        var left = rect.MidX < screen.MidX;
        return (top, left) switch
        {
            (true, true) => ScreenCorner.TopLeft,
            (true, false) => ScreenCorner.TopRight,
            (false, true) => ScreenCorner.BottomLeft,
            (false, false) => ScreenCorner.BottomRight,
        };
    }

    /// <summary>
    /// A <paramref name="w"/> × <paramref name="h"/> frame sharing <paramref name="keeping"/> with
    /// <paramref name="of"/>, nudged back inside <paramref name="within"/> if it would spill off an
    /// edge. When it is larger than the area, the right and bottom edges win.
    /// </summary>
    public static RectD Frame(double w, double h, ScreenCorner keeping, RectD of, RectD within)
    {
        var (x, y) = keeping switch
        {
            ScreenCorner.TopLeft => (of.X, of.Y),
            ScreenCorner.TopRight => (of.Right - w, of.Y),
            ScreenCorner.BottomLeft => (of.X, of.Bottom - h),
            _ => (of.Right - w, of.Bottom - h),
        };
        x = Math.Min(Math.Max(x, within.X), within.Right - w);
        y = Math.Min(Math.Max(y, within.Y), within.Bottom - h);
        return new RectD(x, y, w, h);
    }

    /// <summary>
    /// Pop the panel out when a session starts waiting for input, not on every refresh while one
    /// keeps waiting.
    /// </summary>
    public static bool ShouldPopOut(IReadOnlySet<string> waiting, IReadOnlySet<string> previouslyWaiting) =>
        waiting.Any(id => !previouslyWaiting.Contains(id));
}
