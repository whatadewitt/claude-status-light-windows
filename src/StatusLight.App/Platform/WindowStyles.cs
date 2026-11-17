namespace StatusLight.App.Platform;

/// <summary>Extended window styles for the floating window: it never takes focus or shows in Alt-Tab.</summary>
public static class WindowStyles
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public static int FloatingExStyle(int existing) =>
        existing | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;

    /// <summary>Adds <see cref="FloatingExStyle"/> to the window's current extended style.</summary>
    public static void ApplyFloating(nint hwnd) =>
        Win32.SetWindowLong(hwnd, GWL_EXSTYLE, FloatingExStyle(Win32.GetWindowLong(hwnd, GWL_EXSTYLE)));
}
