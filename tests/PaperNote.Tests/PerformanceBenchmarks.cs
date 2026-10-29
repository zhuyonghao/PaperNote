using System.Diagnostics;
using System.Text.Json;
using PaperNote.Models;
using PaperNote.Services;

internal static class PerformanceBenchmarks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "papernote-benchmark-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var markdown = "# 性能测试\n\n" + string.Concat(Enumerable.Repeat("- [ ] 一段正文 **bold** 和 `code` 📝\n", 2000));
            var note = new Note { Markdown = markdown };
            Measure("title: 200 reads of a 2000-line note", () =>
            {
                for (var i = 0; i < 200; i++) GC.KeepAlive(note.Title);
            });
            Measure("preview: 20 reads of a 2000-line note", () =>
            {
                for (var i = 0; i < 20; i++) GC.KeepAlive(note.Preview);
            });
            Measure("card preview: 20 bounded reads of a 2000-line note", () =>
            {
                for (var i = 0; i < 20; i++) GC.KeepAlive(note.GetPreview(320));
            });

            var path = Path.Combine(directory, "notes.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Enumerable.Range(0, 100)
                .Select(_ => new Note { Markdown = markdown })));
            Measure("load: 100 notes with 2000 lines each", () =>
            {
                using var loaded = new NoteStore(path);
                GC.KeepAlive(loaded.Notes);
            });
            using var store = new NoteStore(path);
            Measure("save: 100 notes with backup", () =>
            {
                store.Update(store.Notes[0], n => n.IsPinned = !n.IsPinned);
                if (!store.Save()) throw new IOException(store.Error);
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Measure(string name, Action action)
    {
        action(); // Warm serializers, regexes, JIT and buffer pools before measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"{name}: {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} allocated bytes");
    }
}
