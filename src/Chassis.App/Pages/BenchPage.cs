using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.Core.Bench;

namespace Chassis.App.Pages;

/// <summary>Estimated benchmarks for the current build: score, frame rates, balance, and the numbers behind them.</summary>
public sealed partial class BenchPage : UserControl
{
    readonly BuildSession _s;
    readonly StackPanel _body = new() { Spacing = 16, MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 16, 0) };
    Resolution _res = Resolution.P1440;
    bool _running;
    double _reveal = 1;               // 0..1 while the test animation runs
    string _stage = "";

    public ScrollViewer Scroller => _scroll;

    static readonly (string Label, int Ms)[] Stages =
    [
        ("Warming up", 450), ("Processor: all cores", 900), ("Processor: one core", 600),
        ("Graphics: competitive shooter", 500), ("Graphics: battle royale", 500), ("Graphics: open-world AAA", 600), ("Graphics: ray tracing", 650),
        ("Memory and storage", 600), ("Power and temperatures", 500),
    ];

    public BenchPage(BuildSession session)
    {
        _s = session;
        _scroll.Content = _body;
        Content = _scroll;
        _s.BuildChanged += () => { if (!_running) Render(); };
        Render();
    }

    public async void Run()
    {
        if (_running) return;
        _running = true;
        try
        {
            var total = Stages.Sum(x => x.Ms);
            var done = 0;
            foreach (var (label, ms) in Stages)
            {
                _stage = label;
                var steps = Math.Max(1, ms / 60);
                for (var i = 0; i < steps; i++)
                {
                    _reveal = Math.Min(0.999, (done + (double)ms * i / steps) / total);
                    Render();
                    await Task.Delay(60);
                }
                done += ms;
            }
        }
        finally
        {
            _running = false;
            _reveal = 1;
            _stage = "Done. These are estimates from published reviews.";
            Render();
        }
    }

    static double Ease(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    double V(double x) => x * Ease(_reveal);

    void Render()
    {
        var b = _s.Bench;
        _body.Children.Clear();
        _body.Children.Add(Hero(b));
        if (_s.Report.Problems > 0)
            _body.Children.Add(Banner($"This build has {_s.Report.Problems} problem{(_s.Report.Problems == 1 ? "" : "s")} that would stop it from going together. The numbers assume it's fixed."));
        if (b.Score == 0)
        {
            _body.Children.Add(Ui.Card(Ui.Column(8, Ui.Title("Add a processor and graphics card", 22),
                Ui.Muted("Benchmarks need at least a CPU and GPU. Head back to Build and pick them."))));
            return;
        }
        _body.Children.Add(Gaming(b));
        _body.Children.Add(Balance(b));
        _body.Children.Add(Stats(b));
        _body.Children.Add(Ui.Muted("How this works: Chassis doesn't run your parts. It combines published review results for each part with how games and apps use the CPU, GPU, memory and storage. Real results vary with drivers, settings and the specific game. Treat these as a good estimate, within about 10 to 15%.", 12));
    }

    UIElement Hero(BenchResult b)
    {
        var g = Ui.Columns(Ui.Star(), Ui.Auto);
        var score = new TextBlock
        {
            Text = Format.Int(V(b.Score)), FontFamily = Ui.MonoBold, FontSize = 68, FontWeight = FontWeights.Bold,
            Foreground = ThemeBrushes.Accent, LineHeight = 74,
        };
        var pills = Ui.Row(8, Ui.Pill(b.Tier, ThemeBrushes.AccentSoft, ThemeBrushes.Accent, 13), Ui.Pill(Format.Money(b.Price), ThemeBrushes.Surface2, ThemeBrushes.Text, 13));
        g.Children.Add(Ui.Column(6, Ui.Label($"CHASSIS SCORE · {_s.Build.Name.ToUpperInvariant()}"), score, pills,
            Ui.Text(_reveal < 1 ? "Testing…" : _s.Bench.Verdict, 15)).At(0));
        var run = Ui.Accent(_running ? "Testing…" : "Run benchmarks", Run);
        run.IsEnabled = !_running;
        run.MinWidth = 220;
        var bar = new ProgressBar { Value = _reveal * 100, Maximum = 100, Width = 260, Visibility = _running ? Visibility.Visible : Visibility.Collapsed };
        var right = Ui.Column(10, run, bar, Ui.Muted(_stage, 12));
        right.VerticalAlignment = VerticalAlignment.Center;
        right.Width = 260;
        g.Children.Add(right.At(1));
        return Ui.Card(g, 26);
    }

    static UIElement Banner(string text)
    {
        var g = Ui.Columns(Ui.Auto, Ui.Star());
        g.Children.Add(new FontIcon { Glyph = "\uE783", Foreground = ThemeBrushes.Bad, FontSize = 18 }.At(0));
        g.Children.Add(Ui.Text(text, 14).At(1));
        return new Border { Background = ThemeBrushes.BadBg, BorderBrush = ThemeBrushes.Bad, BorderThickness = new Thickness(3, 1, 1, 1), CornerRadius = Ui.R, Padding = new Thickness(14, 10, 14, 10), Child = g };
    }

    UIElement Gaming(BenchResult b)
    {
        var head = Ui.Columns(Ui.Star(), Ui.Auto);
        head.Children.Add(Ui.Column(4, Ui.Label("ESTIMATED FRAME RATES"), Ui.Title("How it games", 22), Ui.Muted("High settings, no upscaling or frame generation.")).At(0));
        var seg = Ui.Row(4);
        foreach (var r in Estimator.Resolutions)
        {
            var t = new ToggleButton
            {
                Content = Estimator.Label(r), IsChecked = r == _res, CornerRadius = Ui.RSmall, MinWidth = 64, MinHeight = 32, FontFamily = Ui.Mono, FontSize = 12,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = ThemeBrushes.Text, BorderThickness = new Thickness(0),
            };
            var res = r;
            t.Click += (_, _) => { _res = res; Render(); };
            seg.Children.Add(t);
        }
        seg.Spacing = 2;
        var segBox = new Border { Child = seg, Padding = new Thickness(3), CornerRadius = Ui.R, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1), Background = ThemeBrushes.Surface2, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(segBox.At(1));

        var rows = Ui.Column(18);
        foreach (var game in Estimator.Games)
        {
            var f = b.Fps(game.Key, _res);
            var scale = game.Key switch { "esports" => 500.0, "br" => 300, "aaa" => 200, _ => 150 };
            var row = Ui.Columns(Ui.Px(230), Ui.Star(), Ui.Px(96), Ui.Px(120));
            row.Children.Add(Ui.Column(2, Ui.Text(game.Name, 15, null, FontWeights.SemiBold), Ui.Muted(game.Blurb, 12)).At(0));
            var fill = f.Fps >= 60 ? ThemeBrushes.Accent : ThemeBrushes.Warn;
            var bar = Ui.Bar(V(f.Fps) / scale, fill, 16, 48);
            bar.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(bar.At(1));
            var fps = Ui.Num(Format.Fps(V(f.Fps)), 18, null, bold: true);
            fps.HorizontalTextAlignment = TextAlignment.Right;
            fps.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(fps.At(2));
            var (who, amt) = f.Bottleneck;
            var tag = amt < 0.12 ? Ui.Pill("Balanced", ThemeBrushes.GoodBg, ThemeBrushes.Good) : Ui.Pill($"{who}-bound", ThemeBrushes.Surface2, ThemeBrushes.Muted);
            tag.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(tag.At(3));
            rows.Children.Add(row);
        }
        var legend = Ui.Muted("Bars turn amber below 60 fps. CPU-bound means a faster processor would add frames; GPU-bound means the graphics card sets the pace.", 12);
        return Ui.Card(Ui.Column(20, head, rows, legend), 24);
    }

    UIElement Balance(BenchResult b)
    {
        var f = b.Fps("aaa", Resolution.P1440);
        var (who, amt) = f.Bottleneck;
        var pos = Math.Clamp(who == "GPU" ? 0.5 + amt * 0.5 : 0.5 - amt * 0.5, 0.04, 0.96);

        var track = new Grid { Height = 28 };
        var gradient = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        gradient.GradientStops.Add(new GradientStop { Color = ThemeBrushes.Warn.Color, Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = ThemeBrushes.Good.Color, Offset = 0.5 });
        gradient.GradientStops.Add(new GradientStop { Color = ThemeBrushes.Accent.Color, Offset = 1 });
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(1), Background = gradient, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 });
        var marker = new Grid();
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pos, GridUnitType.Star) });
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        marker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - pos, GridUnitType.Star) });
        marker.Children.Add(new Border { Width = 10, Height = 28, CornerRadius = new CornerRadius(2), Background = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Surface, BorderThickness = new Thickness(2) }.At(1));
        track.Children.Add(marker);
        var labels = Ui.Columns(Ui.Star(), Ui.Star(), Ui.Star());
        labels.Children.Add(Ui.Muted("Processor holds it back", 12).At(0));
        var mid = Ui.Muted("Balanced", 12); mid.HorizontalTextAlignment = TextAlignment.Center;
        labels.Children.Add(mid.At(1));
        var right = Ui.Muted("Graphics card sets the pace", 12); right.HorizontalTextAlignment = TextAlignment.Right;
        labels.Children.Add(right.At(2));
        var text = amt < 0.12 ? "The processor and graphics card are well matched for 1440p gaming."
            : who == "CPU" ? $"At 1440p the processor limits frame rates by about {amt:P0}. A faster gaming CPU (the X3D chips are best) would help more than a bigger GPU."
            : $"At 1440p the graphics card is the limit, which is normal. The processor has about {amt:P0} more frames in it if you upgrade the GPU later.";
        return Ui.Panel("BALANCE · OPEN-WORLD AAA AT 1440P", Ui.Column(12, track, labels, Ui.Text(text, 14)), 22);
    }

    UIElement Stats(BenchResult b)
    {
        var g = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        for (var i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var r = _s.Resolved;

        g.Children.Add(Stat("Processor", Format.Int(V(b.CpuMulti)), "multi-core points",
            [("Single-core", Format.Int(V(b.CpuSingle))), ("Cores / threads", r.Cpu?.Cpu is { } c ? $"{c.Cores} / {c.Threads}" : "—")],
            b.CpuThrottles ? "The cooler can't keep up at full load, so it slows down." : "Runs at full speed with this cooler.", b.CpuThrottles).At(0, 0));
        g.Children.Add(Stat("Memory", b.MemoryBandwidthGBs > 0 ? $"{V(b.MemoryBandwidthGBs):0} GB/s" : "—", "bandwidth",
            [("Latency", b.MemoryLatencyNs > 0 ? $"{b.MemoryLatencyNs:0.0} ns" : "—"), ("Capacity", r.Memory?.Memory is { } m ? $"{m.CapacityGb} GB" : "—")],
            r.Memory?.Memory is { CapacityGb: >= 32 } ? "Plenty for gaming and multitasking." : "Fine for games; tight with lots of tabs open.", false).At(1, 0));
        g.Children.Add(Stat("Storage", b.StorageReadMBs > 0 ? $"{Format.Int(V(b.StorageReadMBs))} MB/s" : "—", "read speed",
            [("Write", b.StorageWriteMBs > 0 ? $"{b.StorageWriteMBs:N0} MB/s" : "—"), ("Typical level load", b.LevelLoadSeconds > 0 ? $"{b.LevelLoadSeconds:0.0} s" : "—")],
            b.StorageReadMBs is > 0 and < 1000 ? "A SATA drive works but loads noticeably slower than NVMe." : "Fast loading in every game.", b.StorageReadMBs is > 0 and < 1000).At(2, 0));

        var psuText = b.PsuLoad > 0 ? $"{b.PsuLoad:P0} of the power supply at peak" : "Pick a power supply";
        g.Children.Add(Stat("Power", $"{V(b.PeakWatts):0} W", "at full load",
            [("While gaming", $"{b.GamingWatts} W"), ("Power supply", r.Psu?.Psu is { } p ? $"{p.Watts} W {p.Rating}" : "—")],
            psuText, b.PsuLoad > 0.85, b.PsuLoad).At(0, 1));
        g.Children.Add(Stat("Temperatures", b.CpuTempC > 0 ? $"{V(b.CpuTempC):0}°C" : "—", "CPU under full load",
            [("GPU while gaming", b.GpuTempC > 0 ? $"{b.GpuTempC}°C" : "—"), ("Noise", b.NoiseDb > 0 ? $"~{b.NoiseDb} dB" : "—")],
            b.NoiseDb >= 36 ? "You'll hear it under load." : b.NoiseDb >= 31 ? "Audible under load, quiet at idle." : "Quiet, even under load.", b.CpuTempC >= 90).At(1, 1));
        g.Children.Add(Stat("Value", b.DollarsPerFrame > 0 ? $"${b.DollarsPerFrame:0.0}" : "—", "per frame (1440p AAA)",
            [("Total", Format.Money(b.Price)), ("Tier", b.Tier)],
            b.DollarsPerFrame is > 0 and < 18 ? "Strong value for the performance." : b.DollarsPerFrame >= 30 ? "You're paying a premium for the top end." : "Typical value.", false).At(2, 1));
        return g;
    }

    static Border Stat(string title, string big, string unit, (string, string)[] rows, string note, bool warn, double? meter = null)
    {
        var col = Ui.Column(8, Ui.Label(title.ToUpperInvariant()));
        var bigText = Ui.Num(big, 28, null, bold: true);
        col.Children.Add(Ui.Column(0, bigText, Ui.Muted(unit, 12)));
        if (meter is { } m and > 0) col.Children.Add(Ui.Bar(m, m > 0.85 ? ThemeBrushes.Bad : ThemeBrushes.Good, 8, 24));
        foreach (var (k, v) in rows)
        {
            var row = Ui.Columns(Ui.Star(), Ui.Auto);
            row.Children.Add(Ui.Muted(k, 13).At(0));
            row.Children.Add(Ui.Num(v, 13).At(1));
            col.Children.Add(row);
        }
        col.Children.Add(Ui.Text(note, 13, warn ? ThemeBrushes.Bad : ThemeBrushes.Text));
        return Ui.Card(col, 20);
    }
}
