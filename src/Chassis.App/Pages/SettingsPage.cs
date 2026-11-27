using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.App.ViewModels.Theming;
using Chassis.Core.Catalog;
using Chassis.Core.Storage;

namespace Chassis.App.Pages;

/// <summary>Themes, 3D options, updates and about.</summary>
public sealed partial class SettingsPage : UserControl
{
    readonly Garage _garage;
    readonly UpdateViewModel _updates;
    readonly StackPanel _themes = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    readonly ToggleSwitch _rgb = new() { Header = "RGB lighting in the 3D view" };
    readonly ToggleSwitch _spin = new() { Header = "Slowly turn the build on its stand" };
    readonly ToggleSwitch _auto = new() { Header = "Check for updates automatically" };
    readonly TextBlock _status = Ui.Muted("");
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 16, 0) };

    public ScrollViewer Scroller => _scroll;
    public event Action<bool>? RgbChanged;
    public event Action<bool>? SpinChanged;

    public SettingsPage(Garage garage, UpdateViewModel updates)
    {
        _garage = garage;
        _updates = updates;
        _rgb.IsOn = garage.Settings.RgbLighting;
        _spin.IsOn = garage.Settings.AutoRotate;
        _auto.IsOn = garage.Settings.CheckForUpdates;
        _rgb.Toggled += (_, _) => { garage.Update(s => s with { RgbLighting = _rgb.IsOn }); RgbChanged?.Invoke(_rgb.IsOn); };
        _spin.Toggled += (_, _) => { garage.Update(s => s with { AutoRotate = _spin.IsOn }); SpinChanged?.Invoke(_spin.IsOn); };
        _auto.Toggled += (_, _) => garage.Update(s => s with { CheckForUpdates = _auto.IsOn });
        _updates.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UpdateViewModel.Status)) _status.Text = _updates.Status; };

        var version = typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var check = Ui.Ghost("Check now", () => _ = _updates.CheckAsync(manual: true), "\uE895");
        var github = new HyperlinkButton { Content = "Chassis on GitHub", NavigateUri = new Uri("https://github.com/larosa023/Chassis"), Padding = new Thickness(0) };
        var logs = Ui.Ghost("Open data folder", () =>
        {
            var dir = Path.GetDirectoryName(Garage.DefaultPath)!;
            Directory.CreateDirectory(dir);
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }); } catch (Exception) { }
        }, "\uE838");

        var body = Ui.Column(16,
            Ui.Title("Settings", 32),
            Ui.Card(Ui.Column(14, Ui.Text("Theme", 16, null, FontWeights.SemiBold), _themes), 22),
            Ui.Card(Ui.Column(10, Ui.Text("3D view", 16, null, FontWeights.SemiBold), _rgb, _spin,
                Ui.Muted("Drag to turn the build, scroll to zoom, right-drag to move, double-click to reset. Click any part to swap it.", 13)), 22),
            Ui.Card(Ui.Column(10, Ui.Text("Updates", 16, null, FontWeights.SemiBold),
                Ui.Text($"You have Chassis {version}.", 14),
                _auto, Ui.Row(12, check, _status),
                Ui.Muted("Updates come from GitHub Releases and install with one click when you're ready. Chassis checks when it opens and every six hours.", 13)), 22),
            Ui.Card(Ui.Column(10, Ui.Text("About", 16, null, FontWeights.SemiBold),
                Ui.Text("Benchmarks in Chassis are estimates. They combine published review results for each part with how games and apps use them, so they're a good guide but not a measurement of your exact PC.", 14),
                Ui.Muted($"{PartCatalog.Default.Parts.Count} parts in the catalog. {PartCatalog.Default.Notes}", 13),
                Ui.Muted("Builds and settings are saved on this PC only. Chassis sends nothing about you anywhere; the only thing it checks online is whether there's an update.", 13),
                Ui.Row(12, logs, github)), 22));
        body.MaxWidth = 860;
        body.HorizontalAlignment = HorizontalAlignment.Left;
        _scroll.Content = body;
        Content = _scroll;
        BuildThemes();
        ThemeBrushes.Changed += BuildThemes;
    }

    public void SyncToggles(bool rgb, bool spin) { _rgb.IsOn = rgb; _spin.IsOn = spin; }

    void BuildThemes()
    {
        _themes.Children.Clear();
        foreach (var t in Themes.All)
        {
            var on = t.Key == ThemeBrushes.Current.Key;
            var preview = new Grid { Width = 132, Height = 84, CornerRadius = new CornerRadius(14), Background = Ui.Hex(t.Bg) };
            preview.Children.Add(new Border { Background = Ui.Hex(t.Surface), CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 10, 46, 10) });
            preview.Children.Add(new Border { Background = Ui.Hex(t.Accent), CornerRadius = new CornerRadius(6), Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10) });
            preview.Children.Add(new Border { Background = Ui.Hex(t.Text), CornerRadius = new CornerRadius(3), Width = 46, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 20, 0, 0) });
            preview.Children.Add(new Border { Background = Ui.Hex(t.Muted), CornerRadius = new CornerRadius(3), Width = 34, Height = 5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 32, 0, 0) });
            var b = new Button
            {
                Content = Ui.Column(8, preview, Ui.Text(t.Name, 13, null, on ? FontWeights.SemiBold : FontWeights.Normal)),
                Padding = new Thickness(6), CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = on ? ThemeBrushes.Accent : ThemeBrushes.Line, BorderThickness = new Thickness(on ? 2.5 : 1),
            };
            AutomationProperties.SetName(b, $"{t.Name} theme");
            var key = t.Key;
            b.Click += (_, _) => { _garage.Update(s => s with { Theme = key }); ThemeBrushes.Apply(Themes.Get(key)); };
            _themes.Children.Add(b);
        }
    }
}
