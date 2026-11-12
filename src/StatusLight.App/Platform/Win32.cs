using System.Runtime.InteropServices;

namespace StatusLight.App.Platform;

public static partial class Win32
{
    public const uint GR_GDIOBJECTS = 0;
    public const uint GR_USEROBJECTS = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    public static partial uint GetGuiResources(nint hProcess, uint uiFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);
}
