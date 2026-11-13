using System.Runtime.Versioning;
using StatusLight.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using Window = System.Windows.Window;

namespace StatusLight.App.Settings;

/// <summary>
/// The preferences window. Every change is saved immediately through <see cref="SettingsStore"/>;
/// the floating window, monitor and start-at-login registration react to its Changed event.
/// Saves always start from the latest settings so a toggle never undoes a concurrent change (a
/// drag's saved position, the tray's floating-window toggle), and those changes are reflected here.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private static readonly (ScreenCorner Corner, string Label)[] Corners =
    [
        (ScreenCorner.TopLeft, "Top left"),
        (ScreenCorner.TopRight, "Top right"),
        (ScreenCorner.BottomLeft, "Bottom left"),
        (ScreenCorner.BottomRight, "Bottom right"),
    ];

    private readonly AppHost _host;
    private readonly SettingsStore _store;
    /// <summary>True while controls are set from settings, so their change events don't save.</summary>
    private bool _refreshing;

    public SettingsWindow(AppHost host, SettingsStore store, Action revealIconFolder)
    {
        _host = host;
        _store = store;
        // Follow the Windows light/dark app theme (Fluent). Only this window: the floating window themes itself.
#pragma warning disable WPF0001 // ThemeMode is marked experimental
        ThemeMode = System.Windows.ThemeMode.System;
#pragma warning restore WPF0001
        InitializeComponent();

        foreach (var (_, label) in Corners) CornerBox.Items.Add(label);
        Refresh(host.Settings);

        // Click (not Checked/Unchecked) fires only for the user, never for Refresh.
        ShowFloatingBox.Click += (_, _) => Save(s => s with { ShowFloatingWindow = IsOn(ShowFloatingBox) });
        LockToCornerBox.Click += (_, _) => Save(s => s with { LockToCorner = IsOn(LockToCornerBox) });
        AutoHideBox.Click += (_, _) => Save(s => s with { AutoHide = IsOn(AutoHideBox) });
        GreenBeatsYellowBox.Click += (_, _) => Save(s => s with { GreenBeatsYellow = IsOn(GreenBeatsYellowBox) });
        StartAtLoginBox.Click += (_, _) => Save(s => s with { StartAtLogin = IsOn(StartAtLoginBox) });
        CornerBox.SelectionChanged += (_, _) =>
        {
            if (_refreshing || CornerBox.SelectedIndex < 0) return;
            Save(s => s with { Corner = Corners[CornerBox.SelectedIndex].Corner });
        };
        RevealIconButton.Click += (_, _) => revealIconFolder();

        _store.Changed += OnSettingsChanged;
        Closed += (_, _) => _store.Changed -= OnSettingsChanged;
    }

    private static bool IsOn(CheckBox box) => box.IsChecked == true;

    private void Save(Func<AppSettings, AppSettings> change) => _store.Save(change(_host.Settings));

    private void OnSettingsChanged(AppSettings s)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnSettingsChanged(s));
            return;
        }
        Refresh(s);
    }

    private void Refresh(AppSettings s)
    {
        _refreshing = true;
        try
        {
            ShowFloatingBox.IsChecked = s.ShowFloatingWindow;
            LockToCornerBox.IsChecked = s.LockToCorner;
            CornerBox.SelectedIndex = Array.FindIndex(Corners, c => c.Corner == s.Corner);
            CornerBox.IsEnabled = s.LockToCorner;
            AutoHideBox.IsChecked = s.AutoHide;
            GreenBeatsYellowBox.IsChecked = s.GreenBeatsYellow;
            StartAtLoginBox.IsChecked = s.StartAtLogin;
        }
        finally
        {
            _refreshing = false;
        }
    }
}
