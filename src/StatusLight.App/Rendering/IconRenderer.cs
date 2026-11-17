using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using StatusLight.Core;

namespace StatusLight.App.Rendering;

/// <summary>
/// Port of the Mac IconRenderer: the pixel mascot tinted to the state colour, or a user-supplied
/// custom icon with a state dot, plus an optional running-subagent badge.
/// </summary>
public static class IconRenderer
{
    public static readonly Color AgentBadgeColor = Color.FromArgb(0x0A, 0x84, 0xFF);

    public static Color StateColor(LightState s) => s switch
    {
        LightState.Working => Color.FromArgb(0xFF, 0xCC, 0x00),
        LightState.Idle => Color.FromArgb(0x34, 0xC7, 0x59),
        LightState.Attention => Color.FromArgb(0xFF, 0x3B, 0x30),
        _ => Color.FromArgb(0x8E, 0x8E, 0x93),
    };

    public static Bitmap Render(LightState state, int side, int frame, int agents, Image? custom)
    {
        var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var rect = new RectangleF(0, 0, side, side);

        if (custom is not null)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(custom, rect);
            DrawStateDot(g, StateColor(state), rect);
        }
        else
        {
            DrawMascot(g, StateColor(state), rect, frame);
        }

        if (agents > 0) DrawAgentBadge(g, agents, rect);
        return bmp;
    }

    /// <summary>
    /// Cell edges are snapped to whole pixels so adjacent cells butt with no cracks, and
    /// antialiasing is off for crisp pixel-art edges at tray size.
    /// </summary>
    private static void DrawMascot(Graphics g, Color color, RectangleF rect, int frame)
    {
        var count = Mascot.Frames.Count;
        var grid = Mascot.Frames[((frame % count) + count) % count];
        var rows = grid.Length;
        var cols = grid[0].Length;
        var cell = Math.Min(rect.Width / cols, rect.Height / rows);
        var originX = rect.X + (rect.Width - cell * cols) / 2;
        var originY = rect.Y + (rect.Height - cell * rows) / 2;
        var xs = Enumerable.Range(0, cols + 1).Select(i => (int)MathF.Round(originX + cell * i)).ToArray();
        var ys = Enumerable.Range(0, rows + 1).Select(i => (int)MathF.Round(originY + cell * i)).ToArray();

        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        using var brush = new SolidBrush(color);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (grid[r][c] != 'X') continue;
                var w = xs[c + 1] - xs[c];
                var h = ys[r + 1] - ys[r];
                if (w > 0 && h > 0) g.FillRectangle(brush, xs[c], ys[r], w, h);
            }
        }
    }

    /// <summary>A white ring with the state-coloured core inset 28 %, pinned bottom-right.</summary>
    private static void DrawStateDot(Graphics g, Color color, RectangleF rect)
    {
        var r = rect.Width * 0.24f;
        var ring = new RectangleF(rect.Right - 2 * r, rect.Bottom - 2 * r, 2 * r, 2 * r);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(Brushes.White, ring);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, RectangleF.Inflate(ring, -r * 0.28f, -r * 0.28f));
    }

    /// <summary>
    /// Running-subagent count: a blue dot with a numeral (9 max), pinned bottom-left so it never
    /// collides with the custom-icon state dot at bottom-right.
    /// </summary>
    private static void DrawAgentBadge(Graphics g, int count, RectangleF rect)
    {
        var r = rect.Width * 0.26f;
        var ring = new RectangleF(rect.X, rect.Bottom - 2 * r, 2 * r, 2 * r);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillEllipse(Brushes.White, ring);
        using var brush = new SolidBrush(AgentBadgeColor);
        g.FillEllipse(brush, RectangleF.Inflate(ring, -r * 0.14f, -r * 0.14f));

        g.TextRenderingHint = TextRenderingHint.AntiAlias; // ClearType needs an opaque background
        using var font = new Font("Segoe UI", r * 1.4f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(count > 9 ? "9" : count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            font, Brushes.White, ring, format);
    }
}
