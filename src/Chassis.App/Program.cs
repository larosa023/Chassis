using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Chassis.App;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("unhandled", e.ExceptionObject as Exception, show: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write("unobserved task", e.Exception, show: false); e.SetObserved(); };
        try
        {
            VelopackApp.Build().Run();
            // Keep the 3D view's browser data with Chassis's own data, not next to the installed exe.
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chassis", "WebView2"));
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(ctx);
                _ = new App();
            });
        }
        catch (Exception e)
        {
            CrashLog.Write("startup", e, show: true);
            Environment.Exit(1);
        }
    }
}
