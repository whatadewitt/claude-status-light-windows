using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace StatusLight.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Stub: Task 9 builds the tray host.
        Shutdown();
    }
}
