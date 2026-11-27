using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Chassis.App.Services;
using Chassis.App.Theming;
using Chassis.App.ViewModels;
using Chassis.Core.Catalog;
using Chassis.Core.Checks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Chassis.App.Pages;

/// <summary>The main screen: the 3D bench, the eight part slots, the parts picker and the live checks.</summary>
public sealed partial class BuildPage : UserControl
{
    readonly BuildSession _s;
    readonly TextBox _name = new();
    readonly TextBlock _total = Ui.Title("", 30), _scoreLine = Ui.Muted(""), _wattsLine = Ui.Muted("");
    readonly Grid _slots = new() { ColumnSpacing = 10, RowSpacing = 10 };
    readonly StackPanel _list = new() { Spacing = 8 };
    readonly TextBlock _catTitle = Ui.Title("", 22);
    readonly TextBox _search = new() { MinHeight = 40, CornerRadius = new CornerRadius(12) };
    readonly ComboBox _sort = new() { MinWidth = 150, MinHeight = 40, CornerRadius = new CornerRadius(12) };
    readonly Button _remove;
    readonly StackPanel _checks = new() { Spacing = 10 };
    readonly TextBlock _checksHead = Ui.Text("", 16, null, FontWeights.SemiBold);
    readonly FontIcon _checksIcon = new() { FontSize = 18 };
    readonly Button _undo, _redo;
    readonly ToggleButton _exploded = Toggle("Exploded", "\uE740"), _spin = Toggle("Spin", "\uE7AD"), _rgb = Toggle("RGB", "\uE790");
    readonly TextBox _shareBox = new() { IsReadOnly = true, Width = 360, TextWrapping = TextWrapping.Wrap };
    readonly TextBox _importBox = new() { PlaceholderText = "Paste a CHASSIS1- code", Width = 360 };
    readonly TextBlock _shareMsg = Ui.Muted("");

    public SceneView Scene { get; } = new();
    public event Action? BenchRequested;

