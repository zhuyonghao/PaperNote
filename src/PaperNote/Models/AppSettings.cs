namespace PaperNote.Models;

[Flags]
public enum ShortcutModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Meta = 8 }
public sealed record ShortcutSpec(string Key, ShortcutModifiers Modifiers)
{
    public static ShortcutSpec Default => new("N", OperatingSystem.IsMacOS() ? ShortcutModifiers.Meta | ShortcutModifiers.Alt : ShortcutModifiers.Control | ShortcutModifiers.Alt);
    public static ShortcutSpec VisibilityDefault => new("H", Default.Modifiers);
    public string Display(bool macOS) => string.Join(macOS ? " " : " + ", Parts(macOS));
    private IEnumerable<string> Parts(bool mac)
    {
        if (Modifiers.HasFlag(ShortcutModifiers.Control)) yield return mac ? "⌃" : "Ctrl";
        if (Modifiers.HasFlag(ShortcutModifiers.Alt)) yield return mac ? "⌥" : "Alt";
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) yield return mac ? "⇧" : "Shift";
        if (Modifiers.HasFlag(ShortcutModifiers.Meta)) yield return mac ? "⌘" : "Win";
        yield return Key == "Space" ? "Space" : Key;
    }
    public string? Validate()
    {
        if ((Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Meta)) == 0)
            return "请至少包含 Command / Win、Control 或 Option / Alt 中的一个。";
        if ((Modifiers & ~(ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift | ShortcutModifiers.Meta)) != 0)
            return "快捷键包含不支持的修饰键。";
        if (string.IsNullOrEmpty(Key) || Key != "Space" && (Key.Length != 1 || !char.IsAsciiLetterOrDigit(Key[0])))
            return "请使用字母、数字或空格作为主键。";
        return null;
    }
}
public sealed class AppSettings
{
    public string ThemeMode { get; set; } = "system";
    public bool GlobalShortcutEnabled { get; set; } = true;
    public ShortcutSpec NewNoteShortcut { get; set; } = ShortcutSpec.Default;
    public bool VisibilityShortcutEnabled { get; set; } = true;
    public ShortcutSpec VisibilityShortcut { get; set; } = ShortcutSpec.VisibilityDefault;
    public bool RestoreDesktop { get; set; } = true;
    public string DefaultColor { get; set; } = "yellow";
}
