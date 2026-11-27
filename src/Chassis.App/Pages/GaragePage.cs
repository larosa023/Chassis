using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.Core.Bench;
using Chassis.Core.Builds;
using Chassis.Core.Catalog;
using Windows.ApplicationModel.DataTransfer;

namespace Chassis.App.Pages;

/// <summary>Saved builds: open, copy, share, delete, and compare two side by side.</summary>
public sealed partial class GaragePage : UserControl
{
    readonly BuildSession _s;
    readonly StackPanel _body = new() { Spacing = 18, MaxWidth = 1180, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 16, 0) };
    readonly TextBox _code = new() { PlaceholderText = "Paste a CHASSIS1- share code", MinWidth = 320, MinHeight = 40 };
    readonly TextBlock _msg = Ui.Muted("");
    string? _a, _b;

    public ScrollViewer Scroller => _scroll;
    public event Action? OpenRequested;

    public GaragePage(BuildSession session)
    {
        _s = session;
        _scroll.Content = _body;
        Content = _scroll;
        _s.Garage.Changed += Render;
        Render();
    }

    Grid? _head;
    readonly TextBlock _count = Ui.Muted("");

    Grid Header()
    {
        var head = Ui.Columns(Ui.Star(), Ui.Auto);
        head.Children.Add(Ui.Column(4, Ui.Title("Garage", 32), _count).At(0));
        var open = Ui.Ghost("Open code", () =>
        {
            if (_s.Import(_code.Text, out var b)) { _code.Text = ""; _msg.Text = ""; OpenRequested?.Invoke(); }
            else _msg.Text = "That code didn't work. It should start with CHASSIS1-.";
        });
        var actions = Ui.Row(8, _code, open, Ui.Accent("New build", () => { _s.NewBuild(); OpenRequested?.Invoke(); }));
        actions.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(actions.At(1));
        return head;
    }

    void Render()
    {
        _body.Children.Clear();
        _head ??= Header();
        _count.Text = $"{_s.Garage.Builds.Count} saved build{(_s.Garage.Builds.Count == 1 ? "" : "s")}. Every change is saved as you go.";
        _body.Children.Add(_head);
        if (_msg.Text.Length > 0) _body.Children.Add(_msg);

        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 372, ItemHeight = 330 };
        foreach (var b in _s.Garage.Builds) grid.Children.Add(BuildCard(b));
        _body.Children.Add(grid);
        _body.Children.Add(Compare());
    }

    UIElement BuildCard(PcBuild build)
    {
        var r = build.Resolve(_s.Catalog);
        var bench = Estimator.Run(r);
        var report = Chassis.Core.Checks.CompatibilityChecker.Check(r);
        var isOpen = build.Id == _s.Build.Id;
        var col = Ui.Column(10);
        var top = Ui.Columns(Ui.Star(), Ui.Auto);
        top.Children.Add(Ui.Text(build.Name, 18, null, FontWeights.SemiBold).At(0));
        if (isOpen) top.Children.Add(Ui.Pill("Open now", ThemeBrushes.AccentSoft, ThemeBrushes.Accent).At(1));
        col.Children.Add(top);
        var stats = Ui.Row(8, Ui.Pill(Format.Money(r.Total), ThemeBrushes.Surface2, ThemeBrushes.Text),
            Ui.Pill(bench.Score > 0 ? $"{bench.Score:N0} · {bench.Tier}" : "Incomplete", ThemeBrushes.Surface2, ThemeBrushes.Text));
        if (report.Problems > 0) stats.Children.Add(Ui.Pill($"{report.Problems} problem{(report.Problems == 1 ? "" : "s")}", ThemeBrushes.BadBg, ThemeBrushes.Bad));
        col.Children.Add(stats);
        var list = Ui.Column(3);
        foreach (var c in new[] { Category.Cpu, Category.Gpu, Category.Memory, Category.Case })
            list.Children.Add(Ui.Text($"{Categories.Short(c)}  {r.Get(c)?.Name ?? "—"}", 13, r.Get(c) is null ? ThemeBrushes.Muted : ThemeBrushes.Text, wrap: false));
        list.Children.Add(Ui.Muted(r.Complete ? $"~{bench.Fps("aaa", Resolution.P1440).Fps:0} fps in open-world games at 1440p" : $"{r.Count} of 8 parts chosen", 12));
        col.Children.Add(list);

        var open = Ui.Accent(isOpen ? "Keep building" : "Open", () => { if (!isOpen) _s.Open(build); OpenRequested?.Invoke(); });
        open.MinHeight = 38;
        open.Padding = new Thickness(16, 6, 16, 6);
        var copy = Ui.Icon("\uE8C8", "Duplicate", () => _s.Garage.Save(build.Copy()));
        var share = Ui.Icon("\uE72D", "Copy share code", () =>
        {
            var dp = new DataPackage();
            dp.SetText(ShareCode.Encode(build));
            Clipboard.SetContent(dp);
        });
        var del = Ui.Icon("\uE74D", "Delete", () => { });
        var confirm = Ui.Accent("Delete", () =>
        {
            if (_s.Garage.Builds.Count <= 1) return;
            var wasOpen = isOpen;
            _s.Garage.Delete(build.Id);
            if (wasOpen) _s.Open(_s.Garage.Current);
        });
        confirm.Background = ThemeBrushes.Bad;
        del.Flyout = new Flyout
        {
            Content = Ui.Column(10, Ui.Text($"Delete “{build.Name}”?", 14, null, FontWeights.SemiBold),
                Ui.Muted(_s.Garage.Builds.Count <= 1 ? "You need at least one build in the garage." : "This can't be undone. Copy its share code first if you might want it back."), confirm),
        };
        var buttons = Ui.Row(8, open, copy, share, del);
        col.Children.Add(buttons);

        return new Border
        {
            Background = ThemeBrushes.Surface, BorderBrush = isOpen ? ThemeBrushes.Accent : ThemeBrushes.Line, BorderThickness = new Thickness(isOpen ? 2 : 1),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(20), Margin = new Thickness(0, 0, 16, 16), Child = col,
        };
    }

    UIElement Compare()
    {
        var builds = _s.Garage.Builds;
        if (builds.Count < 2) return new Border();
        _a ??= builds[0].Id;
        _b ??= builds[1].Id;
        if (builds.All(x => x.Id != _a)) _a = builds[0].Id;
        if (builds.All(x => x.Id != _b)) _b = builds[Math.Min(1, builds.Count - 1)].Id;

        ComboBox Pick(string? selected, Action<string> set)
        {
            var cb = new ComboBox { MinWidth = 260, MinHeight = 40 };
            foreach (var b in builds) cb.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b.Id });
            cb.SelectedItem = cb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected);
            cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem { Tag: string id }) { set(id); Render(); } };
            return cb;
        }

        var a = builds.First(x => x.Id == _a).Resolve(_s.Catalog);
        var b = builds.First(x => x.Id == _b).Resolve(_s.Catalog);
        var ra = Estimator.Run(a);
        var rb = Estimator.Run(b);

        var table = new Grid { RowSpacing = 10, ColumnSpacing = 16 };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new (string Label, double A, double B, Func<double, string> Fmt, bool HigherIsBetter)[]
        {
            ("Chassis Score", ra.Score, rb.Score, v => Format.Int(v), true),
            ("Open-world AAA, 1440p", ra.Fps("aaa", Resolution.P1440).Fps, rb.Fps("aaa", Resolution.P1440).Fps, Format.Fps, true),
            ("Open-world AAA, 4K", ra.Fps("aaa", Resolution.P2160).Fps, rb.Fps("aaa", Resolution.P2160).Fps, Format.Fps, true),
            ("Ray tracing, 4K", ra.Fps("rt", Resolution.P2160).Fps, rb.Fps("rt", Resolution.P2160).Fps, Format.Fps, true),
            ("Competitive shooter, 1080p", ra.Fps("esports", Resolution.P1080).Fps, rb.Fps("esports", Resolution.P1080).Fps, Format.Fps, true),
            ("Multi-core", ra.CpuMulti, rb.CpuMulti, v => Format.Int(v), true),
            ("Power at full load", ra.PeakWatts, rb.PeakWatts, v => $"{v:0} W", false),
            ("Price", (double)ra.Price, (double)rb.Price, v => Format.Money((decimal)v), false),
        };
        var i = 0;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.Children.Add(Pick(_a, id => _a = id).At(1, 0));
        table.Children.Add(Pick(_b, id => _b = id).At(2, 0));
        foreach (var row in rows)
        {
            i++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            table.Children.Add(Ui.Muted(row.Label, 14).At(0, i));
            var aWins = row.A != row.B && (row.HigherIsBetter ? row.A > row.B : row.A < row.B) && row.A > 0;
            var bWins = row.A != row.B && !aWins && row.B > 0;
            table.Children.Add(Ui.Text(row.Fmt(row.A), 15, aWins ? ThemeBrushes.Good : ThemeBrushes.Text, aWins ? FontWeights.Bold : FontWeights.Normal).At(1, i));
            table.Children.Add(Ui.Text(row.Fmt(row.B), 15, bWins ? ThemeBrushes.Good : ThemeBrushes.Text, bWins ? FontWeights.Bold : FontWeights.Normal).At(2, i));
        }
        return Ui.Card(Ui.Column(16, Ui.Title("Compare", 22), Ui.Muted("Pick any two builds. The better number in each row is green."), table), 24);
    }
}
