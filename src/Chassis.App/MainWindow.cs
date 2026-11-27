using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Chassis.App.Pages;
using Chassis.App.Services;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.App.ViewModels.Theming;
using Chassis.Core.Catalog;
using Chassis.Core.Storage;
using Windows.System;

namespace Chassis.App;

/// <summary>The window: custom title bar, pill navigation, update banner, and the four pages.</summary>
public sealed partial class MainWindow : Window
{
    readonly Garage _garage;
    readonly BuildSession _session;
    readonly UpdateViewModel _updates;
    readonly Grid _root = new();
    readonly Grid _host = new();
    readonly StackPanel _nav = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly Dictionary<string, FrameworkElement> _pages = new();
    readonly Dictionary<string, Button> _navButtons = new();
    readonly Border _updateBar = new();
    readonly TextBlock _updateTitle = Ui.Text("", 14, ThemeBrushes.Text, FontWeights.SemiBold);
    readonly TextBlock _updateNotes = Ui.Muted("", 12);
    readonly Button _updateButton;
    readonly ProgressBar _updateProgress = new() { Width = 160, Maximum = 100, Visibility = Visibility.Collapsed };
    readonly BuildPage _build;
    readonly BenchPage _bench;
    readonly GaragePage _garagePage;
    readonly SettingsPage _settings;
    string _current = "Build";

