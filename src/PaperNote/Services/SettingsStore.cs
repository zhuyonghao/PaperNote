using System.Text.Json;
using PaperNote.Models;
namespace PaperNote.Services;

public sealed class SettingsStore
{
    private readonly string _path;
    public AppSettings Current { get; private set; } = new();
    public string? Error { get; private set; }
    public SettingsStore(string directory)
    {
        _path = Path.Combine(directory, "settings.json");
        if (!File.Exists(_path)) return;
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
            if (Current.ThemeMode is not ("system" or "light" or "dark")) Current.ThemeMode = "system";
            if (Current.NewNoteShortcut is null || Current.NewNoteShortcut.Validate() is not null) Current.NewNoteShortcut = ShortcutSpec.Default;
            if (Current.VisibilityShortcut is null || Current.VisibilityShortcut.Validate() is not null) Current.VisibilityShortcut = ShortcutSpec.VisibilityDefault;
            if (!NoteColors.All.Any(c => c.Key == Current.DefaultColor)) Current.DefaultColor = "yellow";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "设置文件无法读取：" + ex.Message;
            try { File.Copy(_path, _path + ".damaged-" + DateTimeOffset.Now.ToUnixTimeMilliseconds()); }
            catch { Error = "设置文件无法读取且无法备份，暂不覆盖原文件。"; }
        }
    }
    public bool Save()
    {
        if (Error?.Contains("无法备份") == true) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, true); Error = null; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "设置保存失败：" + ex.Message; return false; }
    }
}
