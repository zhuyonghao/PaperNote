using PaperNote.Models;

namespace PaperNote.Services;

public readonly record struct DesktopWorkArea(int X, int Y, int Width, int Height, double Scale);
public readonly record struct DesktopPlacement(int X, int Y, double Width, double Height)
{
    // Geometry is stored in physical pixels for position and device-independent units for size.
    public static DesktopPlacement Resolve(DesktopNoteState state, IReadOnlyList<DesktopWorkArea> screens, int offset = 0)
    {
        if (screens.Count == 0) return new(100, 100, 340, 380);
        var screen = screens.FirstOrDefault(s => state.X >= s.X && state.X < s.X + s.Width && state.Y >= s.Y && state.Y < s.Y + s.Height);
        if (screen.Width <= 0) screen = screens[0];
        var scale = double.IsFinite(screen.Scale) && screen.Scale > 0 ? screen.Scale : 1;
        var maxWidth = screen.Width / scale;
        var maxHeight = screen.Height / scale;
        var width = Math.Clamp(double.IsFinite(state.Width) ? state.Width : 340, Math.Min(280, maxWidth), maxWidth);
        var height = Math.Clamp(double.IsFinite(state.Height) ? state.Height : 380, Math.Min(220, maxHeight), maxHeight);
        var x = state.X ?? screen.X + (int)((70 + offset * 28) * scale);
        var y = state.Y ?? screen.Y + (int)((70 + offset * 28) * scale);
        x = Math.Clamp(x, screen.X, screen.X + Math.Max(0, screen.Width - (int)Math.Ceiling(width * scale)));
        y = Math.Clamp(y, screen.Y, screen.Y + Math.Max(0, screen.Height - (int)Math.Ceiling(height * scale)));
        return new(x, y, width, height);
    }
}
