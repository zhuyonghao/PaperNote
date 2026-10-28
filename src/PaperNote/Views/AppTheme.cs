using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PaperNote.Views;
internal static class AppTheme
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["Text"] = ("#354335", "#E2E7DD"),
        ["Muted"] = ("#718069", "#A5B09C"),
        ["Faint"] = ("#7D8975", "#96A18E"),
        ["Canvas"] = ("#FAFBF8", "#1C211C"),
        ["Surface"] = ("#FFFFFF", "#272E26"),
        ["Chrome"] = ("#F1F4ED", "#222920"),
        ["Border"] = ("#DFE5D8", "#3C4537"),
        ["Hover"] = ("#E8EEDF", "#35422F"),
        ["Selected"] = ("#DFE9D5", "#3E5033"),
        ["Accent"] = ("#647F53", "#91AD7D"),
        ["AccentHover"] = ("#536D44", "#A1BC8D"),
        ["AccentPressed"] = ("#465F38", "#7E9C69"),
        ["Danger"] = ("#AC5148", "#EF9A8D"),
        ["HoverOverlay"] = ("#146A7E55", "#287F9C69"),
        ["White"] = ("#FFFFFF", "#FFFFFF")
    };
    private static readonly Dictionary<string, SolidColorBrush> Brushes = [];
    private static readonly Dictionary<string, string> DarkPaper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFF6CF"] = "#363325", ["#EAF1DD"] = "#2D3828", ["#E6EFF8"] = "#283440",
        ["#F9E5E7"] = "#3E2D32", ["#EEE8F6"] = "#342E40", ["#F7F6F2"] = "#272E26"
    };
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    public static event Action? Changed;
    public static void Initialize(string mode)
    {
        Apply(mode);
        Application.Current!.ActualThemeVariantChanged += (_, _) => Refresh();
    }
    public static void Apply(string mode)
    {
        Application.Current!.RequestedThemeVariant = mode switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        Refresh();
    }
    private static void Refresh()
    {
        foreach (var (key, brush) in Brushes) brush.Color = Color.Parse(Resolve(key));
        Changed?.Invoke();
    }
    public static string Resolve(string key) => Palette.TryGetValue(key, out var pair) ? IsDark ? pair.Dark : pair.Light
        : IsDark && DarkPaper.TryGetValue(key, out var paper) ? paper : key;
    public static IBrush Brush(string key)
    {
        if (!Brushes.TryGetValue(key, out var brush)) Brushes[key] = brush = new SolidColorBrush(Color.Parse(Resolve(key)));
        return brush;
    }
}
