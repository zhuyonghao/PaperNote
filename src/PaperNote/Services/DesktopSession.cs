using PaperNote.Models;
namespace PaperNote.Services;

public static class DesktopSession
{
    public static IReadOnlyList<Note> StartupNotes(IEnumerable<Note> notes)
    {
        var all = notes.ToList();
        var open = all.Where(n => !n.IsDeleted && n.Desktop.IsOpen).ToList();
        if (open.Count > 0 || all.Any(n => n.Desktop.HasBeenPlaced)) return open;
        // Existing collections migrate without opening every archived note onto the desktop.
        var first = all.Where(n => !n.IsDeleted && !string.IsNullOrWhiteSpace(n.Markdown))
            .OrderByDescending(n => n.UpdatedAt).FirstOrDefault();
        return first is null ? [] : [first];
    }
}
