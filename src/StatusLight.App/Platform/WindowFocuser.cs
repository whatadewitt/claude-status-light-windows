using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StatusLight.Core;

namespace StatusLight.App.Platform;

/// <summary>
/// Click-to-focus: finds a session's window from its tty and origin and brings it to the front.
/// <list type="bullet">
/// <item><c>claude-app:&lt;pid&gt;</c>: the first visible, unowned top-level window of that pid.</item>
/// <item><c>wt:*</c> or a WSL session: the topmost visible Windows Terminal window.</item>
/// <item><c>console:*</c> with a recorded hwnd: that window, if it still exists.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowFocuser
{
    private const string TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";
    private const string ClaudeAppPrefix = "claude-app:";

    /// <summary>
    /// Brings the session's terminal or app window to the front; false when there is none or
    /// Windows refused. Never throws.
    /// </summary>
    public static bool Focus(SessionState s)
    {
        try
        {
            var hwnd = FindTarget(s);
            return hwnd != 0 && Activate(hwnd);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static nint FindTarget(SessionState s)
    {
        if (s.Tty.StartsWith(ClaudeAppPrefix, StringComparison.Ordinal))
        {
            return uint.TryParse(s.Tty.AsSpan(ClaudeAppPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                ? TopLevelWindows().FirstOrDefault(h => IsMainWindowOf(h, pid))
                : 0;
        }
        if (s.Tty.StartsWith("wt:", StringComparison.Ordinal) || s.Origin == SessionOrigin.Wsl)
        {
            return TopLevelWindows().FirstOrDefault(h => Win32.IsWindowVisible(h) && ClassName(h) == TerminalWindowClass);
        }
        if (s.Tty.StartsWith("console:", StringComparison.Ordinal) && s.Hwnd != 0)
        {
            var hwnd = (nint)s.Hwnd;
            return Win32.IsWindow(hwnd) ? hwnd : 0;
        }
        return 0;
    }

    private static bool IsMainWindowOf(nint hwnd, uint pid)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var owner);
        return owner == pid && Win32.IsWindowVisible(hwnd) && Win32.GetWindow(hwnd, Win32.GW_OWNER) == 0;
    }

    /// <summary>
    /// Restores a minimised window, then asks for the foreground. A click from the no-activate
    /// floating window leaves this process without foreground rights, so when the plain request is
    /// refused it retries with this thread's input attached to the current foreground thread.
    /// </summary>
    private static bool Activate(nint hwnd)
    {
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        if (Win32.SetForegroundWindow(hwnd) && Win32.GetForegroundWindow() == hwnd) return true;

        var foreground = Win32.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : Win32.GetWindowThreadProcessId(foreground, out _);
        var thisThread = Win32.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread &&
            Win32.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            Win32.BringWindowToTop(hwnd);
            return Win32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    private static unsafe string ClassName(nint hwnd)
    {
        const int max = 256; // the documented maximum class-name length
        var buffer = stackalloc char[max];
        var length = Win32.GetClassName(hwnd, buffer, max);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    /// <summary>Every top-level window, topmost first (EnumWindows walks the z-order).</summary>
    private static unsafe List<nint> TopLevelWindows()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            Win32.EnumWindows(&CollectWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint state)
    {
        try
        {
            ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
            return 1;
        }
        catch (Exception)
        {
            return 0; // an exception must not cross the native frame; stop enumerating
        }
    }
}
