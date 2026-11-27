using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Chassis.App.Theming;

namespace Chassis.App;

/// <summary>
/// Chassis's design system, built in code with the theme's brushes.
/// The look is a hardware spec sheet: squared-off panels with hairline borders, Chakra Petch for headings,
/// IBM Plex Sans for text and JetBrains Mono for every number and label.
/// </summary>
public static class Ui
{
    const string F = "ms-appx:///Assets/Fonts/";
    public static readonly FontFamily Display = new(F + "ChakraPetch-SemiBold.ttf#Chakra Petch");
    public static readonly FontFamily DisplayBold = new(F + "ChakraPetch-Bold.ttf#Chakra Petch");
    public static readonly FontFamily Body = new(F + "IBMPlexSans-Regular.ttf#IBM Plex Sans");
    public static readonly FontFamily BodyStrong = new(F + "IBMPlexSans-SemiBold.ttf#IBM Plex Sans");
    public static readonly FontFamily Mono = new(F + "JetBrainsMono-Medium.ttf#JetBrains Mono");
    public static readonly FontFamily MonoBold = new(F + "JetBrainsMono-Bold.ttf#JetBrains Mono");

    /// <summary>Corner radius for panels and controls: small and square-ish, like machined parts.</summary>
    public static readonly CornerRadius R = new(6);
    public static readonly CornerRadius RSmall = new(3);

    static bool Strong(Windows.UI.Text.FontWeight w) => w.Weight >= 600;

    public static TextBlock Text(string text, double size = 14, Brush? color = null, Windows.UI.Text.FontWeight? weight = null, bool wrap = true)
    {
        var w = weight ?? FontWeights.Normal;
        return new TextBlock
        {
            Text = text, FontSize = size, Foreground = color ?? ThemeBrushes.Text,
            FontFamily = Strong(w) ? BodyStrong : Body, FontWeight = Strong(w) ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
    }

    public static TextBlock Muted(string text, double size = 13) => Text(text, size, ThemeBrushes.Muted);

    public static TextBlock Title(string text, double size = 28) => new()
    {
        Text = text, FontSize = size, FontFamily = Display, FontWeight = FontWeights.SemiBold, Foreground = ThemeBrushes.Text,
        TextWrapping = TextWrapping.WrapWholeWords, LineHeight = size * 1.1,
    };

    /// <summary>A number in the mono face (prices, fps, watts, scores).</summary>
    public static TextBlock Num(string text, double size = 14, Brush? color = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? ThemeBrushes.Text, FontFamily = bold ? MonoBold : Mono,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Medium, TextWrapping = TextWrapping.NoWrap,
    };

    /// <summary>A small spec-sheet label in mono, e.g. "03 · Processor".</summary>
    public static TextBlock Label(string text, Brush? color = null) => new()
    {
        Text = text, FontSize = 11, Foreground = color ?? ThemeBrushes.Muted, FontFamily = Mono, FontWeight = FontWeights.Medium,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A panel: flat surface, hairline border, small corners.</summary>
    public static Border Card(UIElement child, double padding = 20) => new()
    {
        Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
        CornerRadius = R, Padding = new Thickness(padding), Child = child,
    };

    /// <summary>A panel with a mono caption above its content, like a section of a spec sheet.</summary>
    public static Border Panel(string caption, UIElement child, double padding = 20, FrameworkElement? right = null)
    {
        var head = Columns(Star(), Auto);
        var cap = Label(caption);
        cap.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(cap.At(0));
        if (right is not null) head.Children.Add(right.At(1));
        return Card(Column(14, head, child), padding);
    }

    /// <summary>A small square tag (tier, price, status).</summary>
    public static Border Pill(string text, Brush bg, Brush fg, double size = 11) => new()
    {
        Background = bg, CornerRadius = RSmall, Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = size, Foreground = fg, FontWeight = FontWeights.Medium, FontFamily = Mono },
    };

    public static Button Accent(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text, Background = ThemeBrushes.Accent, Foreground = ThemeBrushes.OnAccent, FontWeight = FontWeights.SemiBold, FontFamily = BodyStrong,
            CornerRadius = R, Padding = new Thickness(18, 9, 18, 9), MinHeight = 40, BorderThickness = new Thickness(0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Ghost(string text, Action onClick, string? glyph = null)
    {
        var b = new Button
        {
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
            CornerRadius = R, Padding = new Thickness(12, 7, 12, 7), MinHeight = 38, FontFamily = Body,
            Content = glyph is null ? text : Row(7, new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontFamily = Body }),
        };
        AutomationProperties.SetName(b, text);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Icon(string glyph, string tip, Action onClick)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 15 }, Width = 38, Height = 38, Padding = new Thickness(0),
            Background = ThemeBrushes.Surface, Foreground = ThemeBrushes.Text, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
            CornerRadius = R,
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

    /// <summary>A segmented meter like an LED bar: fraction 0..1 of the blocks lit.</summary>
    public static Grid Bar(double fraction, Brush fill, double height = 10, int segments = 40)
    {
        var f = Math.Clamp(fraction, 0, 1);
        var lit = (int)Math.Round(f * segments);
        var g = new Grid { Height = height, ColumnSpacing = 2 };
        for (var i = 0; i < segments; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new Border { Background = i < lit ? fill : ThemeBrushes.Track, CornerRadius = new CornerRadius(1) };
            Grid.SetColumn(cell, i);
            g.Children.Add(cell);
        }
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
