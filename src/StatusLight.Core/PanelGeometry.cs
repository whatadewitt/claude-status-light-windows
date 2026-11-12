namespace StatusLight.Core;

/// <summary>A rectangle in y-down screen coordinates (device-independent units in the app).</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;
}

/// <summary>A monitor's identity (Screen.DeviceName in the app) and its work area.</summary>
public readonly record struct MonitorArea(string DeviceName, RectD WorkArea);

/// <summary>Where the floating window rests, the work area it belongs to, and the corner it grows from.</summary>
public readonly record struct PanelSpot(RectD Rect, RectD Work, ScreenCorner Anchor);

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
    /// Where a <paramref name="w"/> × <paramref name="h"/> window rests. All values share one unit;
    /// the app uses physical pixels, the unit <see cref="AppSettings.FloatingX"/> is saved in.
    /// <list type="bullet">
    /// <item>Locked, or unlocked but never dragged: the chosen corner of
    /// <paramref name="currentWork"/>, inset by <paramref name="margin"/>.</item>
    /// <item>Unlocked with a saved spot whose monitor still exists and whose point lies inside
    /// that monitor's work area: that spot, clamped into the work area (rows can grow the panel),
    /// anchored at the quadrant it sits in.</item>
    /// <item>Otherwise (monitor gone, point off its work area): the top-right of
    /// <paramref name="primaryWork"/>.</item>
    /// </list>
    /// </summary>
    public static PanelSpot Home(
        double w, double h, AppSettings settings, IReadOnlyList<MonitorArea> monitors,
        RectD currentWork, RectD primaryWork, double margin)
    {
        if (settings.LockToCorner || settings.FloatingX is not { } x || settings.FloatingY is not { } y)
            return new PanelSpot(Corner(settings.Corner, w, h, currentWork, margin), currentWork, settings.Corner);

        foreach (var m in monitors)
        {
            if (m.DeviceName != settings.FloatingMonitor) continue;
            var work = m.WorkArea;
            if (x < work.X || x >= work.Right || y < work.Y || y >= work.Bottom) break;
            var rect = Frame(w, h, ScreenCorner.TopLeft, new RectD(x, y, w, h), work);
            return new PanelSpot(rect, work, Quadrant(rect, work));
        }
        return new PanelSpot(Corner(ScreenCorner.TopRight, w, h, primaryWork, margin), primaryWork, ScreenCorner.TopRight);
    }

    /// <summary>A <paramref name="w"/> × <paramref name="h"/> rect in <paramref name="corner"/> of <paramref name="work"/>, inset by <paramref name="margin"/>.</summary>
    public static RectD Corner(ScreenCorner corner, double w, double h, RectD work, double margin) => corner switch
    {
        ScreenCorner.TopLeft => new RectD(work.X + margin, work.Y + margin, w, h),
        ScreenCorner.TopRight => new RectD(work.Right - w - margin, work.Y + margin, w, h),
        ScreenCorner.BottomLeft => new RectD(work.X + margin, work.Bottom - h - margin, w, h),
        _ => new RectD(work.Right - w - margin, work.Bottom - h - margin, w, h),
    };

    /// <summary>
    /// Pop the panel out when a session starts waiting for input, not on every refresh while one
    /// keeps waiting.
    /// </summary>
    public static bool ShouldPopOut(IReadOnlySet<string> waiting, IReadOnlySet<string> previouslyWaiting) =>
        waiting.Any(id => !previouslyWaiting.Contains(id));
}
