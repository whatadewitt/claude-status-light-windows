using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using StatusLight.App.Platform;
using StatusLight.App.Rendering;
using StatusLight.App.Settings;
using StatusLight.Core;
using StatusLight.Core.Sources;

namespace StatusLight.App.Tray;

/// <summary>The notification-area icon and its menu, both kept current from <see cref="AppHost"/>.</summary>
public sealed class TrayController : IDisposable
{
    private const int MaxTooltip = 127;
    private const int DotImageSize = 16;

    // NotifyIcon only opens its ContextMenuStrip on right click. Its internal ShowContextMenu also
    // takes foreground first, which is what makes the menu dismiss on an outside click.
    private static readonly MethodInfo? ShowContextMenuMethod =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly AppHost _host;
    private readonly SettingsStore _store;
    private readonly IconCache _icons;
    private readonly Action _quit;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly Dictionary<LightState, Bitmap> _dots = new();
    private SettingsWindow? _settingsWindow;

    public TrayController(AppHost host, SettingsStore store, IconCache icons, Action quit)
    {
        _host = host;
        _store = store;
        _icons = icons;
        _quit = quit;

        _menu = new ContextMenuStrip { ShowItemToolTips = true };
        _menu.Opening += (_, e) =>
        {
            RebuildMenu();
            e.Cancel = false; // an empty strip opens pre-cancelled
        };
        _notify = new NotifyIcon { ContextMenuStrip = _menu };
        _notify.MouseUp += OnMouseUp;

        _host.Updated += OnUpdated;
        OnUpdated(_host.Current);
        _notify.Visible = true;
    }

    public void Dispose()
    {
        _host.Updated -= OnUpdated;
        _settingsWindow?.Close();
        _notify.Visible = false;
        _notify.Dispose();
        ClearMenu();
        _menu.Dispose();
        foreach (var dot in _dots.Values) dot.Dispose();
        _dots.Clear();
    }

    private void OnUpdated(MonitorSnapshot snapshot)
    {
        _notify.Icon = _icons.Get(
            snapshot.Aggregate, SystemInformation.SmallIconSize.Width, _host.DanceFrame, snapshot.TotalAgents);
        var tip = "Claude · " + LightStates.Label(snapshot.Aggregate);
        _notify.Text = tip.Length > MaxTooltip ? tip[..MaxTooltip] : tip;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return; // right click is handled by NotifyIcon itself
        if (ShowContextMenuMethod is not null)
        {
            ShowContextMenuMethod.Invoke(_notify, null);
            return;
        }
        Win32.SetForegroundWindow(_menu.Handle);
        _menu.Show(Cursor.Position);
    }

    private void RebuildMenu()
    {
        ClearMenu();
        var snapshot = _host.Current;
        var now = DateTimeOffset.UtcNow;

        _menu.Items.Add(new ToolStripMenuItem(snapshot.Sessions.Count > 0 ? "Sessions" : "No active sessions")
        {
            Enabled = false,
        });

        foreach (var session in snapshot.Sessions)
        {
            var tag = SessionText.TagText(session.Tag);
            var text = SessionText.MenuRow(session) + (tag.Length > 0 ? $"  [{tag}]" : "");
            var item = new ToolStripMenuItem(text.Replace("&", "&&"), Dot(session.State))
            {
                ToolTipText = SessionText.Tooltip(session, now),
                Enabled = !session.IsBackground,
            };
            item.Click += (_, _) => WindowFocuser.Focus(session);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Show floating window", null, (_, _) => ToggleFloatingWindow())
        {
            Checked = _host.Settings.ShowFloatingWindow,
        });
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        _menu.Items.Add(new ToolStripMenuItem("Set custom icon…", null, (_, _) => RevealIconFolder()));
        _menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => _quit()));
    }

    private void ClearMenu()
    {
        var old = _menu.Items.Cast<ToolStripItem>().ToList();
        _menu.Items.Clear();
        foreach (var item in old)
        {
            item.Image = null; // dot bitmaps are shared and owned by this controller
            item.Dispose();
        }
    }

    private void ToggleFloatingWindow()
    {
        var settings = _host.Settings;
        _store.Save(settings with { ShowFloatingWindow = !settings.ShowFloatingWindow });
    }

    /// <summary>Shows the one settings window, creating it on first use and bringing it forward after.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            var window = new SettingsWindow(_host, _store, RevealIconFolder);
            window.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow = window;
        }
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            _settingsWindow.WindowState = System.Windows.WindowState.Normal;
        }
        _settingsWindow.Activate();
    }

    /// <summary>Opens %USERPROFILE%\.claude\status-light so a custom icon.png can be dropped in.</summary>
    private static void RevealIconFolder()
    {
        try
        {
            var dir = Path.GetDirectoryName(IconCache.DefaultCustomIconPath)!;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // Nothing useful to report from a tray menu click.
        }
    }

    private Bitmap Dot(LightState state)
    {
        if (_dots.TryGetValue(state, out var cached)) return cached;
        var bmp = new Bitmap(DotImageSize, DotImageSize);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(IconRenderer.StateColor(state));
            g.FillEllipse(brush, 3, 3, DotImageSize - 6, DotImageSize - 6);
        }
        _dots[state] = bmp;
        return bmp;
    }
}
