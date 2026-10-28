using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace PaperNote.Views;
internal static class UI
{
    public static WindowIcon AppIcon() => new(Avalonia.Platform.AssetLoader.Open(new Uri("avares://PaperNote/Assets/icon.png")));
    public static bool FindAncestorButton(this Control control) => control.GetVisualAncestors().OfType<Button>().Any();
    public static IBrush Brush(string color) => AppTheme.Brush(color);
    public static TextBlock Text(string text, double size = 13, string color = "Text", FontWeight? weight = null) =>
        new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center };
    public static StackPanel Stack(Orientation orientation, double spacing = 0) => new() { Orientation = orientation, Spacing = spacing };
    public static Border Line() => new() { Background = Brush("Border"), Height = 1 };
    public static Control Icon(string name, string color = "Muted", double size = 17)
    {
        var path = name switch {
            "document" => "M6 3H14L19 8V21H5V3Z M14 3V9H19 M8 13H16 M8 17H14",
            "folder" => "M3 6H9L11 8H21V20H3Z M3 8V4H9L11 6H20V8",
            "save" => "M4 3H17L21 7V21H3V3Z M7 3V9H16V3 M7 21V13H17V21",
            "plus" => "M12 5V19 M5 12H19",
            "notes" => "M5 3H19V21H5Z M8 8H16 M8 12H16 M8 16H13",
            "pin" => "M8 3H16L15 10L19 14H5L9 10Z M12 14V22",
            "trash" => "M3 6H21 M8 6V3H16V6 M5 6L6 21H18L19 6 M10 10V17 M14 10V17",
            "search" => "M20 20L15 15 M17 10A7 7 0 1 1 3 10A7 7 0 1 1 17 10",
            "export" => "M12 16V2 M7 7L12 2L17 7 M4 13V21H20V13",
            "import" => "M12 2V16 M7 11L12 16L17 11 M4 17V22H20V17",
            "pop" => "M14 3H21V10 M21 3L11 13 M10 5H3V21H19V14",
            "code" => "M8 5L2 12L8 19 M16 5L22 12L16 19 M14 2L10 22",
            "check" => "M4 12L9 17L20 6",
            "restore" => "M3 3V9H9 M3 9A9 9 0 1 1 4 17",
            "arrow" => "M5 12H19 M14 7L19 12L14 17",
            "book" => "M3 4H9L12 7L15 4H21V20H15L12 22L9 20H3Z M12 7V22",
            "close" => "M6 6L18 18 M18 6L6 18",
            "more" => "M5 11.5H5.5V12H5Z M11.75 11.5H12.25V12H11.75Z M18.5 11.5H19V12H18.5Z",
            "format" => "M4 5H20 M12 5V20 M8 20H16",
            "collapse" => "M5 7H19 M8 15L12 11L16 15",
            "settings" => "M4 7H20 M4 17H20 M8 4V10 M16 14V20",
            _ => "M5 5H19V19H5Z"
        };
        return new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = Brush(color), StrokeThickness = 1.55,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Width = size, Height = name == "more" ? 3 : size, VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Uniform };
    }
    public static Button IconButton(string icon, string tip, Action click)
    {
        var b = new Button { Content = Icon(icon), Classes = { "quiet" }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip);
        Avalonia.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }
    public static Border Pill(string label) => new() { Child = Text(label, 10, "Faint"),
        CornerRadius = new CornerRadius(4), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), Padding = new Thickness(5, 2) };
}
