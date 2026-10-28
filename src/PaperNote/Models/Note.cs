using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace PaperNote.Models;
public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Markdown { get; set; } = "";
    public string Color { get; set; } = "yellow";
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
    public DesktopNoteState Desktop { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public string Title => PlainLines.FirstOrDefault() ?? "未命名便签";
    [JsonIgnore] public string Preview => string.Join("  ", PlainLines.Skip(1)).Trim() is { Length: > 0 } p ? p : "写下此刻的想法…";
    [JsonIgnore] private IEnumerable<string> PlainLines => Markdown.Split('\n')
        .Select(l => Regex.Replace(l, @"^\s*(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s*)?|>\s*|\d+\.\s*)", ""))
        .Select(l => Regex.Replace(l, @"[\[\]`*_~]", "").Trim()).Where(l => l.Length > 0);
}

public sealed class DesktopNoteState
{
    public bool HasBeenPlaced { get; set; }
    public bool IsOpen { get; set; }
    public bool Topmost { get; set; }
    public bool Collapsed { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 380;
    public double Opacity { get; set; } = 1;
    public double FontSize { get; set; } = 15;
    public bool ReadOnly { get; set; }
    public bool ShowFormatting { get; set; }
}

public sealed record NoteColor(string Key, string Name, string Paper, string Dot);
public static class NoteColors
{
    public static readonly NoteColor[] All = [
        new("yellow", "奶油黄", "#FFF6CF", "#E9C65C"),
        new("green", "鼠尾草", "#EAF1DD", "#A6BA83"),
        new("blue", "雾霭蓝", "#E6EFF8", "#91B2D2"),
        new("pink", "蔷薇粉", "#F9E5E7", "#DDA2AF"),
        new("purple", "香芋紫", "#EEE8F6", "#B3A0CE"),
        new("white", "月光白", "#F7F6F2", "#C9C5BA")
    ];
    public static NoteColor Get(string key) => All.FirstOrDefault(c => c.Key == key) ?? All[0];
}
