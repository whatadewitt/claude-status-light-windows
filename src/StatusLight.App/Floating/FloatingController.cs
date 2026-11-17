using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using StatusLight.App.Platform;
using StatusLight.App.Rendering;
using StatusLight.Core;
using StatusLight.Core.Sources;
using ImageSource = System.Windows.Media.ImageSource;
using Int32Rect = System.Windows.Int32Rect;
using Screen = System.Windows.Forms.Screen;
using Visual = System.Windows.Media.Visual;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using Window = System.Windows.Window;

namespace StatusLight.App.Floating;

/// <summary>
/// Drives <see cref="FloatingWindow"/> (port of the Mac FloatingPanelController, y-down): shows or
/// hides it with the ShowFloatingWindow setting, pins it to a corner or the saved drag position,
/// and with auto-hide rests it as a mascot tile that expands on hover, or by itself when a session
/// starts waiting for input (staying out until hovered and left, or the wait clears).
/// Placement is in physical pixels (monitor work areas, the saved position and the HWND rect);
/// only the content size and the tile/margin constants start as DIPs and are scaled by the
/// window's current DPI in <see cref="ScreenUnits"/>. After a move onto a monitor with another
/// scale factor, DpiChanged re-places at the new scale. Never activates or focuses the window.
/// </summary>
public sealed class FloatingController : IDisposable
{
    private const double Margin = 8;
    private const double TileSide = 44;
    private const double TileIconSide = 28;
    private const double HeaderIconSide = 20;
    private static readonly TimeSpan CollapseGrace = TimeSpan.FromSeconds(0.4);

    private readonly AppHost _host;
    private readonly SettingsStore _store;
    private readonly IconCache _icons;
    private readonly FloatingWindow _window = new();
    private readonly DispatcherTimer _collapseTimer;
    private AppSettings _settings;
    private MonitorSnapshot? _rendered;
    private bool _expanded;
    private bool _hovering;
    private bool _heldOpenForInput;
    /// <summary>
    /// Corner the tile stays pinned to while expanded: fixed while collapsed so dragging the open
    /// panel doesn't flip its growth direction.
    /// </summary>
    private ScreenCorner _anchor = ScreenCorner.TopRight;
    /// <summary>Expanded state and anchor captured at drag start; the drag end saves from these.</summary>
    private bool _dragFromExpanded;
    private ScreenCorner _dragAnchor;
    private bool _disposed;

    public FloatingController(AppHost host, SettingsStore store, IconCache icons)
    {
        _host = host;
        _store = store;
        _icons = icons;
        _settings = host.Settings;

        _collapseTimer = new DispatcherTimer(DispatcherPriority.Normal, _window.Dispatcher) { Interval = CollapseGrace };
        _collapseTimer.Tick += (_, _) => CollapseAfterGrace();

        _window.MouseEnter += (_, _) => HoverChanged(true);
        _window.MouseLeave += (_, _) => HoverChanged(false);
        _window.DragStarted += OnDragStarted;
        _window.DragEnded += OnDragEnded;
        _window.SessionClicked += s => WindowFocuser.Focus(s);
        // Deferred: WPF moves the window to the system's suggested rect after raising DpiChanged.
        _window.DpiChanged += (_, _) => _window.Dispatcher.BeginInvoke(() =>
        {
            RenderIcons();
            Place();
        });
        // Creates the HWND (SourceInitialized applies the no-activate styles) before the first show,
        // so its DPI and monitor are known for the first placement.
        new WindowInteropHelper(_window).EnsureHandle();
        _window.ApplyTheme(AppsUseLightTheme());
        _window.Draggable = !_settings.LockToCorner;

        _host.Updated += OnUpdated;
        _store.Changed += OnSettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        OnUpdated(_host.Current);
        ApplyVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Updated -= OnUpdated;
        _store.Changed -= OnSettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _collapseTimer.Stop();
        _window.Close();
    }

