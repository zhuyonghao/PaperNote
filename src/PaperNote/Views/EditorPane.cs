using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PaperNote.Models;
using PaperNote.Services;
using PaperNote.Services.Platform;

namespace PaperNote.Views;
public sealed class EditorPane : Grid
{
    private readonly NativeWebView _web = new();
    private readonly TextBlock _error = new() { Text = "正在准备编辑器…", Margin = new Avalonia.Thickness(30), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly NoteStore? _store;
    private Note? _note;
    private string _markdown = "";
    private string? _paperColor;
    private bool _deleted;
    private bool _ready;
    private bool _attached, _webAttached, _navigated;
    private Window? _window;
    private bool? _lowMemoryApplied;
    private string? _appliedOptions, _surfaceColor;
    private readonly bool _compact;
    private bool _showFormatting;
    private bool _readOnly;
    private double _fontSize = 15;
    private bool _focusRequested;
    private string? _viewMode;
    private int _token;
    private readonly SemaphoreSlim _scripts = new(1, 1);
    public event Action<string>? ActionRequested;
    public event Action<int>? CountChanged;
    public event Action<bool>? ModeChanged;
    public event Action<string>? MarkdownChanged;
    public bool SourceMode { get; private set; }
    public bool IsReady => _ready;
    public EditorPane(NoteStore? store = null, bool compact = false)
    {
        _store = store; _compact = compact;
        UpdateBackground();
        Children.Add(_error);
        _web.WebMessageReceived += OnMessage;
        _web.NavigationStarted += (_, e) => {
            // Never let pasted links replace the local editor or gain access to its bridge.
            if (_ready) e.Cancel = true;
        };
        _web.NewWindowRequested += (_, e) => e.Handled = true;
        _web.NavigationCompleted += (_, e) => {
            if (!e.IsSuccess) { _error.Text = "编辑器无法启动。Windows 请检查 WebView2 Runtime 是否已安装。"; _error.IsVisible = true; }
        };
        _web.AdapterDestroyed += (_, _) => { _ready = _navigated = false; _appliedOptions = null; _lowMemoryApplied = null; };
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateActivity(); };
        AttachedToVisualTree += (_, _) => {
            _attached = true;
            AppTheme.Changed += OnThemeChanged;
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window is not null) _window.PropertyChanged += OnWindowPropertyChanged;
            UpdateActivity();
        };
        DetachedFromVisualTree += (_, _) => {
            _attached = false;
            AppTheme.Changed -= OnThemeChanged;
            if (_window is not null) _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        };
    }
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) UpdateActivity();
    }
    private void UpdateActivity()
    {
        if (!_attached) return;
        var active = IsVisible && _window?.IsVisible == true && _window.WindowState != WindowState.Minimized;
        // Restoring a collapsed sticky should not start an unused renderer.
        if (!_webAttached && active) { _webAttached = true; Children.Insert(0, _web); }
        if (_webAttached && !_navigated && active)
        {
            _navigated = true;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PaperNote.Editor.editor.html")!;
            using var reader = new StreamReader(stream);
            // Apply the surface before WebView creates its first native frame. CSS
            // and bridge configuration otherwise arrive after the blank browser surface.
            UpdateBackground();
            var theme = AppTheme.IsDark ? "dark" : "light";
            var surface = SurfaceColor;
            var background = _compact ? "var(--paper)" : "var(--canvas)";
            var html = reader.ReadToEnd()
                .Replace("<html lang=\"zh-CN\">", $"<html lang=\"zh-CN\" data-theme=\"{theme}\" style=\"background:{background};--paper:{surface};--canvas:{surface}\">")
                .Replace("<body>", _compact ? "<body class=\"compact\">" : "<body>");
            _web.NavigateToString(html);
        }
        if (_ready && OperatingSystem.IsWindows() && _lowMemoryApplied != !active
            && WindowsWebViewMemory.SetLowMemory(_web, !active)) _lowMemoryApplied = !active;
    }
    private void OnMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = JsonDocument.Parse(e.Body ?? "{}");
            var root = json.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true; _error.IsVisible = false;
                    _ = InitializeEditorAsync();
                    UpdateActivity();
                    break;
                case "change":
                    if (_note is null || root.GetProperty("token").GetInt32() != _token || root.GetProperty("id").GetString() != _note.Id.ToString()) return;
                    _markdown = root.GetProperty("markdown").GetString() ?? "";
                    UpdateMarkdown(_markdown);
                    CountChanged?.Invoke(root.GetProperty("count").GetInt32());
                    break;
                case "stats":
                    if (root.GetProperty("token").GetInt32() == _token) CountChanged?.Invoke(root.GetProperty("count").GetInt32());
                    break;
                case "action": ActionRequested?.Invoke(root.GetProperty("action").GetString()!); break;
                case "mode": SourceMode = root.GetProperty("source").GetBoolean(); _viewMode = SourceMode ? "source" : "rich"; ModeChanged?.Invoke(SourceMode); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { System.Diagnostics.Trace.WriteLine(ex); }
    }
    private async Task InitializeEditorAsync()
    {
        await ApplyOptionsAsync();
        if (_note is not null) await PushNoteAsync();
        if (_focusRequested) { _focusRequested = false; await ScriptAsync("paper.focus()"); }
    }
    public void ConfigureCompact(bool showFormatting, double fontSize)
    {
        fontSize = Math.Clamp(double.IsFinite(fontSize) ? fontSize : 15, 13, 20);
        if (_showFormatting == showFormatting && _fontSize == fontSize) return;
        _showFormatting = showFormatting;
        _fontSize = fontSize;
        if (_ready) _ = ApplyOptionsAsync();
    }
    private Task ApplyOptionsAsync()
    {
        var options = JsonSerializer.Serialize(new { compact = _compact, toolbar = !_compact || _showFormatting, fontSize = _fontSize, mode = _viewMode, readOnly = _readOnly, dark = AppTheme.IsDark, canvas = AppTheme.Resolve("Canvas"), paper = AppTheme.Resolve(NoteColors.Get(_note?.Color ?? "white").Paper) });
        if (_appliedOptions == options) return Task.CompletedTask;
        _appliedOptions = options;
        return ScriptAsync("paper.configure(" + options + ")");
    }
    private string SurfaceColor => _compact
        ? AppTheme.Resolve(NoteColors.Get(_note?.Color ?? "white").Paper)
        : AppTheme.Resolve("Canvas");
    private void UpdateBackground(string? surface = null)
    {
        // NativeWebView forwards Background to the native browser controller;
        // a managed overlay cannot cover a windowed WebView on Windows.
        surface ??= SurfaceColor;
        if (_surfaceColor == surface) return;
        _surfaceColor = surface;
        Background = _web.Background = UI.Brush(surface);
    }
    private void OnThemeChanged() { UpdateBackground(); if (_ready) _ = ApplyOptionsAsync(); }
    public void SetReadOnly(bool readOnly) { if (_readOnly == readOnly) return; if (readOnly) _viewMode = "rich"; _readOnly = readOnly; if (_ready) _ = ApplyOptionsAsync(); }
    public void SetViewMode(string mode)
    {
        if (mode is not ("rich" or "source")) throw new ArgumentOutOfRangeException(nameof(mode));
        _viewMode = mode;
        if (_ready) _ = ScriptAsync("paper.setMode(" + JsonSerializer.Serialize(mode) + ")");
    }
    private void UpdateMarkdown(string markdown)
    {
        if (_note is null || _note.IsDeleted) return;
        if (_store is not null) _store.UpdateMarkdown(_note.Id, markdown);
        else _note.Markdown = markdown;
        MarkdownChanged?.Invoke(markdown);
    }
    public void SetNote(Note note)
    {
        // Set the native backing surface even when only the paper color changes.
        var surface = _compact ? AppTheme.Resolve(NoteColors.Get(note.Color).Paper) : AppTheme.Resolve("Canvas");
        UpdateBackground(surface);
        var paper = AppTheme.Resolve(NoteColors.Get(note.Color).Paper);
        if (_note?.Id == note.Id && _markdown == note.Markdown && _deleted == note.IsDeleted)
        {
            if (_ready && _paperColor != paper) _ = ScriptAsync("paper.setColor(" + JsonSerializer.Serialize(paper) + ")");
            _paperColor = paper;
            return;
        }
        _paperColor = paper;
        _note = note; _markdown = note.Markdown; _deleted = note.IsDeleted; _token++;
        if (_ready) _ = PushNoteAsync();
    }
    private Task PushNoteAsync()
    {
        if (_note is null) return Task.CompletedTask;
        var data = JsonSerializer.Serialize(new { id = _note.Id.ToString(), token = _token, markdown = _markdown, paper = AppTheme.Resolve(NoteColors.Get(_note.Color).Paper), deleted = _note.IsDeleted });
        return ScriptAsync("paper.setNote(" + data + ")");
    }
    public async Task<bool> FlushAsync()
    {
        if (!_ready || _note is null) return false;
        await _scripts.WaitAsync();
        try
        {
            // Read directly as well: native message delivery may trail InvokeScript completion on some platforms.
            var id = _note.Id;
            var result = await _web.InvokeScript("JSON.stringify({markdown: paper.getMarkdown()})");
            if (result is not null && _note?.Id == id)
            {
                // WebView's API returns a JSON encoded JavaScript result.
                using var first = JsonDocument.Parse(result);
                using var value = first.RootElement.ValueKind == JsonValueKind.String
                    ? JsonDocument.Parse(first.RootElement.GetString()!) : JsonDocument.Parse(result);
                _markdown = value.RootElement.GetProperty("markdown").GetString() ?? "";
                UpdateMarkdown(_markdown);
                return true;
            }
            return false;
        }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { _scripts.Release(); }
    }
    public void ToggleSource() { if (_ready) _ = ScriptAsync("paper.toggleSource()"); }
    public void FocusEditor() { if (_ready) _ = ScriptAsync("paper.focus()"); else _focusRequested = true; }
    private async Task ScriptAsync(string script)
    {
        await _scripts.WaitAsync();
        try { await _web.InvokeScript(script); }
        catch (Exception ex) { ShowError(ex); }
        finally { _scripts.Release(); }
    }
    private void ShowError(Exception ex) => Dispatcher.UIThread.Post(() => { _error.Text = "编辑器发生错误：" + ex.Message; _error.IsVisible = true; });
}
