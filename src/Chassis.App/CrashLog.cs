using System.Runtime.InteropServices;

namespace Chassis.App;

/// <summary>Writes startup/crash details to %LocalAppData%\Chassis\logs and tells the user where they are.</summary>
public static class CrashLog
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chassis", "logs");

    public static void Write(string stage, Exception? e, bool show)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, e is null ? "app.log" : $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var text = $"[{DateTime.Now:O}] {stage} · Chassis {typeof(CrashLog).Assembly.GetName().Version} · {Environment.OSVersion}\n{e}\n\n";
            File.AppendAllText(file, text);
            if (show && e is not null && Environment.GetEnvironmentVariable("CHASSIS_SMOKE") != "1")
                MessageBoxW(0, $"Chassis hit a problem ({stage}).\n\n{e.GetType().Name}: {e.Message}\n\nDetails were saved to:\n{file}", "Chassis", 0x10);
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
