using Avalonia;
using Avalonia.Controls;

namespace PaperNote.Views;

// Arrange columns directly instead of wrapping fixed widths plus trailing margins.
internal sealed class LibraryCardsPanel : Panel
{
    public static readonly StyledProperty<bool> ListViewProperty =
        AvaloniaProperty.Register<LibraryCardsPanel, bool>(nameof(ListView));
    public bool ListView { get => GetValue(ListViewProperty); set => SetValue(ListViewProperty, value); }
    private bool IsEmpty => Children.Count == 1 && Equals(Children[0].Tag, "empty");
    private double Gap => ListView ? 8 : 16;

    static LibraryCardsPanel() => AffectsMeasure<LibraryCardsPanel>(ListViewProperty);

    private (int Columns, double Width) GetColumns(double available)
    {
        var columns = ListView || IsEmpty ? 1 : Math.Max(1, (int)Math.Floor((available + Gap) / (244 + Gap)));
        return (columns, Math.Max(0, (available - (columns - 1) * Gap) / columns));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var available = double.IsFinite(availableSize.Width) ? availableSize.Width : 800;
        var (columns, width) = GetColumns(available);
        var height = 0d;
        for (var first = 0; first < Children.Count; first += columns)
        {
            var rowHeight = 0d;
            for (var i = first; i < Math.Min(first + columns, Children.Count); i++)
            {
                Children[i].Measure(new Size(width, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            }
            height += rowHeight + (first > 0 ? Gap : 0);
        }
        return new Size(available, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = GetColumns(finalSize.Width);
        var y = 0d;
        for (var first = 0; first < Children.Count; first += columns)
        {
            var rowHeight = 0d;
            for (var i = first; i < Math.Min(first + columns, Children.Count); i++)
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            for (var i = first; i < Math.Min(first + columns, Children.Count); i++)
                Children[i].Arrange(new Rect((i - first) * (width + Gap), y, width, rowHeight));
            y += rowHeight + Gap;
        }
        return finalSize;
    }
}
