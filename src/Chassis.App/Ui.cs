using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Chassis.App.Theming;

namespace Chassis.App;

/// <summary>Small helpers for building the UI in code with the theme's brushes.</summary>
public static class Ui
{
    public static readonly FontFamily Display = new("ms-appx:///Assets/Fonts/SpaceGrotesk.ttf#Space Grotesk");
    public static readonly FontFamily Body = new("ms-appx:///Assets/Fonts/Inter.ttf#Inter");

    public static TextBlock Text(string text, double size = 14, Brush? color = null, Windows.UI.Text.FontWeight? weight = null, bool wrap = true) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? ThemeBrushes.Text, FontFamily = Body,
        FontWeight = weight ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Muted(string text, double size = 13) => Text(text, size, ThemeBrushes.Muted);

    public static TextBlock Title(string text, double size = 28) => new()
    {
        Text = text, FontSize = size, FontFamily = Display, FontWeight = FontWeights.SemiBold, Foreground = ThemeBrushes.Text,
        TextWrapping = TextWrapping.WrapWholeWords, LineHeight = size * 1.1,
    };

    public static Border Card(UIElement child, double padding = 22) => new()
    {
        Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(22), Padding = new Thickness(padding), Child = child,
    };

    public static Border Pill(string text, Brush bg, Brush fg, double size = 12) => new()
    {
        Background = bg, CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = size, Foreground = fg, FontWeight = FontWeights.SemiBold, FontFamily = Body },
    };

    public static Button Accent(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text, Background = ThemeBrushes.Accent, Foreground = ThemeBrushes.OnAccent, FontWeight = FontWeights.SemiBold,
            CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 10, 20, 10), MinHeight = 44, BorderThickness = new Thickness(0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Ghost(string text, Action onClick, string? glyph = null)
    {
        var b = new Button
        {
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 14, 8), MinHeight = 40,
            Content = glyph is null ? text : Row(6, new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }),
        };
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Icon(string glyph, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 16 }, Width = 40, Height = 40, Padding = new Thickness(0),
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
        };
        ToolTipService.SetToolTip(b, tip);
        AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    /// <summary>A horizontal bar: fraction 0..1 of the track filled.</summary>
    public static Grid Bar(double fraction, Brush fill, double height = 10)
    {
        var f = Math.Clamp(fraction, 0, 1);
        var g = new Grid { Height = height, CornerRadius = new CornerRadius(height / 2), Background = ThemeBrushes.Track };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(f, 0.0001), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - f, 0.0001), GridUnitType.Star) });
        g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
        return g;
    }

    public static Grid Columns(params GridLength[] widths)
    {
        var g = new Grid { ColumnSpacing = 12 };
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    public static T At<T>(this T e, int column, int row = 0) where T : FrameworkElement
    {
        Grid.SetColumn(e, column);
        Grid.SetRow(e, row);
        return e;
    }

    public static GridLength Star(double v = 1) => new(v, GridUnitType.Star);
    public static GridLength Auto => GridLength.Auto;
    public static GridLength Px(double v) => new(v);

    public static Brush Hex(string hex) => new SolidColorBrush(ThemeBrushes.C(hex));
}
