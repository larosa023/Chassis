namespace Chassis.App.Services;

/// <summary>
/// CI only (CHASSIS_TOUR=&lt;folder&gt;): shows every page in a dark and a light theme. For each screen it writes
/// ready-NAME.txt and waits for the CI script to take a screenshot and write shot-NAME.txt. The screenshot is taken
/// from the screen (not rendered by the app) so the 3D view, which draws outside XAML, is captured too.
/// </summary>
public static class ScreenTour
{
    public sealed record Step(string Name, Action Show, int SettleMs = 2500);

    public static async Task RunAsync(string folder, Func<IReadOnlyList<Step>> steps)
    {
        Directory.CreateDirectory(folder);
        await Task.Delay(6000);   // let WebView2 start and the scene load
        foreach (var s in steps())
        {
            try
            {
                s.Show();
                await Task.Delay(s.SettleMs);
                File.WriteAllText(Path.Combine(folder, $"ready-{s.Name}.txt"), "ok");
                var shot = Path.Combine(folder, $"shot-{s.Name}.txt");
                for (var i = 0; i < 100 && !File.Exists(shot); i++) await Task.Delay(150);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, $"{s.Name}.error.txt"), e.ToString()); }
        }
        File.WriteAllText(Path.Combine(folder, "done.txt"), "ok");
    }
}
