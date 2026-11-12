using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using StatusLight.App.Platform;
using StatusLight.App.Rendering;
using StatusLight.Core;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontWeights = System.Windows.FontWeights;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ImageSource = System.Windows.Media.ImageSource;
using Size = System.Windows.Size;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Thickness = System.Windows.Thickness;
using UIElement = System.Windows.UIElement;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Visibility = System.Windows.Visibility;
using Window = System.Windows.Window;

namespace StatusLight.App.Floating;

/// <summary>
/// The floating status window's view: header, session rows and the auto-hide tile. Placement,
/// hover and settings logic live in <see cref="FloatingController"/>. It never takes focus:
/// <see cref="WindowStyles.FloatingExStyle"/> is applied as soon as the HWND exists, and nothing
/// in it is focusable, so clicks work without activating it.
/// </summary>
public partial class FloatingWindow : Window
{
    private const int WM_MOVING = 0x0216;
    private const string HeaderPrefix = "Claude Code — ";

    private static readonly Dictionary<LightState, Brush> DotBrushes = Enum.GetValues<LightState>()
        .ToDictionary(s => s, Brush (s) => Frozen(IconRenderer.StateColor(s)));

    private double? _titleWidth;
    private List<RowView> _shownRows = [];
    private SessionState[] _sessions = [];
    private UIElement? _pressedRow;

    /// <summary>Everything a row shows; rows are rebuilt only when this changes.</summary>
    private readonly record struct RowView(string Text, LightState State, string Tag, string Tooltip, bool Parked);

    public FloatingWindow()
    {
        InitializeComponent();
        Header.MouseLeftButtonDown += OnHeaderMouseDown;
    }

    /// <summary>A listed session was clicked.</summary>
    public event Action<SessionState>? SessionClicked;

    /// <summary>A header drag is starting (<see cref="IsDragging"/> is already true).</summary>
    public event Action? DragStarted;

    /// <summary>A header drag finished; the argument says whether the window moved.</summary>
    public event Action<bool>? DragEnded;

    /// <summary>Whether the header drags the window (off while locked to a corner).</summary>
    public bool Draggable { get; set; }

    /// <summary>True inside the drag's modal move loop, which keeps pumping polls and dance frames.</summary>
    public bool IsDragging { get; private set; }

    private nint Hwnd => new WindowInteropHelper(this).Handle;