    public MainWindow()
    {
        _garage = new Garage(Garage.DefaultPath);
        ThemeBrushes.Apply(Themes.Get(_garage.Settings.Theme));
        _session = new BuildSession(PartCatalog.Default, _garage);
        var dq = DispatcherQueue;
        _updates = new UpdateViewModel(new VelopackUpdateSource(), a => { if (!dq.TryEnqueue(() => a())) a(); });
        _updateButton = Ui.Accent("Update and restart", () => _updates.UpdateCommand.Execute(null));

        Title = "Chassis";
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "chassis.ico"));
        SizeWindow();

        _build = new BuildPage(_session);
        _bench = new BenchPage(_session);
        _garagePage = new GaragePage(_session);
        _settings = new SettingsPage(_garage, _updates);
        _pages["Build"] = _build;
        _pages["Benchmarks"] = _bench;
        _pages["Garage"] = _garagePage;
        _pages["Settings"] = _settings;
        _build.BenchRequested += () => { Navigate("Benchmarks"); _bench.Run(); };
        _garagePage.OpenRequested += () => Navigate("Build");
        _settings.RgbChanged += on => _build.SetRgb(on);
        _settings.SpinChanged += on => _build.SetSpin(on);

        Content = Layout();
        foreach (var (name, page) in _pages) { _host.Children.Add(page); AddNav(name); }
        Navigate("Build");
        Accelerators();

        ThemeBrushes.Changed += OnTheme;
        OnTheme();
        _updates.PropertyChanged += (_, _) => RenderUpdateBar();
        RenderUpdateBar();

        if (Environment.GetEnvironmentVariable("CHASSIS_TOUR") is { Length: > 0 } tour)
            DispatcherQueue.TryEnqueue(async () => await ScreenTour.RunAsync(tour, TourSteps));
        _ = StartUpdatesAsync();
    }

    void SizeWindow()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min(1560, area.Width - 40), h = Math.Min(980, area.Height - 40);
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = 1100;
            op.PreferredMinimumHeight = 720;
            if (area.Width < 1240 || area.Height < 800 || Environment.GetEnvironmentVariable("CHASSIS_TOUR") is { Length: > 0 }) { op.Maximize(); return; }
        }
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    UIElement Layout()
    {
        _root.Background = ThemeBrushes.Bg;
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Row 0: draggable title strip (Windows draws the caption buttons on the right).
        var titleStrip = new Grid { Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(18, 0, 150, 0) };
        titleStrip.Children.Add(Ui.Row(10,
            new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/chassis.png")), Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = "Chassis", FontFamily = Ui.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = ThemeBrushes.Muted, VerticalAlignment = VerticalAlignment.Center }));
        _root.Children.Add(titleStrip);
        SetTitleBar(titleStrip);

        // Row 1: navigation pills.
        var header = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        header.Padding = new Thickness(28, 4, 28, 12);
        var brand = Ui.Row(10, new TextBlock { Text = "Chassis", FontFamily = Ui.Display, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = ThemeBrushes.Text, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(brand.At(0));
        var navPill = new Border
        {
            Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
            Padding = new Thickness(5), Margin = new Thickness(24, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = _nav,
        };
        header.Children.Add(navPill.At(1));
        var catalogNote = Ui.Muted($"{PartCatalog.Default.Parts.Count} parts", 12);
        catalogNote.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(catalogNote.At(2));
        Grid.SetRow(header, 1);
        _root.Children.Add(header);

        // Row 2: update banner.
        var ub = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto);
        ub.Children.Add(new FontIcon { Glyph = "\uE896", Foreground = ThemeBrushes.Accent, FontSize = 20 }.At(0));
        ub.Children.Add(Ui.Column(2, _updateTitle, _updateNotes).At(1));
        var later = Ui.Ghost("Later", () => _updates.DismissCommand.Execute(null));
        var buttons = Ui.Row(8, _updateProgress, later, _updateButton);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        ub.Children.Add(buttons.At(3));
        _updateBar.Child = ub;
        _updateBar.Background = ThemeBrushes.AccentSoft;
        _updateBar.BorderBrush = ThemeBrushes.Accent;
        _updateBar.BorderThickness = new Thickness(1.5);
        _updateBar.CornerRadius = new CornerRadius(16);
        _updateBar.Padding = new Thickness(16, 10, 12, 10);
        _updateBar.Margin = new Thickness(28, 0, 28, 12);
        Grid.SetRow(_updateBar, 2);
        _root.Children.Add(_updateBar);

        // Row 3: pages.
        _host.Margin = new Thickness(28, 0, 28, 24);
        Grid.SetRow(_host, 3);
        _root.Children.Add(_host);
        return _root;
    }

    void AddNav(string name)
    {
        var b = new Button { Content = name, Padding = new Thickness(16, 8, 16, 8), CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(0), MinHeight = 36 };
        b.Click += (_, _) => Navigate(name);
        _navButtons[name] = b;
        _nav.Children.Add(b);
    }

    void Navigate(string name)
    {
        _current = name;
        foreach (var (n, p) in _pages) p.Visibility = n == name ? Visibility.Visible : Visibility.Collapsed;
        UpdateNav();
    }

    void UpdateNav()
    {
        foreach (var (name, b) in _navButtons)
        {
            var on = name == _current;
            b.Background = on ? ThemeBrushes.Text : new SolidColorBrush(Colors.Transparent);
            b.Foreground = on ? ThemeBrushes.Bg : ThemeBrushes.Muted;
            b.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    void OnTheme()
    {
        _root.RequestedTheme = ThemeBrushes.ElementTheme;
        UpdateNav();
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = ThemeBrushes.Text.Color;
        tb.ButtonInactiveForegroundColor = ThemeBrushes.Muted.Color;
        tb.ButtonHoverBackgroundColor = ThemeBrushes.Surface2.Color;
        tb.ButtonHoverForegroundColor = ThemeBrushes.Text.Color;
        tb.ButtonPressedBackgroundColor = ThemeBrushes.Line.Color;
        tb.ButtonPressedForegroundColor = ThemeBrushes.Text.Color;
    }

    void RenderUpdateBar()
    {
        _updateBar.Visibility = _updates.Available ? Visibility.Visible : Visibility.Collapsed;
        _updateTitle.Text = _updates.Title;
        _updateNotes.Text = _updates.Notes.Length > 0 ? _updates.Notes.Split('\n')[0].TrimStart('#', ' ') : "Takes a few seconds. Your builds are kept.";
        _updateProgress.Visibility = _updates.Downloading ? Visibility.Visible : Visibility.Collapsed;
        _updateProgress.Value = _updates.Progress;
        _updateButton.IsEnabled = !_updates.Downloading;
    }

    void Accelerators()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action a)
        {
            var k = new KeyboardAccelerator { Key = key, Modifiers = mods };
            k.Invoked += (_, e) =>
            {
                // Let text boxes keep their own undo.
                if (FocusManager.GetFocusedElement(_root.XamlRoot) is TextBox && key is VirtualKey.Z or VirtualKey.Y) return;
                e.Handled = true;
                a();
            };
            _root.KeyboardAccelerators.Add(k);
        }
        _root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Add(VirtualKey.Z, VirtualKeyModifiers.Control, _session.Undo);
        Add(VirtualKey.Y, VirtualKeyModifiers.Control, _session.Redo);
        Add(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, _session.Redo);
        Add(VirtualKey.Number1, VirtualKeyModifiers.Control, () => Navigate("Build"));
        Add(VirtualKey.Number2, VirtualKeyModifiers.Control, () => Navigate("Benchmarks"));
        Add(VirtualKey.Number3, VirtualKeyModifiers.Control, () => Navigate("Garage"));
        Add(VirtualKey.Number4, VirtualKeyModifiers.Control, () => Navigate("Settings"));
        Add(VirtualKey.N, VirtualKeyModifiers.Control, () => { _session.NewBuild(); Navigate("Build"); });
    }

    async Task StartUpdatesAsync()
    {
        if (!_garage.Settings.CheckForUpdates || Environment.GetEnvironmentVariable("CHASSIS_TOUR") is { Length: > 0 }) return;
        await Task.Delay(3000);
        await _updates.CheckAsync();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromHours(6);
        timer.Tick += async (_, _) => { if (_garage.Settings.CheckForUpdates) await _updates.CheckAsync(); };
        timer.Start();
    }

    IReadOnlyList<ScreenTour.Step> TourSteps()
    {
        var steps = new List<ScreenTour.Step>();
        foreach (var theme in new[] { "graphite", "bench" })
        {
            var t = theme;
            steps.Add(new($"{t}-build", () => { ThemeBrushes.Apply(Themes.Get(t)); _session.Open(_garage.Find("preset-flagship")!); _session.Selected = Category.Gpu; _build.SetExploded(false); Navigate("Build"); }, 4000));
            if (t == "graphite")
            {
                steps.Add(new($"{t}-exploded", () => _build.SetExploded(true), 3000));
                steps.Add(new($"{t}-problem", () => { _build.SetExploded(false); _session.Open(_garage.Find("preset-sff")!); _session.Choose("nv-5090"); _session.Choose("noctua-nh-d15-g2"); _session.Selected = Category.Cooler; }, 4000));
                steps.Add(new($"{t}-empty", () => _session.NewBuild("Tour build"), 3000));
            }
            steps.Add(new($"{t}-bench", () => { _session.Open(_garage.Find("preset-1440")!); Navigate("Benchmarks"); _bench.Scroller.ChangeView(null, 0, null, true); }, 2000));
            steps.Add(new($"{t}-bench-bottom", () => _bench.Scroller.ChangeView(null, _bench.Scroller.ScrollableHeight, null, true), 1200));
            steps.Add(new($"{t}-garage", () => { Navigate("Garage"); _garagePage.Scroller.ChangeView(null, 0, null, true); }, 1500));
            steps.Add(new($"{t}-garage-bottom", () => _garagePage.Scroller.ChangeView(null, _garagePage.Scroller.ScrollableHeight, null, true), 1200));
            steps.Add(new($"{t}-settings", () => Navigate("Settings"), 1500));
        }
        foreach (var t in new[] { "circuit", "frost", "synth" })
        {
            var k = t;
            steps.Add(new($"{k}-build", () => { ThemeBrushes.Apply(Themes.Get(k)); _session.Open(_garage.Find("preset-sff")!); Navigate("Build"); }, 3500));
        }
        return steps;
    }
}
