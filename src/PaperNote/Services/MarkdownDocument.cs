using System.Security.Cryptography;
using System.Text;

namespace PaperNote.Services;

// File documents have an explicit save lifecycle, separate from auto-saved sticky notes.
public sealed class MarkdownDocument
{
    private string _savedContent = "";
    private byte[]? _savedHash;
    private bool _utf8Bom;
    public string Content { get; set; } = "";
    public string? FilePath { get; private set; }
    public bool CanCloseWithoutSaving => !IsDirty || FilePath is null && string.IsNullOrWhiteSpace(Content);
    public bool IsDirty => Content != _savedContent;
    public string Name => FilePath is null ? "未命名.md" : Path.GetFileName(FilePath);

    public void Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        // Commit only after the entire file has been read successfully.
        Content = _savedContent = content;
        FilePath = fullPath;
        _savedHash = SHA256.HashData(bytes);
        _utf8Bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
    }

    public void Save(string? path = null)
    {
        var destination = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("请先选择保存位置。"));
        if (destination == FilePath && _savedHash is not null &&
            (!File.Exists(destination) || !SHA256.HashData(File.ReadAllBytes(destination)).AsSpan().SequenceEqual(_savedHash)))
            throw new IOException("文件已被其他程序修改或移走。请使用“另存为”保留当前内容，或重新打开磁盘上的文件。");

        var encoding = new UTF8Encoding(_utf8Bom);
        var text = encoding.GetBytes(Content);
        var bytes = encoding.GetPreamble().Concat(text).ToArray();
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        FilePath = destination;
        _savedContent = Content;
        _savedHash = SHA256.HashData(bytes);
    }
}