    /// <summary>
    /// Called after every poll and every dance frame. A dance frame re-sends the same snapshot,
    /// which only swaps the mascot; rows and pop-out are handled once per new snapshot.
    /// </summary>
    private void OnUpdated(MonitorSnapshot snapshot)
    {
        if (_disposed) return;
        var fresh = !ReferenceEquals(snapshot, _rendered);
        _rendered = snapshot;
        if (fresh)
        {
            _window.SetHeader(snapshot.Aggregate);
            _window.SetSessions(snapshot.Sessions, DateTimeOffset.UtcNow);
            if (snapshot.PopOut) _heldOpenForInput = true;
            else if (!snapshot.Sessions.Any(s => s.State == LightState.Attention)) _heldOpenForInput = false;
        }
        RenderIcons();
        if (fresh) Arrange();
    }

    private void OnSettingsChanged(AppSettings s)
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.BeginInvoke(() => OnSettingsChanged(s));
            return;
        }
        if (_disposed) return;
        _settings = s;
        _window.Draggable = !s.LockToCorner;
        if (!s.AutoHide) _collapseTimer.Stop();
        RenderIcons();
        Arrange();
        ApplyVisibility();
    }

    // Deferred so WinForms' Screen has dropped its cached monitors and work areas first.
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _window.ApplyTheme(AppsUseLightTheme());
            Place();
        });

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            RenderIcons();
            Place();
        });

    private void ApplyVisibility()
    {
        if (_settings.ShowFloatingWindow)
        {
            if (_window.IsVisible) return;
            _window.Show(); // ShowActivated=false: shown without activation
            Place();        // the HWND rect is ours; reassert it after WPF's first-show setup
        }
        else if (_window.IsVisible)
        {
            _window.Hide();
            _hovering = false;
            _collapseTimer.Stop();
        }
    }

    // Auto-hide

    private void HoverChanged(bool inside)
    {
        if (_window.IsDragging) return; // the pointer moves with the window; nothing really changed
        _hovering = inside;
        _collapseTimer.Stop();
        if (!_settings.AutoHide) return;
        if (inside)
        {
            _expanded = true;
            Place();
            return;
        }
        _heldOpenForInput = false;
        _collapseTimer.Start(); // grace period so brushing past the edge doesn't snap it shut
    }

    private void CollapseAfterGrace()
    {
        _collapseTimer.Stop();
        if (_disposed || _hovering || _heldOpenForInput || !_settings.AutoHide) return;
        _expanded = false;
        Place();
    }

    /// <summary>Recomputes expanded state from hover / hold-open, then places the window.</summary>
    private void Arrange()
    {
        _expanded = _settings.AutoHide &&
            (_hovering || _heldOpenForInput || (_expanded && _collapseTimer.IsEnabled));
        Place();
    }

    // Placement

    /// <summary>
    /// Sizes and positions the window for the current mode. Without auto-hide it is the full panel
    /// at the saved spot or locked corner; with it, that spot belongs to the tile and the full
    /// panel grows out of it, away from the anchored corner.
    /// </summary>
    private void Place()
    {
        if (_disposed || _window.IsDragging) return; // the drag's end saves and re-places
        var autoHide = _settings.AutoHide;
        _window.SetMode(full: !autoHide || _expanded);
        var scale = ScreenUnits.Scale(_window);

        RectD target;
        if (!autoHide)
        {
            var (w, h) = FullSize(scale);
            target = Home(w, h, scale).Rect;
        }
        else
        {
            var side = ScreenUnits.ToPixels(TileSide, scale);
            var tile = Home(side, side, scale);
            if (!_expanded || _settings.LockToCorner) _anchor = tile.Anchor;
            if (_expanded)
            {
                var (w, h) = FullSize(scale);
                target = PanelGeometry.Frame(w, h, _anchor, tile.Rect, tile.Work);
            }
            else
            {
                target = tile.Rect;
            }
        }
        _window.SetFrame(target);
    }

    private (double Width, double Height) FullSize(double scale)
    {
        var (w, h) = _window.MeasureFull();
        return (ScreenUnits.ToPixels(w, scale), ScreenUnits.ToPixels(h, scale));
    }

    private PanelSpot Home(double w, double h, double scale) => PanelGeometry.Home(
        w, h, _settings, ScreenUnits.Monitors(),
        currentWork: ScreenUnits.WorkArea(ScreenUnits.Current(_window)),
        primaryWork: ScreenUnits.WorkArea(ScreenUnits.Primary()),
        margin: ScreenUnits.ToPixels(Margin, scale));

    private void OnDragStarted()
    {
        _collapseTimer.Stop();
        _dragFromExpanded = _settings.AutoHide && _expanded;
        _dragAnchor = _anchor;
    }

    /// <summary>
    /// Saves the dragged position once, when the drag ends: the window's top-left in pixels, or,
    /// when the auto-hide panel was open at drag start, the tile's in the anchored corner. It is
    /// kept inside the monitor's work area so a restore never treats it as off-screen.
    /// </summary>
    private void OnDragEnded(bool moved)
    {
        if (_disposed) return;
        if (!moved || _settings.LockToCorner)
        {
            Place(); // catch up on updates skipped during the drag
            return;
        }
        var f = _window.Frame;
        var spot = f;
        if (_dragFromExpanded)
        {
            var side = ScreenUnits.ToPixels(TileSide, ScreenUnits.Scale(_window));
            var left = _dragAnchor is ScreenCorner.TopLeft or ScreenCorner.BottomLeft;
            var top = _dragAnchor is ScreenCorner.TopLeft or ScreenCorner.TopRight;
            spot = new RectD(left ? f.X : f.Right - side, top ? f.Y : f.Bottom - side, side, side);
        }
        var screen = ScreenUnits.Current(_window);
        spot = PanelGeometry.Frame(spot.Width, spot.Height, ScreenCorner.TopLeft, spot, ScreenUnits.WorkArea(screen));
        _store.Save(_settings with { FloatingX = spot.X, FloatingY = spot.Y, FloatingMonitor = screen.DeviceName });
    }

    // Rendering

    /// <summary>The 20 px header mascot, plus the 28 px tile mascot under auto-hide, at the current dance frame.</summary>
    private void RenderIcons()
    {
        if (_disposed || _rendered is not { } snapshot) return;
        var scale = ScreenUnits.Scale(_window);
        _window.SetIcons(
            Render(snapshot, HeaderIconSide * scale),
            _settings.AutoHide ? Render(snapshot, TileIconSide * scale) : null);
    }

    /// <summary>Copies the cached HICON's pixels; the cache keeps ownership of the handle.</summary>
    private ImageSource Render(MonitorSnapshot snapshot, double sidePixels)
    {
        var icon = _icons.Get(snapshot.Aggregate, Math.Max(1, (int)Math.Round(sidePixels)), _host.DanceFrame, snapshot.TotalAgents);
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    /// <summary>HKCU AppsUseLightTheme: 0 means dark; missing or unreadable means light (the Windows default).</summary>
    private static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }
}

/// <summary>
/// The one place WPF units meet pixels. System.Windows.Forms.Screen reports work areas in physical
/// pixels, which is what placement uses; content measured in DIPs is scaled by the window's DPI.
/// </summary>
internal static class ScreenUnits
{
    public static double Scale(Visual window)
    {
        var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        return scale > 0 ? scale : 1;
    }

    public static double ToPixels(double dips, double scale) => Math.Ceiling(dips * scale);

    public static RectD WorkArea(Screen screen)
    {
        var r = screen.WorkingArea;
        return new RectD(r.X, r.Y, r.Width, r.Height);
    }

    public static MonitorArea[] Monitors() =>
        Screen.AllScreens.Select(s => new MonitorArea(s.DeviceName, WorkArea(s))).ToArray();

    public static Screen Current(Window window) => Screen.FromHandle(new WindowInteropHelper(window).Handle);

    public static Screen Primary() => Screen.PrimaryScreen ?? Screen.AllScreens[0];
}
