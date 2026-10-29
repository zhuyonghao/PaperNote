using Avalonia;
using Avalonia.Controls;
using PaperNote.Models;
using PaperNote.Services;
using PaperNote.Views;

if (args is ["--benchmark"])
{
    PerformanceBenchmarks.Run();
    return;
}

if (args.Length >= 2 && args[0] == "--instance-client")
{
    using var client = new SingleInstanceService(args[1]);
    Environment.ExitCode = client.IsPrimary ? 2 : await client.SendAsync(args.Length > 2 ? args[2..] : ["--new-note"]) ? 0 : 3;
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "papernote-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
try
{
    var summarySamples = new[] { "", " \r\n\t", "# 标题\r\n\r\n- [x] **正文** `code` 📝\r\n> 引用",
        "  ## 标题\n\t* _甲_\t　乙\r\n1. ~~丙~~", "# 标题\n甲\t*\t乙\n丙", "***\n[]\n`_~`\n+ 最后的标题", "标题\n甲" + new string(' ', 500) + "乙\n末行" };
    foreach (var markdown in summarySamples)
    {
        var lines = markdown.Split('\n')
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\s*(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s*)?|>\s*|\d+\.\s*)", ""))
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"[\[\]`*_~]", "").Trim()).Where(l => l.Length > 0).ToArray();
        var summaryNote = new Note { Markdown = markdown };
        var expected = string.Join("  ", lines.Skip(1));
        if (expected.Length == 0) expected = "写下此刻的想法…";
        Check(summaryNote.Title == (lines.FirstOrDefault() ?? "未命名便签") && summaryNote.Preview == expected,
            "streamed title and preview preserve existing Markdown stripping and whitespace");
        foreach (var length in new[] { 1, 8, 80, 320 })
            Check(summaryNote.GetPreview(length) == (expected.Length > length ? expected[..length] + "…" : expected),
                $"card previews stop at {length} characters without changing their prefix");
    }
    var cachedTitle = new Note { Markdown = "# 旧标题\n\n正文" };
    Check(cachedTitle.Title == "旧标题", "title cache starts with the current Markdown");
    cachedTitle.Markdown = "# 新标题\n\n正文";
    Check(cachedTitle.Title == "新标题", "changing Markdown invalidates the cached title");
    cachedTitle.Markdown = "标题\n" + new string('文', 4_000_000);
    _ = cachedTitle.Title; _ = cachedTitle.GetPreview(320);
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 100; i++) { _ = cachedTitle.Title; _ = cachedTitle.GetPreview(320); }
    Check(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore < 1_000_000,
        "repeated title and card preview reads do not allocate proportional to a 4 MB paragraph");
    cachedTitle.Markdown = "标题\n" + new string('文', 319) + "📝尾声";
    Check(cachedTitle.GetPreview(320) == new string('文', 319) + "…", "preview truncation keeps surrogate pairs intact");

    var random = new Random(73);
    const string characters = "# >-+_~[]`*\r\t abc文\n";
    var equivalentSummaries = true;
    for (var sample = 0; sample < 1000 && equivalentSummaries; sample++)
    {
        var markdown = new string(Enumerable.Range(0, 160).Select(_ => characters[random.Next(characters.Length)]).ToArray());
        var lines = markdown.Split('\n')
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\s*(#{1,6}\s+|[-*+]\s+(\[[ xX]\]\s*)?|>\s*|\d+\.\s*)", ""))
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"[\[\]`*_~]", "").Trim()).Where(l => l.Length > 0).ToArray();
        var expected = string.Join("  ", lines.Skip(1));
        if (expected.Length == 0) expected = "写下此刻的想法…";
        var summaryNote = new Note { Markdown = markdown };
        equivalentSummaries = summaryNote.Title == (lines.FirstOrDefault() ?? "未命名便签") && summaryNote.Preview == expected
            && summaryNote.GetPreview(24) == (expected.Length > 24 ? expected[..24] + "…" : expected);
    }
    Check(equivalentSummaries, "1000 mixed Markdown samples preserve title, preview and bounded prefixes");

    var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
    var selection = new LibrarySelection(); selection.SetVisible(ids);
    selection.Begin(); selection.Toggle(ids[1]); selection.Toggle(ids[4]);
    Check(selection.IsActive && selection.Count == 2 && selection.Contains(ids[1]) && selection.Contains(ids[4]), "clicking cards adds independent selections");
    selection.Toggle(ids[1]);
    Check(selection.Count == 1 && !selection.Contains(ids[1]), "clicking a selected card deselects only that note");
    selection.Toggle(Guid.NewGuid());
    Check(selection.Count == 1, "notes outside the visible results cannot be selected");
    selection.SelectAll();
    Check(selection.Count == 6, "select all selects exactly the visible notes");
    selection.SetVisible(ids.Reverse());
    Check(selection.Count == 6, "sorting or changing view preserves selection by note ID");
    selection.SetVisible([ids[1], ids[3]]);
    Check(selection.Count == 2 && !selection.Contains(ids[4]), "searching removes hidden notes from batch selection");
    selection.SetVisible(ids);
    Check(selection.Count == 2, "clearing search does not silently reselect hidden notes");
    selection.Clear();
    Check(selection.Count == 0 && selection.IsActive, "clearing selection keeps multi-select mode active");
    selection.SelectAll(); selection.End(); selection.Begin();
    Check(selection.Count == 0 && selection.IsActive, "finishing then reopening multi-select starts with no selection");
    selection.SelectAll(); selection.SetVisible(ids.Skip(2));
    Check(selection.Count == 4 && !selection.Contains(ids[0]) && selection.Contains(ids[2]), "a partial batch keeps unprocessed visible notes selected");
    selection.SetVisible([]); selection.SelectAll();
    Check(selection.Count == 0, "empty results have no actionable selection");

    foreach (var width in new double[] { 320, 503, 504, 763, 764, 864, 1156, 1500 })
    {
        var cards = new LibraryCardsPanel();
        for (var i = 0; i < 12; i++) cards.Children.Add(new Border { Height = 228 });
        cards.Measure(new Size(width, double.PositiveInfinity));
        cards.Arrange(new Rect(0, 0, width, cards.DesiredSize.Height));
        var expectedColumns = Math.Max(1, (int)Math.Floor((width + 16) / 260));
        var firstRow = cards.Children.Where(c => c.Bounds.Y == 0).ToArray();
        Check(firstRow.Length == expectedColumns && Math.Abs(firstRow[^1].Bounds.Right - width) < 1.01
            && cards.Children.All(c => c.Bounds.Right <= width + 1.01), $"cards fill {width}px viewport without losing the last column");
        cards.ListView = true; cards.Measure(new Size(width, double.PositiveInfinity));
        cards.Arrange(new Rect(0, 0, width, cards.DesiredSize.Height));
        Check(cards.Children.All(c => Math.Abs(c.Bounds.Width - width) < 1.01) && cards.Children[1].Bounds.Y > 0, $"list rows fill {width}px viewport after changing view");
    }
    var emptyPanel = new LibraryCardsPanel(); emptyPanel.Children.Add(new Border { Tag = "empty", Height = 200 });
    emptyPanel.Measure(new Size(864, double.PositiveInfinity)); emptyPanel.Arrange(new Rect(0, 0, 864, 200));
    Check(emptyPanel.Children[0].Bounds.Width == 864, "empty state spans the full card viewport");
    var path = Path.Combine(directory, "notes.json");
    Check(LaunchRequest.TryParse([], out var emptyLaunch) && emptyLaunch.Action == "--show", "empty launches preserve normal application activation");
    Check(LaunchRequest.TryParse(["--markdown"], out var editorLaunch) && editorLaunch.Action == "--markdown", "Markdown editor commands are valid single-instance requests");
    var fileWithSpaces = Path.Combine(directory, "中文 # 文档.md");
    Check(LaunchRequest.TryParse([fileWithSpaces], out var fileLaunch) && fileLaunch.Files.SequenceEqual([fileWithSpaces]), "shell file arguments preserve Unicode, spaces and hash characters");
    Check(LaunchRequest.TryParse([new Uri(fileWithSpaces).AbsoluteUri], out var uriLaunch) && uriLaunch.Files.SequenceEqual([fileWithSpaces]), "file URI activation decodes into a local document path");
    Check(LaunchRequest.TryParse(["--open", fileWithSpaces, fileWithSpaces], out var multipleLaunch) && multipleLaunch.Files.Count == 1 && multipleLaunch.ToArguments().SequenceEqual(["--open", fileWithSpaces]), "duplicate file requests normalize to one opened document");
    Check(LaunchRequest.TryParse(["relative.markdown"], out var relativeLaunch) && Path.IsPathFullyQualified(relativeLaunch.Files[0]), "relative document paths normalize before forwarding");
    Check(LaunchRequest.TryParse(["--", "-draft.md"], out _), "explicit path delimiter permits names beginning with a dash");
    Check(!LaunchRequest.TryParse(["--open"], out _) && !LaunchRequest.TryParse(["--unknown"], out _), "incomplete or unknown launch commands are rejected");
    Check(!LaunchRequest.TryParse(["https://example.com/document.md"], out _) && !LaunchRequest.TryParse([Path.Combine(directory, "program.exe")], out _), "launch messages only accept supported local document files");
    Check(!LaunchRequest.TryParse(Enumerable.Repeat(fileWithSpaces, 66).ToArray(), out _), "single-instance file requests retain a bounded argument count");
    var document = new MarkdownDocument();
    Check(!document.IsDirty && document.FilePath is null, "new Markdown documents begin clean and unbound");
    document.Content = "  \n\t";
    Check(document.IsDirty && document.CanCloseWithoutSaving, "whitespace-only untitled drafts close without a save prompt");
    document.Content = "draft";
    Check(!document.CanCloseWithoutSaving, "nonempty untitled drafts still require a save decision");
    var markdownPath = Path.Combine(directory, "中文文档.md");
    var originalMarkdown = "---\r\ntitle: 文档\r\n---\r\n\r\n# 标题 📝\r\n\r\n[^note]: 脚注  \r\n";
    File.WriteAllText(markdownPath, originalMarkdown, new System.Text.UTF8Encoding(true));
    document.Load(markdownPath);
    Check(document.Content == originalMarkdown && !document.IsDirty && document.Name == "中文文档.md", "opening Markdown preserves raw syntax, Unicode and CRLF");
    document.Content = "";
    Check(!document.CanCloseWithoutSaving, "clearing an existing file still requires a save decision");
    document.Content = "# 短文\r\n";
    Check(document.IsDirty, "editing a file marks it unsaved");
    document.Save();
    var savedBytes = File.ReadAllBytes(markdownPath);
    Check(File.ReadAllText(markdownPath) == document.Content && savedBytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) && !document.IsDirty && Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic document save truncates safely and preserves UTF-8 BOM");
    document.Content = "unsaved local edits";
    File.WriteAllText(markdownPath, "external edits");
    var conflict = false;
    try { document.Save(); } catch (IOException) { conflict = true; }
    Check(conflict && File.ReadAllText(markdownPath) == "external edits" && document.IsDirty, "external edits block overwrite without losing local changes");
    var copyPath = Path.Combine(directory, "copy.md");
    document.Save(copyPath);
    Check(document.FilePath == copyPath && !document.IsDirty && File.ReadAllText(copyPath) == "unsaved local edits", "Save As binds to the new path after a successful write");
    document.Content = "still unsaved";
    var failed = false;
    try { document.Save(Path.Combine(directory, "missing", "cannot-save.md")); } catch (IOException) { failed = true; }
    Check(failed && document.IsDirty && document.FilePath == copyPath && File.ReadAllText(copyPath) == "unsaved local edits", "failed Save As preserves the original binding and dirty state");
    var loadFailed = false;
    try { document.Load(Path.Combine(directory, "missing.md")); } catch (IOException) { loadFailed = true; }
    Check(loadFailed && document.Content == "still unsaved" && document.FilePath == copyPath, "failed open leaves the current document intact");
    document.Content = "unsaved local edits";
    Check(!document.IsDirty, "reverting to the saved content clears dirty state");
    File.Delete(copyPath);
    var removed = false;
    try { document.Save(); } catch (IOException) { removed = true; }
    Check(removed && !File.Exists(copyPath), "removed files are not silently recreated over an external change");
    Guid noteId;
    using (var store = new NoteStore(path))
    {
        var blank = store.Add(" \n\t");
        Check(store.DiscardEmpty(blank) && !store.Notes.Contains(blank), "empty desktop notes are discarded without entering trash");
        var retained = store.Add("keep this");
        Check(!store.DiscardEmpty(retained) && store.Notes.Contains(retained), "nonempty desktop notes cannot be discarded by the empty-note path");
        store.Update(retained, n => { n.Markdown = ""; n.IsDeleted = true; });
        Check(!store.DiscardEmpty(retained), "empty notes already in trash remain recoverable");
        Check(store.Notes.Count >= 1, "first launch seeds welcome notes");
        var note = store.Add("# 测试中文\n\n- [ ] task\n\n**bold** and `code`", "blue"); noteId = note.Id;
        Check(note.Title == "测试中文", "heading becomes note title");
        store.Update(note, n => n.IsPinned = true);
        Check(store.Save() && File.Exists(path), "atomic save succeeds");
        store.UpdateMarkdown(noteId, "# 已更新\n\n文本 📝"); store.Save();
        Check(File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "backup retained and no partial write remains");
        store.Update(note, n => n.IsDeleted = true);
        store.UpdateMarkdown(noteId, "SHOULD NOT WRITE");
        Check(note.Markdown.Contains("已更新"), "deleted notes reject editor writes");
        store.Update(note, n => n.IsDeleted = false);
        store.RemovePermanently(note);
        Check(store.Notes.Contains(note), "permanent delete requires trash state");
    }
    using (var reopened = new NoteStore(path))
    {
        var note = reopened.Notes.Single(n => n.Id == noteId);
        Check(note.Markdown == "# 已更新\n\n文本 📝" && note.IsPinned && note.Color == "blue", "restart preserves Markdown, Unicode, pin and color");
        reopened.Update(note, n => n.IsDeleted = true); reopened.RemovePermanently(note); reopened.Save();
    }
    using (var reopened = new NoteStore(path)) Check(reopened.Notes.All(n => n.Id != noteId), "permanent deletion survives restart");
    var debounce = Path.Combine(directory, "debounced.json");
    using (var store = new NoteStore(debounce))
    {
        var n = store.Add("one");
        store.Save();
        var saveNotifications = 0;
        store.SaveStateChanged += () => saveNotifications++;
        for (var i = 0; i < 30; i++) store.UpdateMarkdown(n.Id, "version " + i);
        Check(saveNotifications == 1, "typing a burst only notifies save status once until the state changes");
        await Task.Delay(700);
        Check(File.Exists(debounce) && File.ReadAllText(debounce).Contains("version 29") && !store.IsDirty, "autosave persists latest edit after debounce");
        Check(saveNotifications == 2, "autosave notifies when the store becomes clean again");
    }
    var broken = Path.Combine(directory, "broken.json"); File.WriteAllText(broken, "{ damaged original");
    using (var store = new NoteStore(broken))
    {
        Check(store.Error is not null && Directory.GetFiles(directory, "broken.json.damaged-*").Length == 1, "invalid data is preserved with visible error");
        Check(File.ReadAllText(broken) == "{ damaged original", "loading never overwrites damaged source");
    }
    foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode, System.Text.Encoding.UTF32 })
    {
        var encodedPath = Path.Combine(directory, "encoded-" + encoding.CodePage + ".json");
        File.WriteAllText(encodedPath, "[{\"Markdown\":\"# 带 BOM 的便签 📝\"}]", encoding);
        using var encodedStore = new NoteStore(encodedPath);
        Check(encodedStore.Error is null && encodedStore.Notes.Single().Title == "带 BOM 的便签 📝",
            $"streamed JSON loading preserves BOM encoding {encoding.WebName}");
    }
    var desktopPath = Path.Combine(directory, "desktop.json");
    Guid desktopId;
    DateTimeOffset originalDate;
    using (var store = new NoteStore(desktopPath))
    {
        var note = store.Add("# 桌面上的想法"); desktopId = note.Id; originalDate = note.UpdatedAt;
        store.UpdateDesktop(note, d => { d.IsOpen = true; d.HasBeenPlaced = true; d.X = -1400; d.Y = 160; d.Width = 315; d.Height = 420; d.Topmost = true; d.Collapsed = true; d.Opacity = .8; d.FontSize = 17; d.ShowFormatting = true; d.ReadOnly = true; });
        Check(note.UpdatedAt == originalDate, "moving or configuring a sticky does not change content modification time");
        store.Save();
    }
    using (var store = new NoteStore(desktopPath))
    {
        var note = store.Notes.Single(n => n.Id == desktopId); var state = note.Desktop;
        Check(state.IsOpen && state.X == -1400 && state.Y == 160 && state.Width == 315 && state.Height == 420 && state.Topmost && state.Collapsed && state.Opacity == .8 && state.FontSize == 17 && state.ShowFormatting && state.ReadOnly, "desktop position, size, pin, collapse, opacity and formatting survive restart");
        Check(DesktopSession.StartupNotes(store.Notes).Select(n => n.Id).SequenceEqual([desktopId]), "startup restores exactly the open desktop notes");
        store.UpdateDesktop(note, d => d.IsOpen = false);
        Check(DesktopSession.StartupNotes(store.Notes).Count == 0, "explicitly closed notes stay in the library on restart");
        store.UpdateDesktop(note, d => d.IsOpen = true); store.Update(note, n => n.IsDeleted = true);
        Check(DesktopSession.StartupNotes(store.Notes).Count == 0, "deleted desktop notes never reopen");
    }
    var legacy = Path.Combine(directory, "legacy.json");
    File.WriteAllText(legacy, "[{\"Markdown\":\"legacy note\"}]");
    using (var store = new NoteStore(legacy))
    {
        Check(store.Notes[0].Desktop is not null && store.Notes[0].Markdown == "legacy note", "older note collections gain desktop defaults without losing content");
        Check(DesktopSession.StartupNotes(store.Notes).Count == 1, "migration opens one existing note rather than the whole collection");
    }
    var screen = new DesktopWorkArea(0, 30, 2880, 1700, 2);
    var placement = DesktopPlacement.Resolve(new DesktopNoteState { X = 9000, Y = -9000, Width = 6000, Height = 6000 }, [screen]);
    Check(placement.X >= 0 && placement.Y >= 30 && placement.X + placement.Width * 2 <= 2880 && placement.Y + placement.Height * 2 <= 1730, "disconnected monitor and oversized windows recover inside the current high-DPI screen");
    var external = new DesktopWorkArea(-1920, 0, 1920, 1080, 1);
    var onSecond = DesktopPlacement.Resolve(new DesktopNoteState { X = -1400, Y = 160, Width = 315, Height = 420 }, [screen, external]);
    Check(onSecond.X == -1400 && onSecond.Y == 160 && onSecond.Width == 315, "valid negative coordinates on a second monitor are preserved");
    var preferences = new SettingsStore(Path.Combine(directory, "preferences"));
    Check(preferences.Current.NewNoteShortcut.Validate() is null, "default global shortcut is valid");
    Check(preferences.Current.VisibilityShortcut.Validate() is null && preferences.Current.VisibilityShortcut != preferences.Current.NewNoteShortcut, "visibility shortcut has a valid independent default");
    Check(new ShortcutSpec("N", ShortcutModifiers.None).Validate() is not null, "bare keys cannot be registered globally");
    Check(new ShortcutSpec("F12", ShortcutModifiers.Control).Validate() is not null, "unsupported keys are rejected");
    preferences.Current.NewNoteShortcut = new("K", ShortcutModifiers.Control | ShortcutModifiers.Alt);
    preferences.Current.VisibilityShortcut = new("J", ShortcutModifiers.Control | ShortcutModifiers.Shift);
    preferences.Current.VisibilityShortcutEnabled = false;
    preferences.Current.GlobalShortcutEnabled = false; preferences.Current.RestoreDesktop = false; preferences.Current.DefaultColor = "green"; preferences.Current.ThemeMode = "dark";
    Check(preferences.Save(), "application preferences save atomically");
    var loadedPreferences = new SettingsStore(Path.Combine(directory, "preferences"));
    Check(loadedPreferences.Current.VisibilityShortcut == preferences.Current.VisibilityShortcut && !loadedPreferences.Current.VisibilityShortcutEnabled, "custom visibility shortcut and enabled state survive restart");
    Check(loadedPreferences.Current.NewNoteShortcut == preferences.Current.NewNoteShortcut && !loadedPreferences.Current.GlobalShortcutEnabled && !loadedPreferences.Current.RestoreDesktop && loadedPreferences.Current.DefaultColor == "green" && loadedPreferences.Current.ThemeMode == "dark", "custom shortcut and desktop preferences survive restart");
    var malformedDirectory = Path.Combine(directory, "bad-preferences"); Directory.CreateDirectory(malformedDirectory);
    var oldSettingsDirectory = Path.Combine(directory, "old-settings"); Directory.CreateDirectory(oldSettingsDirectory);
    File.WriteAllText(Path.Combine(oldSettingsDirectory, "settings.json"), "{\"NewNoteShortcut\":{\"Key\":\"K\",\"Modifiers\":3}}");
    var migratedSettings = new SettingsStore(oldSettingsDirectory);
    Check(migratedSettings.Current.NewNoteShortcut.Key == "K" && migratedSettings.Current.VisibilityShortcut == ShortcutSpec.VisibilityDefault && migratedSettings.Current.VisibilityShortcutEnabled, "older preferences gain visibility shortcut without losing existing shortcut");
    File.WriteAllText(Path.Combine(oldSettingsDirectory, "settings.json"), "{\"VisibilityShortcut\":null}");
    Check(new SettingsStore(oldSettingsDirectory).Current.VisibilityShortcut == ShortcutSpec.VisibilityDefault, "null visibility shortcut falls back safely");
    File.WriteAllText(Path.Combine(oldSettingsDirectory, "settings.json"), "{\"VisibilityShortcut\":{\"Key\":\"F12\",\"Modifiers\":1}}");
    Check(new SettingsStore(oldSettingsDirectory).Current.VisibilityShortcut == ShortcutSpec.VisibilityDefault, "invalid visibility shortcut falls back safely");
    File.WriteAllText(Path.Combine(malformedDirectory, "settings.json"), "{broken");
    var malformedPreferences = new SettingsStore(malformedDirectory);
    Check(malformedPreferences.Error is not null && Directory.GetFiles(malformedDirectory, "settings.json.damaged-*").Length == 1, "damaged preferences are preserved before fallback");
    var scope = Path.Combine(directory, "instance-test");
    using (var primary = new SingleInstanceService(scope))
    {
        Check(primary.IsPrimary, "first process owns the instance lock");
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SetHandler(args => received.TrySetResult(args));
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if ((Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--instance-client"); start.ArgumentList.Add(scope);
        using var child = System.Diagnostics.Process.Start(start)!;
        Check(child.WaitForExit(6000) && child.ExitCode == 0 && received.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult().SequenceEqual(["--new-note"]), "a real secondary process routes taskbar commands to the running application");
        var receivedFiles = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.SetHandler(args => receivedFiles.TrySetResult(args));
        start.ArgumentList.Add("--open"); start.ArgumentList.Add(fileWithSpaces);
        using var fileChild = System.Diagnostics.Process.Start(start)!;
        Check(fileChild.WaitForExit(6000) && fileChild.ExitCode == 0 && receivedFiles.Task.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult().SequenceEqual(["--open", fileWithSpaces]), "a real secondary process forwards Markdown file paths without dropping Unicode");
    }
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(directory, true); }
