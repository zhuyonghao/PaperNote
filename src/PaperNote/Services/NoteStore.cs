using System.Text.Json;
using System.Text;
using PaperNote.Models;

namespace PaperNote.Services;

// One store is shared by the library and all detached notes. Writes are atomic and serialized.
public sealed class NoteStore : IDisposable
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _dirty;
    public List<Note> Notes { get; private set; } = [];
    public string? Error { get; private set; }
    public event Action<Guid?>? Changed;
    public event Action<Guid>? DesktopChanged;
    public event Action? SaveStateChanged;
    public bool IsDirty { get { lock (_gate) return _dirty; } }
    public string DataPath => _path;

    public NoteStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperNote", "notes.json");
        _timer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }
    private void Load()
    {
        if (!File.Exists(_path))
        {
            Notes = SeedNotes();
            ScheduleSave();
            return;
        }
        try
        {
            using var stream = File.OpenRead(_path);
            Notes = ReadNotes(stream) ?? throw new JsonException("便签文件内容为空");
            var ids = new HashSet<Guid>();
            foreach (var note in Notes)
            {
                if (note is null || note.Markdown is null || !ids.Add(note.Id))
                    throw new JsonException("便签文件格式无效");
                note.Desktop ??= new DesktopNoteState();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preserve a damaged source before allowing a new collection to be saved.
            try { File.Copy(_path, _path + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss")); }
            catch { Error = "便签文件无法读取且无法备份，请先检查文件权限。原文件未改动。"; return; }
            Error = "原数据无法读取，已保留 .damaged 备份。可从 notes.json.bak 手动恢复。";
            Notes = [];
        }
    }
    private static List<Note>? ReadNotes(FileStream stream)
    {
        // Match ReadAllText's BOM handling without allocating the whole JSON as
        // a UTF-16 string. Normal app saves use UTF-8 and need no transcoding.
        Span<byte> header = stackalloc byte[4];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        (Encoding? encoding, int preambleLength) = header[..read] switch
        {
            [0, 0, 0xfe, 0xff] => (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4),
            [0xff, 0xfe, 0, 0] => (Encoding.UTF32, 4),
            [0xff, 0xfe, ..] => (Encoding.Unicode, 2),
            [0xfe, 0xff, ..] => (Encoding.BigEndianUnicode, 2),
            [0xef, 0xbb, 0xbf, ..] => (null, 3),
            _ => (null, 0)
        };
        stream.Position = preambleLength;
        if (encoding is null) return JsonSerializer.Deserialize<List<Note>>(stream);
        using var utf8 = Encoding.CreateTranscodingStream(stream, encoding, Encoding.UTF8, leaveOpen: true);
        return JsonSerializer.Deserialize<List<Note>>(utf8);
    }
    public Note Add(string markdown = "", string color = "yellow")
    {
        var note = new Note { Markdown = markdown, Color = color };
        lock (_gate) Notes.Add(note);
        Touch(note);
        return note;
    }
    public void UpdateMarkdown(Guid id, string markdown)
    {
        var note = Notes.FirstOrDefault(n => n.Id == id);
        if (note is null || note.IsDeleted || note.Markdown == markdown) return;
        lock (_gate) note.Markdown = markdown;
        Touch(note);
    }
    public void Update(Note note, Action<Note> change)
    {
        lock (_gate) change(note);
        Touch(note);
    }
    public bool DiscardEmpty(Note note)
    {
        if (note.IsDeleted || !string.IsNullOrWhiteSpace(note.Markdown)) return false;
        lock (_gate) { if (!Notes.Remove(note)) return false; }
        ScheduleSave(); Changed?.Invoke(note.Id); return true;
    }
    public void RemovePermanently(Note note)
    {
        if (!note.IsDeleted) return;
        lock (_gate) Notes.Remove(note);
        ScheduleSave();
        Changed?.Invoke(note.Id);
    }
    public void UpdateDesktop(Note note, Action<DesktopNoteState> change)
    {
        lock (_gate) change(note.Desktop);
        ScheduleSave();
        DesktopChanged?.Invoke(note.Id);
    }
    private void Touch(Note note)
    {
        lock (_gate) note.UpdatedAt = DateTimeOffset.Now;
        ScheduleSave();
        Changed?.Invoke(note.Id);
    }
    private void ScheduleSave()
    {
        bool notify;
        lock (_gate) { notify = !_dirty; _dirty = true; }
        _timer.Change(400, Timeout.Infinite);
        if (notify) SaveStateChanged?.Invoke();
    }
    public bool Save()
    {
        lock (_gate)
        {
            if (!_dirty) return Error is null;
            // If loading could not preserve the source, don't overwrite it.
            if (Error?.Contains("无法备份") == true) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using (var stream = File.Create(_path + ".tmp"))
                    JsonSerializer.Serialize(stream, Notes, JsonOptions);
                if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
                File.Move(_path + ".tmp", _path, true);
                _dirty = false;
                Error = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Error = "保存失败：" + ex.Message; }
        }
        SaveStateChanged?.Invoke();
        return Error is null;
    }
    public void Dispose() { _timer.Dispose(); Save(); }

    private static List<Note> SeedNotes() => [
        new() { IsPinned = true, Markdown = "# 留一点空间，给灵感。\n\n那些突然闪过的念头，值得被好好留下。\n\n这是你的第一张便签。在这里，文字可以轻一点，想法可以自由一点。\n\n## 今天的小计划\n\n- [x] 为自己泡一杯咖啡\n- [ ] 记下一个有意思的想法\n- [ ] 留出半小时，做喜欢的事\n\n> 不必把每一天都安排得满满当当。\n\n试着输入 **粗体**、*斜体*，或用 `#` 加空格写一个标题。", Color = "yellow", UpdatedAt = DateTimeOffset.Now.AddMinutes(-3) },
        new() { Markdown = "# 周末，去走走\n\n城市的另一面，藏在慢下来的时候。\n\n- 去河边散步\n- 逛一家没去过的书店\n- 买一束新鲜的花\n\n不设目的地，也很好。", Color = "green", UpdatedAt = DateTimeOffset.Now.AddHours(-1) },
        new() { Markdown = "# 一些设计碎片\n\n好的设计，是让复杂的事情变得自然。\n\n## 最近的观察\n\n- 留白也是一种表达\n- 细节里藏着温度\n- 让每一次操作都有回应", Color = "blue", UpdatedAt = DateTimeOffset.Now.AddHours(-3) },
        new() { Markdown = "# 下次想读的书\n\n- [ ] 《一个人的好天气》\n- [ ] 《也许你该找个人聊聊》\n- [x] 《悉达多》\n\n慢慢读，不着急。", Color = "pink", UpdatedAt = DateTimeOffset.Now.AddDays(-1) },
        new() { Markdown = "# Markdown 小抄\n\n输入后按空格，格式自然出现。\n\n## 常用格式\n\n- `#` 一级标题\n- `##` 二级标题\n- `-` 无序列表\n- `1.` 有序列表\n- `[]` 待办事项\n- `>` 引用\n\n选中文字后，也可以使用上方的格式工具。\n\n```csharp\nvar idea = \"让灵感随手可得\";\n```", Color = "purple", UpdatedAt = DateTimeOffset.Now.AddDays(-2) }
    ];
}
