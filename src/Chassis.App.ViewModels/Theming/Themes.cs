using System.Globalization;

namespace Chassis.App.ViewModels.Theming;

public sealed record ThemePalette(string Key, string Name, string Bg, string Surface, string Surface2, string Text, string Muted,
    string Accent, string OnAccent, string Good, string GoodBg, string Bad, string Line, string Track, bool IsDark)
{
    /// <summary>The colours the 3D scene needs.</summary>
    public object ForScene => new { type = "theme", bg = Bg, surface = Surface, text = Text, muted = Muted, accent = Accent, line = Line, isDark = IsDark };
}

public static class Themes
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new("bench", "Bench", "#EEF0F3", "#FFFFFF", "#F4F5F7", "#14161A", "#545A65", "#E8480F", "#FFFFFF", "#157A4A", "#E2F4EA", "#C8261B", "#DCDFE5", "#E6E8EC", false),
        new("graphite", "Graphite", "#0E1014", "#171A20", "#20242C", "#F2F3F5", "#9AA1AD", "#FF6A2B", "#0E1014", "#4ADE80", "#13301F", "#FF6B63", "#262A33", "#262A33", true),
        new("circuit", "Circuit", "#0B1A14", "#10241B", "#173226", "#E9F5EE", "#93B5A3", "#E7B84A", "#0B1A14", "#7EE2A8", "#17382A", "#FF7A6E", "#1F3B2F", "#1C3529", true),
        new("frost", "Frost", "#E9F0F6", "#FFFFFF", "#E2EBF2", "#0E1B26", "#4A5D6C", "#0A6FA8", "#FFFFFF", "#12704B", "#DDF3E9", "#B42318", "#D3E0E9", "#DCE7EF", false),
        new("synth", "Synthwave", "#120B1E", "#1C1230", "#271A42", "#F6F1FF", "#B4A6D3", "#22D3EE", "#120B1E", "#A3E635", "#22301A", "#FB7185", "#2E2148", "#2A1E42", true),
    ];

    public static ThemePalette Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
}

/// <summary>WCAG 2 contrast ratio.</summary>
public static class Contrast
{
    public static double Ratio(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return (byte.Parse(h[..2], NumberStyles.HexNumber), byte.Parse(h[2..4], NumberStyles.HexNumber), byte.Parse(h[4..6], NumberStyles.HexNumber));
    }

    static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        static double C(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }
}
