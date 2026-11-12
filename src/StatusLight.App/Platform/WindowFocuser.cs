using StatusLight.Core;

namespace StatusLight.App.Platform;

public static class WindowFocuser
{
    /// <summary>Brings the session's terminal or app window to the front; false when there is none.</summary>
    public static bool Focus(SessionState s) => false;
}
