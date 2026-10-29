using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Text;

namespace PaperNote.Models;
public sealed partial class Note
{
    private string _markdown = "";
    private string? _title;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Markdown
    {
        get => _markdown;
        set { _markdown = value; _title = null; }
    }
    public string Color { get; set; } = "yellow";
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }
    public DesktopNoteState Desktop { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore] public string Title => _title ??= PlainLines(int.MaxValue).FirstOrDefault() ?? "未命名便签";
    [JsonIgnore] public string Preview => GetPreview(int.MaxValue);

    // Cards only display a few lines. Stop reading once their text budget is full,
    // including for notes consisting of a single, very long paragraph.
    public string GetPreview(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        var preview = new StringBuilder(Math.Min(maxLength, 320));
        var skipTitle = true;
        foreach (var line in PlainLines(maxLength == int.MaxValue ? maxLength : maxLength + 1))
        {
            if (skipTitle) { skipTitle = false; continue; }
            var separator = preview.Length > 0 ? "  " : "";
            var remaining = maxLength - preview.Length;
            if ((long)separator.Length + line.Length > remaining)
            {
                preview.Append(separator.AsSpan(0, Math.Min(remaining, separator.Length)));
                remaining = maxLength - preview.Length;
                preview.Append(line.AsSpan(0, Math.Min(remaining, line.Length)));
                if (preview.Length > 0 && char.IsHighSurrogate(preview[^1])) preview.Length--;
                return preview.Append('…').ToString();
            }
            preview.Append(separator).Append(line);
        }
        const string placeholder = "写下此刻的想法…";
        return preview.Length > 0 ? preview.ToString()
            : placeholder.Length <= maxLength ? placeholder : placeholder[..maxLength] + "…";
    }

    private IEnumerable<string> PlainLines(int maxLength)
    {
        var start = 0;
        while (start < Markdown.Length)
        {
            var end = Markdown.IndexOf('\n', start);
            if (end < 0) end = Markdown.Length;
            var line = PlainLine(Markdown.AsSpan(start, end - start), maxLength);
            if (line.Length > 0) yield return line;
            start = end + 1;
        }
    }

    private static string PlainLine(ReadOnlySpan<char> line, int maxLength)
    {
        foreach (var match in LeadingMarkdown().EnumerateMatches(line))
        {
            line = line[match.Length..];
            break;
        }
        var text = new StringBuilder(Math.Min(line.Length, Math.Min(maxLength, 320)));
        var whitespaceStart = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character is '[' or ']' or '`' or '*' or '_' or '~') continue;
            if (char.IsWhiteSpace(character))
            {
                if (text.Length > 0 && whitespaceStart < 0) whitespaceStart = i;
                continue;
            }
            if (whitespaceStart >= 0)
            {
                // Keep internal whitespace exactly, without buffering long runs
                // of trailing whitespace that Trim would eventually discard.
                for (var j = whitespaceStart; j < i && text.Length < maxLength; j++)
                    if (char.IsWhiteSpace(line[j])) text.Append(line[j]);
                whitespaceStart = -1;
            }
            if (text.Length < maxLength) text.Append(character);
            if (text.Length >= maxLength) break;
        }
        return text.ToString();
    }

    [GeneratedRegex(@"^\s*(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s*)?|>\s*|\d+\.\s*)")]
    private static partial Regex LeadingMarkdown();
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
