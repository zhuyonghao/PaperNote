using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace PaperNote.Views;
internal static class AppTheme
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["Paper-yellow"] = ("#FFFCF4", "#2C2924"),
        ["Paper-green"] = ("#F8FBF5", "#252D28"),
        ["Paper-blue"] = ("#F6F9FE", "#242B35"),
        ["Paper-pink"] = ("#FEF7F8", "#30262C"),
        ["Paper-purple"] = ("#FAF7FE", "#2B2735"),
        ["Paper-white"] = ("#FFFFFF", "#24252F"),
        ["Text"] = ("#292833", "#EEEFF5"),
        ["Muted"] = ("#71717E", "#B4B5C3"),
        ["Faint"] = ("#868591", "#9293A5"),
        ["Canvas"] = ("#F8F8FA", "#191A21"),
        ["Surface"] = ("#FFFFFF", "#24252F"),
        ["Chrome"] = ("#F1F1F5", "#1E1F28"),
        ["Border"] = ("#E6E5EC", "#363743"),
        ["Hover"] = ("#EAE9F1", "#2D2E3B"),
        ["Selected"] = ("#E9E5F6", "#35304D"),
        ["Accent"] = ("#7460B6", "#BEACEF"),
        ["AccentHover"] = ("#6551A4", "#D0BFFA"),
        ["AccentPressed"] = ("#584591", "#AE98E5"),
        ["Danger"] = ("#AC5148", "#EF9A8D"),
        ["HoverOverlay"] = ("#10000000", "#18FFFFFF"),
        ["White"] = ("#FFFFFF", "#FFFFFF")
    };
    private static readonly Dictionary<string, SolidColorBrush> Brushes = [];
    private static readonly Dictionary<string, string> DarkPaper = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#FFF6CF"] = "#363325", ["#EAF1DD"] = "#2D3828", ["#E6EFF8"] = "#283440",
        ["#F9E5E7"] = "#3E2D32", ["#EEE8F6"] = "#342E40", ["#F7F6F2"] = "#24252F"
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