    /// <summary>The window's rect in physical screen pixels.</summary>
    public RectD Frame =>
        Win32.GetWindowRect(Hwnd, out var r) ? new RectD(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;

    /// <summary>
    /// Moves and sizes the window in one SetWindowPos, in physical pixels, without activating it.
    /// Left/Top/Width/Height are never set, so WPF never moves it in DIPs behind our back.
    /// </summary>
    public void SetFrame(RectD px)
    {
        var hwnd = Hwnd;
        if (hwnd == 0) return;
        int x = (int)Math.Round(px.X), y = (int)Math.Round(px.Y);
        int w = (int)Math.Round(px.Width), h = (int)Math.Round(px.Height);
        if (Frame == new RectD(x, y, w, h)) return;
        Win32.SetWindowPos(hwnd, 0, x, y, w, h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Shows the full panel, or only the auto-hide tile.</summary>
    public void SetMode(bool full)
    {
        Full.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        TileIcon.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetIcons(ImageSource header, ImageSource? tile)
    {
        HeaderIcon.Source = header;
        if (tile is not null) TileIcon.Source = tile;
    }

    public void SetHeader(LightState aggregate) => HeaderText.Text = HeaderPrefix + LightStates.Label(aggregate);

    /// <summary>
    /// Every poll brings new session objects, but the rows are only rebuilt when what they show
    /// changes, so an open row tooltip survives the 1 s refresh. Clicks always use the latest state.
    /// </summary>
    public void SetSessions(IReadOnlyList<SessionState> sessions, DateTimeOffset now)
    {
        _sessions = [.. sessions];
        var views = sessions.Select(s => new RowView(
            SessionText.PanelRow(s), s.State, SessionText.TagText(s.Tag), SessionText.Tooltip(s, now), s.IsParked(now)))
            .ToList();
        if (Rows.Children.Count > 0 && views.SequenceEqual(_shownRows)) return;
        _shownRows = views;

        Rows.Children.Clear();
        if (views.Count == 0)
        {
            var empty = new TextBlock { Text = "No active sessions" };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "PanelSecondary");
            Rows.Children.Add(empty);
            return;
        }
        for (var i = 0; i < views.Count; i++) Rows.Children.Add(MakeRow(views[i], i));
    }

    /// <summary>
    /// The full panel's size: as wide as its widest row, and never narrower than the longest
    /// possible header, so a state change never resizes it.
    /// </summary>
    public (double Width, double Height) MeasureFull()
    {
        HeaderText.MinWidth = _titleWidth ??= Enum.GetValues<LightState>().Max(s =>
        {
            var probe = new TextBlock
            {
                Text = HeaderPrefix + LightStates.Label(s),
                FontFamily = HeaderText.FontFamily,
                FontSize = HeaderText.FontSize,
                FontWeight = HeaderText.FontWeight,
            };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        });
        var root = (UIElement)Content;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (Math.Ceiling(root.DesiredSize.Width), Math.Ceiling(root.DesiredSize.Height));
    }

    /// <summary>Follows the Windows app theme (light or dark) with a translucent background.</summary>
    public void ApplyTheme(bool light)
    {
        Resources["PanelBackground"] = Frozen(light ? Color.FromArgb(0xE6, 0xF3, 0xF3, 0xF3) : Color.FromArgb(0xE6, 0x20, 0x20, 0x20));
        Resources["PanelBorder"] = Frozen(light ? Color.FromArgb(0x1F, 0x00, 0x00, 0x00) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Resources["PanelForeground"] = Frozen(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF2, 0xF2, 0xF2));
        Resources["PanelSecondary"] = Frozen(light ? Color.FromArgb(0x99, 0x00, 0x00, 0x00) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        Resources["PillBackground"] = Frozen(light ? Color.FromArgb(0x1A, 0x00, 0x00, 0x00) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.ApplyFloating(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    /// <summary>
    /// A WS_EX_NOACTIVATE window is not repositioned live during a system move loop (it only jumps
    /// on release); moving it to each proposed rect keeps the drag smooth.
    /// </summary>
    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOVING && lParam != 0)
        {
            var r = Marshal.PtrToStructure<Win32.RECT>(lParam);
            Win32.SetWindowPos(hwnd, 0, r.Left, r.Top, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
        return 0;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Draggable || e.ButtonState != MouseButtonState.Pressed) return;
        var before = Frame;
        IsDragging = true;
        try
        {
            DragStarted?.Invoke();
            DragMove(); // returns when the button is released
        }
        catch (InvalidOperationException)
        {
            // The button was already up: nothing moved, reported below as an unmoved drag.
        }
        finally
        {
            IsDragging = false;
        }
        var after = Frame;
        DragEnded?.Invoke((before.X, before.Y) != (after.X, after.Y));
    }

    private UIElement MakeRow(RowView view, int index)
    {
        var line = new DockPanel { LastChildFill = true };

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = DotBrushes[view.State],
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dot, Dock.Left);
        line.Children.Add(dot);

        if (view.Tag.Length > 0)
        {
            var tagText = new TextBlock { Text = view.Tag, FontSize = 10.5 };
            tagText.SetResourceReference(TextBlock.ForegroundProperty, "PanelSecondary");
            var pill = new Border
            {
                Child = tagText,
                CornerRadius = new System.Windows.CornerRadius(7),
                Padding = new Thickness(6, 0, 6, 1),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            pill.SetResourceReference(Border.BackgroundProperty, "PillBackground");
            DockPanel.SetDock(pill, Dock.Right);
            line.Children.Add(pill);
        }

        line.Children.Add(new TextBlock { Text = view.Text, VerticalAlignment = VerticalAlignment.Center });

        // Transparent (not null) background so the whole row is clickable; Border is not focusable.
        var row = new Border
        {
            Child = line,
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = Cursors.Hand,
            ToolTip = view.Tooltip,
            Opacity = view.Parked ? 0.5 : 1,
        };
        // A click is a press and release on the same row (not a drag that ends over it).
        row.MouseLeftButtonDown += (_, _) => _pressedRow = row;
        row.MouseLeftButtonUp += (_, e) =>
        {
            var pressed = ReferenceEquals(_pressedRow, row);
            _pressedRow = null;
            if (!pressed) return;
            e.Handled = true;
            if (index < _sessions.Length) SessionClicked?.Invoke(_sessions[index]);
        };
        return row;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(System.Drawing.Color c) => Frozen(Color.FromArgb(c.A, c.R, c.G, c.B));
}
