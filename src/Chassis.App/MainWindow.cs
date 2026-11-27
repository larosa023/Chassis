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

/// <summary>The window: side rail (navigation, current build, themes), custom title bar, update banner, and the four pages.</summary>
public sealed partial class MainWindow : Window
{
    readonly Garage _garage;
    readonly BuildSession _session;
    readonly UpdateViewModel _updates;
    readonly Grid _root = new();
    readonly Grid _host = new();
    readonly StackPanel _nav = new() { Spacing = 4 };
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

    static readonly Dictionary<string, (string Glyph, string Key)> NavInfo = new()
    {
        ["Build"] = ("\uE950", "Ctrl+1"),
        ["Benchmarks"] = ("\uE9D9", "Ctrl+2"),
        ["Garage"] = ("\uE8F1", "Ctrl+3"),
        ["Settings"] = ("\uE713", "Ctrl+4"),
    };

    readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly TextBlock _railBuild = Ui.Text("", 13, null, FontWeights.SemiBold, wrap: false);
    readonly TextBlock _railStats = Ui.Label("");

    UIElement Layout()
    {
        _root.Background = ThemeBrushes.Bg;
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Rail: full height on the left, behind the title strip.
        var rail = new Grid { Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(14, 48, 14, 16) };
        rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var mark = new Border { Width = 34, Height = 34, CornerRadius = Ui.R, Background = ThemeBrushes.Accent, Child = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/chassis.png")), Width = 34, Height = 34 } };
        var word = Ui.Column(0,
            new TextBlock { Text = "CHASSIS", FontFamily = Ui.DisplayBold, FontWeight = FontWeights.Bold, FontSize = 20, Foreground = ThemeBrushes.Text, CharacterSpacing = 80 },
            Ui.Label("PC bench · v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0")));
        word.VerticalAlignment = VerticalAlignment.Center;
        var brand = Ui.Row(10, mark, word);
        brand.Margin = new Thickness(4, 0, 0, 24);
        rail.Children.Add(brand);
        _nav.Orientation = Orientation.Vertical;
        _nav.Spacing = 4;
        rail.Children.Add(_nav.At(0, 1));

        var current = Ui.Column(4, Ui.Label("ON THE BENCH"), _railBuild, _railStats);
        var foot = Ui.Column(14, new Border { Height = 1, Background = ThemeBrushes.Line }, current, Ui.Label("THEME"), _swatches);
        rail.Children.Add(foot.At(0, 3));
        Grid.SetRowSpan(rail, 2);
        _root.Children.Add(rail);

        // Title strip over the content column (Windows draws the caption buttons on the right).
        var titleStrip = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetColumn(titleStrip, 1);
        _root.Children.Add(titleStrip);
        SetTitleBar(titleStrip);

        // Content: update banner + pages.
        var content = new Grid { Padding = new Thickness(26, 4, 26, 22), RowSpacing = 12 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var ub = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        ub.Children.Add(new FontIcon { Glyph = "\uE896", Foreground = ThemeBrushes.Accent, FontSize = 18 }.At(0));
        ub.Children.Add(Ui.Column(2, _updateTitle, _updateNotes).At(1));
        var later = Ui.Ghost("Later", () => _updates.DismissCommand.Execute(null));
        var buttons = Ui.Row(8, _updateProgress, later, _updateButton);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        ub.Children.Add(buttons.At(2));
        _updateBar.Child = ub;
        _updateBar.Background = ThemeBrushes.AccentSoft;
        _updateBar.BorderBrush = ThemeBrushes.Accent;
        _updateBar.BorderThickness = new Thickness(3, 1, 1, 1);
        _updateBar.CornerRadius = Ui.R;
        _updateBar.Padding = new Thickness(14, 8, 10, 8);
        content.Children.Add(_updateBar);
        Grid.SetRow(_host, 1);
        content.Children.Add(_host);
        Grid.SetColumn(content, 1);
        Grid.SetRow(content, 1);
        _root.Children.Add(content);

        _session.BuildChanged += RenderRail;
        _session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BuildSession.Build)) RenderRail(); };
        RenderRail();
        return _root;
    }

    void RenderRail()
    {
        _railBuild.Text = _session.Build.Name;
        var b = _session.Bench;
        _railStats.Text = b.Score > 0 ? $"{_session.TotalText} · {b.Score:N0} pts" : $"{_session.TotalText} · {_session.Resolved.Count}/8 parts";
    }

    void BuildSwatches()
    {
        _swatches.Children.Clear();
        foreach (var t in Themes.All)
        {
            var on = t.Key == ThemeBrushes.Current.Key;
            var chip = new Grid { Width = 18, Height = 18, CornerRadius = Ui.RSmall, Background = Ui.Hex(t.Bg) };
            chip.Children.Add(new Border { Width = 9, Height = 9, Background = Ui.Hex(t.Accent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
            var b = new Button
            {
                Content = chip, Padding = new Thickness(3), CornerRadius = Ui.RSmall, MinWidth = 0, MinHeight = 0,
                Background = new SolidColorBrush(Colors.Transparent), BorderBrush = on ? ThemeBrushes.Accent : ThemeBrushes.Line, BorderThickness = new Thickness(on ? 2 : 1),
            };
            ToolTipService.SetToolTip(b, t.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"{t.Name} theme");
            var key = t.Key;
            b.Click += (_, _) => { _garage.Update(s => s with { Theme = key }); ThemeBrushes.Apply(Themes.Get(key)); };
            _swatches.Children.Add(b);
        }
    }

    void AddNav(string name)
    {
        var (glyph, key) = NavInfo[name];
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 6, 0, 6) };
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(bar.At(0));
        g.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center }.At(1));
        g.Children.Add(new TextBlock { Text = name, FontFamily = Ui.Body, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }.At(2));
        var k = Ui.Label(key);
        k.VerticalAlignment = VerticalAlignment.Center;
        k.Opacity = 0.7;
        g.Children.Add(k.At(3));
        var b = new Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 8, 10, 8), CornerRadius = Ui.R, BorderThickness = new Thickness(0), MinHeight = 42, Tag = bar,
        };
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
            b.Background = on ? ThemeBrushes.Surface2 : new SolidColorBrush(Colors.Transparent);
            b.Foreground = on ? ThemeBrushes.Text : ThemeBrushes.Muted;
            if (b.Tag is Border bar) bar.Background = on ? ThemeBrushes.Accent : new SolidColorBrush(Colors.Transparent);
        }
    }

    void OnTheme()
    {
        _root.RequestedTheme = ThemeBrushes.ElementTheme;
        UpdateNav();
        BuildSwatches();
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
        foreach (var t in new[] { "circuit", "aluminum", "synth" })
        {
            var k = t;
            steps.Add(new($"{k}-build", () => { ThemeBrushes.Apply(Themes.Get(k)); _session.Open(_garage.Find("preset-sff")!); Navigate("Build"); }, 3500));
        }
        return steps;
    }
}
