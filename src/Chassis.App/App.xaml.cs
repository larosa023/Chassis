using Microsoft.UI.Xaml;
using Chassis.App.Theming;

namespace Chassis.App;

public partial class App : Application
{
    MainWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => { CrashLog.Write("xaml", e.Exception, show: true); e.Handled = true; };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ThemeBrushes.Register(Resources);   // Application.Resources isn't available in the constructor
            _window = new MainWindow();
            _window.Activate();
            CrashLog.Write("started", null, show: false);
        }
        catch (Exception e)
        {
            CrashLog.Write("window", e, show: true);
            Exit();
        }
    }
}
