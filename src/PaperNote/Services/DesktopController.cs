using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaperNote.Models;
using PaperNote.Views;
using PaperNote.Services.Platform;

namespace PaperNote.Services;

// Owns the lifetime of desktop notes. The library may be hidden without closing any sticky notes.
public sealed class DesktopController
{
    private readonly NoteStore _store;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Dictionary<Guid, StickyWindow> _windows = [];
    private readonly List<MarkdownWindow> _documents = [];
    private MainWindow? _library;
    private SettingsWindow? _settingsWindow;
    private readonly GlobalHotkeyService _hotkey;
    public SettingsStore Preferences { get; }
    public AutoStartService AutoStart { get; } = new();
    public string ShortcutStatus { get; private set; } = "正在注册快捷键…";
    public string ShellStatus { get; private set; } = "";
    public event Action? IntegrationChanged;
    private readonly List<(NativeMenuItem Item, bool Enabled)> _recordingMenus = [];
    private TrayIcon? _tray;
    private readonly List<NativeMenuItem> _visibilityItems = [];
    private bool _hidden;
    private bool _started;
    private readonly Queue<string[]> _pendingLaunches = new();
    public bool IsQuitting { get; private set; }
    public bool IsClosingSession { get; private set; }

    public DesktopController(NoteStore store, IClassicDesktopStyleApplicationLifetime lifetime, SettingsStore preferences)
    {
        _store = store; _lifetime = lifetime; Preferences = preferences;
        _hotkey = new GlobalHotkeyService(() => Dispatcher.UIThread.Post(() => { if (!IsClosingSession) NewNote(); }));
        _lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _lifetime.ShutdownRequested += (_, e) => { if (!IsQuitting) { e.Cancel = true; _ = QuitAsync(); } };
        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activation)
            activation.Activated += (_, e) => {
                if (e is FileActivatedEventArgs files)
                {
                    var paths = files.Files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToArray();
                    if (paths.Length > 0) HandleLaunch(["--open", .. paths]);
                    return;
                }
                if (e.Kind != ActivationKind.Reopen) return;
                if (_settingsWindow?.IsVisible == true) { _settingsWindow.Activate(); return; }
                if (_documents.LastOrDefault() is { } document) { document.Show(); document.Activate(); return; }
                if (_library?.IsVisible == true) { _library.Activate(); return; }
                ShowAll();
            };
    }
    public void Start(string[] args)
    {
        _started = true;
        if (OperatingSystem.IsMacOS()) { MacNative.EnsureDockIcon(); ShellStatus = MacNative.DockStatus; }
        else if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") != "1") ShellStatus = WindowsTaskbar.InstallTasks();
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") != "1")
            FileAssociations.RegisterWindows();
        BuildMenus();
        _ = RefreshShortcutAsync();
        var notes = Preferences.Current.RestoreDesktop ? DesktopSession.StartupNotes(_store.Notes) : [];
        foreach (var note in notes) OpenNote(note, expand: false);
        // Do not open the library as a fallback for an empty desktop.
        if (args.Length > 0) HandleLaunch(args);
        while (_pendingLaunches.TryDequeue(out var pending)) HandleLaunch(pending);
        if (_store.Error is { } error) Dispatcher.UIThread.Post(() => _ = ShowErrorAsync(error));
        WriteIntegrationDiagnostics();
    }
    private void WriteIntegrationDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") != "1") return;
        Directory.CreateDirectory(Path.GetDirectoryName(_store.DataPath)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_store.DataPath)!, "integration-diagnostics.json"), System.Text.Json.JsonSerializer.Serialize(new {
            TestMode = true, Dock = ShellStatus, Process = Environment.ProcessPath, ProcessId = Environment.ProcessId,
            VisibleWindows = _lifetime.Windows.Count(w => w.IsVisible), DesktopWindows = _windows.Count, DocumentWindows = _documents.Count,
            TrayVisible = _tray?.IsVisible == true
        }));
    }
    public void ShowLibrary(bool search = false)
    {
        _library ??= new MainWindow(_store, this);
        _lifetime.MainWindow ??= _library;
        _library.Show(); _library.WindowState = WindowState.Normal; _library.Activate();
        if (search) _library.FocusSearch();
    }
    public void CloseCurrentWindow(Window? requestingWindow = null)
    {
        if (IsClosingSession) return;
        Window? target;
        if (OperatingSystem.IsMacOS())
        {
            // Native WebView focus can leave the managed keyboard focus on the library.
            // Match AppKit's key window instead of closing whichever window raised KeyDown.
            var keyWindow = MacNative.KeyWindow;
            target = keyWindow == IntPtr.Zero ? null : _lifetime.Windows.FirstOrDefault(window =>
                window.IsVisible && window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle handle && handle.NSWindow == keyWindow);
        }
        else target = requestingWindow ?? _lifetime.Windows.FirstOrDefault(window => window.IsActive);
        if (Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") == "1")
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(_store.DataPath)!, "close-target-diagnostics.jsonl"),
                System.Text.Json.JsonSerializer.Serialize(new { Source = requestingWindow?.Title, Target = target?.Title,
                    LibraryVisible = _library?.IsVisible == true, Windows = _lifetime.Windows.Select(window => new { window.Title, window.IsActive, window.IsVisible }) }) + Environment.NewLine);
        target?.Close();
    }
    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show(); _settingsWindow.Activate();
    }
    public void ShowMarkdownEditor()
        => OpenDocument(null);
    public MarkdownWindow? FindDocument(string path) => _documents.FirstOrDefault(w => w.FilePath is { } existing && LaunchRequest.PathComparer.Equals(existing, Path.GetFullPath(path)));
    public void OpenDocuments(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (LaunchRequest.TryGetPath(path, out var normalized)) OpenDocument(normalized);
    }
    private void OpenDocument(string? path)
    {
        if (IsClosingSession) return;
        if (path is not null && FindDocument(path) is { } existing)
        { existing.Show(); existing.WindowState = WindowState.Normal; existing.Activate(); existing.FocusEditor(); return; }
        MarkdownWindow window;
        try { window = new MarkdownWindow(this, path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _ = ShowErrorAsync("无法打开文档：" + ex.Message); return; }
        _documents.Add(window); _lifetime.MainWindow ??= window;
        window.Closed += (_, _) => _documents.Remove(window);
        window.Show();
    }
    public void HandleLaunch(string[] args)
    {
        if (IsClosingSession) return;
        if (!_started) { _pendingLaunches.Enqueue(args); return; }
        if (!LaunchRequest.TryParse(args, out var request)) return;
        if (request.Files.Count > 0) { OpenDocuments(request.Files); return; }
        if (args.Contains("--new-note")) NewNote();
        else if (args.Contains("--markdown")) ShowMarkdownEditor();
        else if (args.Contains("--settings")) ShowSettings();
        else if (args.Contains("--library")) ShowLibrary();
        else if (!args.Contains("--login")) ShowAll();
    }
    public async Task<HotkeyResult> SaveShortcutAsync(ShortcutSpec spec, bool enabled)
    {
        var old = Preferences.Current.NewNoteShortcut; var oldEnabled = Preferences.Current.GlobalShortcutEnabled;
        var result = await _hotkey.ApplyAsync(enabled ? spec : null);
        if (!result.Success) { ShortcutStatus = result.Error ?? "注册失败"; IntegrationChanged?.Invoke(); return result; }
        Preferences.Current.NewNoteShortcut = spec; Preferences.Current.GlobalShortcutEnabled = enabled;
        if (!Preferences.Save())
        {
            Preferences.Current.NewNoteShortcut = old; Preferences.Current.GlobalShortcutEnabled = oldEnabled;
            await _hotkey.ApplyAsync(oldEnabled ? old : null);
            return new(false, Preferences.Error);
        }
        ShortcutStatus = enabled ? "全局快捷键已生效" : "全局快捷键已关闭"; IntegrationChanged?.Invoke(); return new(true);
    }
    public async Task<HotkeyResult> SuspendShortcutAsync()
    {
        if (_recordingMenus.Count == 0 && NativeMenu.GetMenu(Application.Current!) is { } menu)
            foreach (var item in menu.Items.OfType<NativeMenuItem>()) { _recordingMenus.Add((item, item.IsEnabled)); item.IsEnabled = false; }
        var result = await _hotkey.ApplyAsync(null);
        if (!result.Success) RestoreRecordingMenus();
        return result;
    }
    private void RestoreRecordingMenus()
    {
        foreach (var (item, enabled) in _recordingMenus) item.IsEnabled = enabled;
        _recordingMenus.Clear();
    }
    public async Task<HotkeyResult> RefreshShortcutAsync()
    {
        RestoreRecordingMenus();
        if (IsQuitting) return new(true);
        var result = await _hotkey.ApplyAsync(Preferences.Current.GlobalShortcutEnabled ? Preferences.Current.NewNoteShortcut : null);
        ShortcutStatus = result.Success ? Preferences.Current.GlobalShortcutEnabled ? "全局快捷键已生效" : "全局快捷键已关闭" : result.Error ?? "注册失败";
        IntegrationChanged?.Invoke(); return result;
    }
    public Note NewNote(string? color = null, PixelPoint? near = null)
    {
        var note = _store.Add("", color ?? Preferences.Current.DefaultColor);
        if (near is { } point) _store.UpdateDesktop(note, d => { d.X = point.X + 28; d.Y = point.Y + 28; });
        OpenNote(note); _windows[note.Id].FocusEditor(); return note;
    }
    public void OpenNote(Note note, bool expand = true)
    {
        if (note.IsDeleted) return;
        _hidden = false;
        if (_windows.TryGetValue(note.Id, out var existing))
        {
            existing.Show(); existing.WindowState = WindowState.Normal;
            if (expand) existing.Expand();
            existing.Activate(); if (expand) existing.FocusEditor(); UpdateVisibilityMenu(); return;
        }
        _store.UpdateDesktop(note, d => { d.IsOpen = true; d.HasBeenPlaced = true; });
        var window = new StickyWindow(_store, note, this, _windows.Count);
        _lifetime.MainWindow ??= window;
        _windows[note.Id] = window;
        window.Closed += (_, _) => {
            _windows.Remove(note.Id); UpdateVisibilityMenu();
        };
        window.Show(); if (expand) window.FocusEditor(); UpdateVisibilityMenu();
    }
    public async Task TrashNotesAsync(IEnumerable<Note> notes)
    {
        foreach (var note in notes.ToArray())
        {
            if (_windows.TryGetValue(note.Id, out var window) && !await window.FlushAsync()) { await ShowErrorAsync("无法读取便签内容，删除已取消。请稍后重试。"); return; }
            if (_store.Error is not null) { await ShowErrorAsync(_store.Error); return; }
            _store.Update(note, n => n.IsDeleted = true);
        }
    }
    public void ShowAll()
    {
        if (_windows.Count == 0) {
            if (_documents.LastOrDefault() is { } document) { document.Show(); document.WindowState = WindowState.Normal; document.Activate(); }
            return;
        }
        _hidden = false;
        foreach (var window in _windows.Values) { window.Show(); window.WindowState = WindowState.Normal; }
        _windows.Values.Last().Activate(); UpdateVisibilityMenu();
    }
    public void ToggleVisibility()
    {
        if (_windows.Count == 0) return;
        if (_hidden) { ShowAll(); return; }
        _hidden = true;
        foreach (var window in _windows.Values) window.Hide();
        UpdateVisibilityMenu();
    }
    private void UpdateVisibilityMenu() { foreach (var item in _visibilityItems) item.Header = _hidden ? "显示桌面便签" : "暂时隐藏桌面便签"; }
    private void BuildMenus()
    {
        NativeMenu CreateActions(bool includeQuit = true)
        {
            var menu = new NativeMenu();
            menu.Items.Add(Item("新建", () => {
                if (_documents.Any(w => w.IsActive)) { ShowMarkdownEditor(); return; }
                var active = _windows.Values.FirstOrDefault(w => w.IsActive);
                NewNote(active?.PaperColor, active?.Position);
            }, "N"));
            menu.Items.Add(Item("打开便签库", () => ShowLibrary()));
            menu.Items.Add(Item("Markdown 编辑器…", ShowMarkdownEditor));
            menu.Items.Add(Item("设置…", ShowSettings));
            menu.Items.Add(Item("显示所有桌面便签", ShowAll));
            if (OperatingSystem.IsMacOS()) menu.Items.Add(Item("关闭当前窗口", () => CloseCurrentWindow(), "Q"));
            menu.Items.Add(new NativeMenuItemSeparator());
            var visibility = Item("暂时隐藏桌面便签", ToggleVisibility); _visibilityItems.Add(visibility); menu.Items.Add(visibility);
            if (includeQuit)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(Item("退出纸间", () => _ = QuitAsync(), OperatingSystem.IsMacOS() ? null : "Q"));
            }
            return menu;
        }
        // macOS exports the existing application menu early; mutate it so the native exporter stays attached.
        var appMenu = NativeMenu.GetMenu(Application.Current!);
        if (appMenu is null) NativeMenu.SetMenu(Application.Current!, appMenu = new NativeMenu());
        var frameworkAbout = appMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(item => item.Header == "About Avalonia");
        if (frameworkAbout is not null) appMenu.Items.Remove(frameworkAbout);
        if (OperatingSystem.IsMacOS())
        {
            // Keep the native quit action, but remove its Command+Q accelerator.
            var quit = appMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(item => item.Header == "Quit");
            if (quit is not null) { quit.Header = "退出纸间"; quit.Gesture = null; }
        }
        var actions = CreateActions(includeQuit: false);
        var items = actions.Items.ToArray(); actions.Items.Clear();
        for (var i = 0; i < items.Length; i++) appMenu.Items.Insert(i, items[i]);
        appMenu.Items.Insert(items.Length, new NativeMenuItemSeparator());
        NativeDock.SetMenu(Application.Current!, CreateActions(includeQuit: false));
        _tray = new TrayIcon { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PaperNote/Assets/icon.png"))), ToolTipText = "纸间 · 桌面便签", Menu = CreateActions(), IsVisible = true };
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        if (!OperatingSystem.IsMacOS()) _tray.Clicked += (_, _) => ShowAll();
    }
    private static NativeMenuItem Item(string title, Action action, string? key = null)
    {
        var item = new NativeMenuItem(title);
        if (key is not null) item.Gesture = new KeyGesture(Enum.Parse<Key>(key), OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);
        item.Click += (_, _) => action(); return item;
    }
    public async Task QuitAsync()
    {
        if (IsClosingSession || _documents.Any(w => w.IsBusy)) return;
        IsClosingSession = true;
        try
        {
            foreach (var document in _documents.ToArray())
                if (!await document.ConfirmCloseAsync()) return;
            if (_library is not null) await _library.FlushAsync();
            foreach (var window in _windows.Values.ToArray())
                if (!await window.PrepareSessionEndAsync()) { await ShowErrorAsync("无法保存便签，退出已取消。请稍后重试。"); return; }
            // Keep desktop notes open in storage so the next launch restores their layout.
            if (!_store.Save())
            {
                await ShowErrorAsync(_store.Error ?? "保存失败，关闭已取消。请重试。");
                return;
            }
            IsQuitting = true;
            foreach (var window in _windows.Values.ToArray()) window.CloseForSessionEnd();
            foreach (var document in _documents.ToArray()) document.CloseForSessionEnd();
            _settingsWindow?.Close();
            _library?.CloseForShutdown();
            _hotkey.Dispose(); _tray?.Dispose(); _lifetime.Shutdown();
        }
        finally { IsClosingSession = false; }
    }
    private async Task ShowErrorAsync(string message)
    {
        ShowLibrary();
        var dialog = new Window { Title = "保存提示", Width = 410, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = UI.Stack(Avalonia.Layout.Orientation.Vertical, 16); stack.Margin = new Thickness(24);
        var text = UI.Text(message, 13); text.TextWrapping = Avalonia.Media.TextWrapping.Wrap; stack.Children.Add(text);
        var close = new Button { Content = "知道了", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close(); stack.Children.Add(close); dialog.Content = stack;
        await dialog.ShowDialog(_library!);
    }
}
