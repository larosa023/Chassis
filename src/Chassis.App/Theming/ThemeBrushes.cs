using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Chassis.App.ViewModels.Theming;
using Windows.UI;

namespace Chassis.App.Theming;

/// <summary>One shared brush per theme token. Switching themes recolors these brushes in place, so every element updates live.</summary>
public static class ThemeBrushes
{
    public static readonly SolidColorBrush Bg = new(), Surface = new(), Surface2 = new(), Text = new(), Muted = new(), Accent = new(),
        OnAccent = new(), Good = new(), GoodBg = new(), Bad = new(), BadBg = new(), Warn = new(), Line = new(), Track = new(), AccentSoft = new();

    public static ThemePalette Current { get; private set; } = Themes.Get("bench");

    public static void Register(ResourceDictionary r)
    {
        // WinUI's own controls (toggle, slider, progress, focus) use the Windows accent (usually blue). Point them at the theme's accent.
        foreach (var k in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                                  "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
                                  "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                                  "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                                  "AccentAAFillColorDefaultBrush", "ProgressRingForegroundThemeBrush", "ProgressBarForeground",
                                  "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "ToggleButtonBackgroundCheckedPressed",
                                  "TextControlBorderBrushFocused", "ComboBoxItemPillFillBrush" })
            r[k] = Accent;
        foreach (var k in new[] { "TextOnAccentFillColorPrimaryBrush", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
                                  "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed" })
            r[k] = OnAccent;
        Apply(Current);
    }

    public static event Action? Changed;

    public static void Apply(ThemePalette p)
    {
        Current = p;
        Bg.Color = C(p.Bg); Surface.Color = C(p.Surface); Surface2.Color = C(p.Surface2); Text.Color = C(p.Text); Muted.Color = C(p.Muted);
        Accent.Color = C(p.Accent); OnAccent.Color = C(p.OnAccent); Good.Color = C(p.Good); GoodBg.Color = C(p.GoodBg);
        Bad.Color = C(p.Bad); BadBg.Color = Mix(p.Bad, p.Surface, 0.14); Warn.Color = p.IsDark ? C("#F5B942") : C("#9A5B00");
        Line.Color = C(p.Line); Track.Color = C(p.Track); AccentSoft.Color = Mix(p.Accent, p.Surface, 0.16);
        Changed?.Invoke();
    }

    public static Color C(string hex)
    {
        var (r, g, b) = Contrast.Parse(hex);
        return ColorHelper.FromArgb(255, r, g, b);
    }

    static Color Mix(string a, string b, double t)
    {
        var (ar, ag, ab) = Contrast.Parse(a);
        var (br, bg, bb) = Contrast.Parse(b);
        byte L(byte x, byte y) => (byte)Math.Round(y + (x - y) * t);
        return ColorHelper.FromArgb(255, L(ar, br), L(ag, bg), L(ab, bb));
    }

    public static ElementTheme ElementTheme => Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
}