    public BuildPage(BuildSession session)
    {
        _s = session;
        _remove = Ui.Ghost("Remove", () => _s.Remove(_s.Selected), "\uE74D");
        _undo = Ui.Icon("\uE7A7", "Undo (Ctrl+Z)", _s.Undo);
        _redo = Ui.Icon("\uE7A6", "Redo (Ctrl+Y)", _s.Redo);

        var root = new Grid { RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Toolbar());

        var body = Ui.Columns(Ui.Star(), Ui.Px(410));
        body.ColumnSpacing = 20;
        body.Children.Add(Left().At(0));
        body.Children.Add(Right().At(1));
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;

        _s.BuildChanged += RenderAll;
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BuildSession.Candidates)) RenderList();
            if (e.PropertyName == nameof(BuildSession.Selected)) { RenderSlots(); RenderHeader(); }
            if (e.PropertyName == nameof(BuildSession.Build) && _name.FocusState == FocusState.Unfocused) _name.Text = _s.Build.Name;
        };
        Scene.Picked += c => { _s.Selected = c; Scene.SetView(new { type = "view", focus = c.ToString() }); };
        ThemeBrushes.Changed += () => Scene.SetTheme(ThemeBrushes.Current.ForScene);
        Scene.SetTheme(ThemeBrushes.Current.ForScene);

        _rgb.IsChecked = _s.Garage.Settings.RgbLighting;
        _spin.IsChecked = _s.Garage.Settings.AutoRotate;
        Scene.SetView(new { type = "view", rgb = _rgb.IsChecked == true, autoRotate = _spin.IsChecked == true });
        _exploded.Click += (_, _) => Scene.SetView(new { type = "view", exploded = _exploded.IsChecked == true });
        _spin.Click += (_, _) => { Scene.SetView(new { type = "view", autoRotate = _spin.IsChecked == true }); _s.Garage.Update(x => x with { AutoRotate = _spin.IsChecked == true }); };
        _rgb.Click += (_, _) => { Scene.SetView(new { type = "view", rgb = _rgb.IsChecked == true }); _s.Garage.Update(x => x with { RgbLighting = _rgb.IsChecked == true }); };

        RenderAll();
    }

    public void SetRgb(bool on) { _rgb.IsChecked = on; Scene.SetView(new { type = "view", rgb = on }); }
    public void SetSpin(bool on) { _spin.IsChecked = on; Scene.SetView(new { type = "view", autoRotate = on }); }
    public void SetExploded(bool on) { _exploded.IsChecked = on; Scene.SetView(new { type = "view", exploded = on }); }

    static ToggleButton Toggle(string text, string glyph)
    {
        var t = new ToggleButton
        {
            Content = Ui.Row(6, new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }),
            CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 14, 6), MinHeight = 36,
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(t, text);
        return t;
    }

    // ---------- layout ----------

    FrameworkElement Toolbar()
    {
        var g = Ui.Columns(Ui.Star(), Ui.Auto);
        _name.FontFamily = Ui.Display;
        _name.FontSize = 26;
        _name.FontWeight = FontWeights.SemiBold;
        _name.BorderThickness = new Thickness(0);
        _name.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _name.Padding = new Thickness(0, 4, 8, 4);
        _name.MaxWidth = 560;
        _name.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTipService.SetToolTip(_name, "Rename this build");
        _name.LostFocus += (_, _) => _s.Rename(_name.Text);
        _name.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { _s.Rename(_name.Text); Focus(FocusState.Programmatic); } };
        g.Children.Add(_name.At(0));

        var share = Ui.Ghost("Share", () => { }, "\uE72D");
        var copy = Ui.Accent("Copy code", () =>
        {
            var dp = new DataPackage();
            dp.SetText(_s.ShareCode);
            Clipboard.SetContent(dp);
            _shareMsg.Text = "Copied. Anyone with Chassis can paste it under Open a code.";
        });
        var open = Ui.Ghost("Open", () =>
        {
            if (_s.Import(_importBox.Text, out var b)) { _shareMsg.Text = $"Opened “{b.Name}”."; _importBox.Text = ""; }
            else _shareMsg.Text = "That code didn't work. It should start with CHASSIS1-.";
        });
        share.Flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            Content = Ui.Column(10,
                Ui.Text("Share this build", 15, null, FontWeights.SemiBold),
                Ui.Muted("A short code with every part. Friends paste it into Chassis to open the same build."),
                _shareBox, copy,
                Ui.Text("Open a code", 15, null, FontWeights.SemiBold),
                Ui.Row(8, _importBox, open), _shareMsg),
        };
        share.Flyout.Opening += (_, _) => { _shareBox.Text = _s.ShareCode; _shareMsg.Text = ""; };

        var bench = Ui.Accent("Run benchmarks", () => BenchRequested?.Invoke());
        bench.MinHeight = 40;
        bench.Padding = new Thickness(16, 8, 16, 8);
        g.Children.Add(Ui.Row(8, _undo, _redo, share, Ui.Ghost("New", () => _s.NewBuild(), "\uE710"), bench).At(1));
        return g;
    }

    FrameworkElement Left()
    {
        var g = new Grid { RowSpacing = 14 };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var viewport = new Grid();
        viewport.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(24), BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1), Background = ThemeBrushes.Bg,
            Child = Scene,
        });
        var tools = Ui.Row(8, _exploded, _spin, _rgb, ToolButton("Reset view", "\uE72C", () => Scene.SetView(new { type = "view", reset = true })));
        tools.HorizontalAlignment = HorizontalAlignment.Right;
        tools.VerticalAlignment = VerticalAlignment.Top;
        tools.Margin = new Thickness(14);
        viewport.Children.Add(tools);
        g.Children.Add(viewport);

        for (var i = 0; i < 4; i++) _slots.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _slots.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _slots.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_slots, 1);
        g.Children.Add(_slots);
        return g;
    }

    static Button ToolButton(string text, string glyph, Action click)
    {
        var b = new Button
        {
            Content = Ui.Row(6, new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }),
            CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 6, 14, 6), MinHeight = 36,
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
        };
        b.Click += (_, _) => click();
        return b;
    }

    FrameworkElement Right()
    {
        var g = new Grid { RowSpacing = 14 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Summary
        var sum = Ui.Columns(Ui.Star(), Ui.Auto);
        sum.Children.Add(Ui.Column(2, Ui.Muted("Total"), _total).At(0));
        var right = Ui.Column(4, _scoreLine, _wattsLine);
        right.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        _scoreLine.HorizontalTextAlignment = TextAlignment.Right;
        _wattsLine.HorizontalTextAlignment = TextAlignment.Right;
        sum.Children.Add(right.At(1));
        g.Children.Add(Ui.Card(sum, 18));

        // Parts picker
        var head = Ui.Columns(Ui.Star(), Ui.Auto);
        head.Children.Add(_catTitle.At(0));
        head.Children.Add(_remove.At(1));
        _search.TextChanged += (_, _) => _s.Query = _search.Text;
        foreach (var (label, mode) in new[] { ("Best match", SortMode.Recommended), ("Price: low", SortMode.PriceLow), ("Price: high", SortMode.PriceHigh), ("Performance", SortMode.Performance) })
            _sort.Items.Add(new ComboBoxItem { Content = label, Tag = mode });
        _sort.SelectedIndex = 0;
        _sort.SelectionChanged += (_, _) => { if (_sort.SelectedItem is ComboBoxItem { Tag: SortMode m }) _s.Sort = m; };
        var filters = Ui.Columns(Ui.Star(), Ui.Auto);
        filters.Children.Add(_search.At(0));
        filters.Children.Add(_sort.At(1));
        var scroller = new ScrollViewer { Content = _list, Padding = new Thickness(0, 0, 12, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var picker = new Grid { RowSpacing = 12 };
        picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        picker.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        picker.Children.Add(head);
        picker.Children.Add(filters.At(0, 1));
        picker.Children.Add(scroller.At(0, 2));
        var pickerCard = Ui.Card(picker, 18);
        Grid.SetRow(pickerCard, 1);
        g.Children.Add(pickerCard);

        // Checks
        var checksHead = Ui.Row(10, _checksIcon, _checksHead);
        var checksBody = new ScrollViewer { Content = _checks, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        var checksCard = Ui.Card(Ui.Column(12, checksHead, checksBody), 18);
        Grid.SetRow(checksCard, 2);
        g.Children.Add(checksCard);
        return g;
    }

    // ---------- rendering ----------

    void RenderAll()
    {
        if (_name.FocusState == FocusState.Unfocused) _name.Text = _s.Build.Name;
        _total.Text = _s.TotalText;
        var b = _s.Bench;
        _scoreLine.Text = b.Score > 0 ? $"Score {b.Score:N0} · {b.Tier}" : "Score: add parts";
        _wattsLine.Text = b.PeakWatts > 0 ? $"~{b.PeakWatts} W at full load" : "";
        _undo.IsEnabled = _s.CanUndo;
        _redo.IsEnabled = _s.CanRedo;
        RenderSlots();
        RenderHeader();
        RenderList();
        RenderChecks();
        Scene.SetBuild(_s.SceneJson());
    }

    void RenderHeader()
    {
        _catTitle.Text = Categories.Label(_s.Selected);
        _search.PlaceholderText = $"Search {Categories.Label(_s.Selected).ToLowerInvariant()}s";
        if (_search.Text != _s.Query) _search.Text = _s.Query;
        _remove.Visibility = _s.Build[_s.Selected] is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void RenderSlots()
    {
        _slots.Children.Clear();
        var i = 0;
        foreach (var slot in _s.Slots)
        {
            var on = slot.Category == _s.Selected;
            var stateBrush = slot.State switch
            {
                SlotState.Problem => ThemeBrushes.Bad,
                SlotState.Warning => ThemeBrushes.Warn,
                SlotState.Ok => ThemeBrushes.Good,
                _ => ThemeBrushes.Track,
            };
            var dot = new Ellipse { Width = 8, Height = 8, Fill = stateBrush, VerticalAlignment = VerticalAlignment.Center };
            var top = Ui.Row(6, dot, Ui.Text(slot.Label, 12, ThemeBrushes.Muted, FontWeights.SemiBold, wrap: false));
            var value = Ui.Text(slot.Value, 13, slot.State == SlotState.Empty ? ThemeBrushes.Muted : ThemeBrushes.Text, FontWeights.SemiBold, wrap: false);
            var b = new Button
            {
                Content = Ui.Column(4, top, value),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(16), MinHeight = 62,
                Background = on ? ThemeBrushes.AccentSoft : ThemeBrushes.Surface,
                BorderBrush = on ? ThemeBrushes.Accent : slot.State == SlotState.Problem ? ThemeBrushes.Bad : ThemeBrushes.Line,
                BorderThickness = new Thickness(on || slot.State == SlotState.Problem ? 2 : 1),
            };
            ToolTipService.SetToolTip(b, slot.State == SlotState.Empty ? $"Choose a {slot.Label.ToLowerInvariant()}" : $"{slot.Label}: {slot.Value} ({Format.Money(slot.Price)})");
            var cat = slot.Category;
            b.Click += (_, _) => { _s.Selected = cat; if (slot.State != SlotState.Empty) Scene.SetView(new { type = "view", focus = cat.ToString() }); };
            Grid.SetColumn(b, i % 4);
            Grid.SetRow(b, i / 4);
            _slots.Children.Add(b);
            i++;
        }
    }

    void RenderList()
    {
        _list.Children.Clear();
        if (_s.Candidates.Count == 0)
        {
            _list.Children.Add(Ui.Muted("Nothing matches that search."));
            return;
        }
        foreach (var c in _s.Candidates) _list.Children.Add(CandidateCard(c));
    }

    UIElement CandidateCard(Candidate c)
    {
        var g = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
        g.Children.Add(new Ellipse
        {
            Width = 16, Height = 16, Fill = Ui.Hex(c.Part.Color), Stroke = ThemeBrushes.Line, StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0),
        }.At(0));
        var text = Ui.Column(3,
            Ui.Text(c.Part.Name, 14, null, FontWeights.SemiBold),
            Ui.Muted($"{c.Part.Brand} · {string.Join(" · ", c.Part.Facts)}", 12));
        if (!c.Fits) text.Children.Add(Ui.Text("Won't work: " + c.Reason, 12, ThemeBrushes.Bad, FontWeights.SemiBold));
        g.Children.Add(text.At(1));
        var delta = Ui.Text(c.DeltaText, 12, c.Selected ? ThemeBrushes.Accent : c.ScoreDelta > 0 ? ThemeBrushes.Good : ThemeBrushes.Muted, FontWeights.SemiBold, wrap: false);
        delta.HorizontalTextAlignment = TextAlignment.Right;
        var price = Ui.Text(c.PriceText, 15, null, FontWeights.SemiBold, wrap: false);
        price.HorizontalTextAlignment = TextAlignment.Right;
        g.Children.Add(Ui.Column(2, price, delta).At(2));
        var b = new Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(16),
            Background = c.Selected ? ThemeBrushes.AccentSoft : ThemeBrushes.Surface2,
            BorderBrush = c.Selected ? ThemeBrushes.Accent : ThemeBrushes.Line, BorderThickness = new Thickness(c.Selected ? 2 : 1),
            Opacity = c.Fits || c.Selected ? 1 : 0.78,
        };
        var id = c.Part.Id;
        b.Click += (_, _) => _s.Choose(id);
        return b;
    }

    void RenderChecks()
    {
        var r = _s.Report;
        _checksHead.Text = r.Headline;
        (_checksIcon.Glyph, _checksIcon.Foreground) = r.Problems > 0 ? ("\uE783", ThemeBrushes.Bad) : r.Warnings > 0 ? ("\uE7BA", ThemeBrushes.Warn) : ("\uE73E", ThemeBrushes.Good);
        _checks.Children.Clear();
        foreach (var f in r.Findings.Where(f => f.Severity != Severity.Good))
        {
            var brush = f.Severity switch { Severity.Problem => ThemeBrushes.Bad, Severity.Warning => ThemeBrushes.Warn, _ => ThemeBrushes.Muted };
            var row = Ui.Columns(Ui.Auto, Ui.Star());
            row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = brush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) }.At(0));
            row.Children.Add(Ui.Column(2, Ui.Text(f.Title, 13, null, FontWeights.SemiBold), Ui.Muted(f.Detail, 12)).At(1));
            var parts = f.Parts;
            row.Tapped += (_, _) => { if (parts.Length > 0) _s.Selected = parts[0]; };
            _checks.Children.Add(row);
        }
        var good = r.Findings.Count(f => f.Severity == Severity.Good);
        if (good > 0)
        {
            var ok = Ui.Columns(Ui.Auto, Ui.Star());
            ok.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 13, Foreground = ThemeBrushes.Good, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) }.At(0));
            ok.Children.Add(Ui.Muted($"{good} check{(good == 1 ? "" : "s")} passed: " + string.Join(", ", r.Findings.Where(f => f.Severity == Severity.Good).Select(f => f.Title.ToLowerInvariant())) + ".", 12).At(1));
            _checks.Children.Add(ok);
        }
    }
}
