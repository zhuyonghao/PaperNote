using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using PaperNote.Models;
using PaperNote.Services;

namespace PaperNote.Views;

public sealed class MarkdownWindow : Window
{
    private readonly EditorPane _editor = new();
    private readonly MarkdownDocument _document = new();
    private readonly TextBlock _name = UI.Text("未命名.md", 14, "Text", FontWeight.SemiBold);
    private readonly TextBlock _status = UI.Text("新文档 · 尚未保存", 11, "Muted");
    private readonly TextBlock _count = UI.Text("0 字", 11, "Muted");
    private readonly Ellipse _stateDot = new() { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center };
    public string? FilePath => _document.FilePath;
    private Note _note = new() { Color = "white" };
    private readonly StackPanel _commands;
    private readonly DesktopController _controller;
    private bool _closing, _busy, _reading;
    private readonly Grid _root;
    private readonly Border _headerSurface, _footerSurface;
    private readonly Button _readingButton;
    public bool IsBusy => _busy;
    private static readonly FilePickerFileType MarkdownFiles = new("Markdown / 纯文本") { Patterns = LaunchRequest.Extensions.Select(e => "*" + e).ToArray() };

    public MarkdownWindow(DesktopController controller, string? path = null)
    {
        _controller = controller;
        if (path is not null) { _document.Load(path); _note.Markdown = _document.Content; }
        _name.TextTrimming = _status.TextTrimming = TextTrimming.CharacterEllipsis;
        Icon = UI.AppIcon(); Width = 1060; Height = 780; MinWidth = 580; MinHeight = 420;
        Background = UI.Brush("Canvas"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 52;
        var root = _root = new Grid { RowDefinitions = new RowDefinitions("52,*,30") }; Content = root;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(OperatingSystem.IsMacOS() ? 83 : 20, 0, OperatingSystem.IsWindows() ? 145 : 18, 0) };
        var file = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*"), Margin = new Thickness(0, 0, 18, 0) };
        file.Children.Add(UI.Icon("document", "Faint", 16));
        _name.FontSize = 12; _name.FontWeight = FontWeight.Medium; _name.Foreground = UI.Brush("Text");
        Grid.SetColumn(_name, 1); file.Children.Add(_name); header.Children.Add(file);
        _commands = UI.Stack(Orientation.Horizontal, 5);
        void Command(string label, string icon, Func<Task> action, bool primary = false)
        {
            var button = new Button { Classes = { primary ? "document-save" : "document-tool" }, VerticalAlignment = VerticalAlignment.Center };
            if (primary)
            {
                var content = UI.Stack(Orientation.Horizontal, 7); content.Children.Add(UI.Icon(icon, "White", 14));
                content.Children.Add(UI.Text("保存", 11, "White", FontWeight.Medium)); button.Content = content;
            }
            else button.Content = UI.Icon(icon, "Muted", 16);
            ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label);
            button.Click += async (_, _) => await RunAsync(action); _commands.Children.Add(button);
        }
        Command("新建文档", "plus", () => { controller.ShowMarkdownEditor(); return Task.CompletedTask; });
        Command("打开文件…", "folder", OpenAsync);
        _commands.Children.Add(new Border { Width = 1, Height = 16, Background = UI.Brush("Border"), Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        Command("保存", "save", async () => { await SaveAsync(); }, primary: true);
        var more = new Button { Content = UI.Icon("more", "Muted", 16), Classes = { "document-tool" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(more, "文档选项"); ToolTip.SetTip(more, "文档选项");
        _readingButton = UI.IconButton("book", "沉浸阅读（只读） · ⌘/Ctrl Shift R", ToggleReading);
        _readingButton.Classes.Add("document-tool"); _commands.Children.Add(_readingButton);
        var menu = new ContextMenu();
        void Item(string label, string icon, Func<Task> action)
        {
            var item = new MenuItem { Header = label, Icon = UI.Icon(icon, "Muted", 14) };
            item.Click += async (_, _) => await RunAsync(action); menu.Items.Add(item);
        }
        Item("另存为…", "export", async () => { await SaveAsync(saveAs: true); });
        Item("切换源码 / 所见即所得", "code", () => { _editor.ToggleSource(); return Task.CompletedTask; });
        menu.Items.Add(new Separator());
        Item("便签库", "notes", () => { controller.ShowLibrary(); return Task.CompletedTask; });
        more.Click += (_, _) => menu.Open(more); _commands.Children.Add(more);
        Grid.SetColumn(_commands, 1); header.Children.Add(_commands);
        var headerSurface = _headerSurface = new Border { Child = header, Background = UI.Brush("Chrome"), BorderBrush = UI.Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
        headerSurface.PointerPressed += (_, e) => {
            if (e.Source is Control c && (c is Button || c.FindAncestorButton())) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        root.Children.Add(headerSurface);
        Grid.SetRow(_editor, 1); root.Children.Add(_editor);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 0) };
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("12,*"), Margin = new Thickness(0, 0, 18, 0) };
        status.Children.Add(_stateDot); _status.FontSize = 10; Grid.SetColumn(_status, 1); status.Children.Add(_status); footer.Children.Add(status);
        var stats = UI.Stack(Orientation.Horizontal, 16); stats.Children.Add(_count);
        stats.Children.Add(new Border { Width = 1, Height = 11, Background = UI.Brush("Border"), VerticalAlignment = VerticalAlignment.Center });
        stats.Children.Add(UI.Text("Markdown · UTF-8", 10, "Faint")); Grid.SetColumn(stats, 1); footer.Children.Add(stats);
        var footerSurface = _footerSurface = new Border { Child = footer, BorderBrush = UI.Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(footerSurface, 2); root.Children.Add(footerSurface);
        _editor.MarkdownChanged += markdown => { _document.Content = markdown; RefreshTitle(); };
        _editor.CountChanged += count => _count.Text = $"{count} 字";
        _editor.ActionRequested += action => {
            if (action == "close") { _controller.CloseCurrentWindow(this); return; }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => HandleAction(action));
        };
        _editor.SetNote(_note); _editor.SetViewMode("rich");
        Opened += (_, _) => _editor.FocusEditor();
        KeyDown += (_, e) => {
            if (e.Key == Key.Escape && _reading) { ToggleReading(); e.Handled = true; return; }
            if ((e.KeyModifiers & (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) == 0) return;
            var action = e.Key switch { Key.R when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => "reading", Key.N => "new", Key.O => "open", Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => "save-as", Key.S => "save", Key.W => "close", Key.Q => OperatingSystem.IsMacOS() ? "close" : "quit", Key.M when e.KeyModifiers.HasFlag(KeyModifiers.Shift) => "source", _ => null };
            if (action is not null) { e.Handled = true; HandleAction(action); }
        };
        Closing += async (_, e) => {
            if (_closing) return;
            e.Cancel = true;
            await RunAsync(async () => { if (await ConfirmCloseAsync()) { _closing = true; Close(); } });
        };
        RefreshTitle();
    }

    private void ToggleReading()
    {
        _reading = !_reading; _editor.SetReadOnly(_reading);
        _name.IsVisible = !_reading;
        foreach (var command in _commands.Children) command.IsVisible = !_reading || command == _readingButton;
        _readingButton.Content = _reading ? UI.Text("退出阅读 · Esc", 11, "Muted") : UI.Icon("book");
        _readingButton.Width = _reading ? 110 : 30;
        _footerSurface.IsVisible = !_reading;
        _root.RowDefinitions[2].Height = new GridLength(_reading ? 0 : 30);
        ToolTip.SetTip(_readingButton, _reading ? "退出沉浸阅读 · Esc" : "沉浸阅读（只读）");
        Avalonia.Automation.AutomationProperties.SetName(_readingButton, _reading ? "退出沉浸阅读" : "沉浸阅读（只读）");
    }
    private void RefreshTitle()
    {
        var marker = _document.IsDirty ? " *" : "";
        Title = _document.Name + marker + " · Markdown 编辑器 · 纸间";
        _name.Text = _document.Name + marker;
        _status.Text = _document.IsDirty ? "尚有更改未保存" : _document.FilePath is null ? "新文档 · 尚未保存" : "已保存到本地";
        _stateDot.Fill = UI.Brush(_document.IsDirty ? "Faint" : "Faint");
        ToolTip.SetTip(_status, _document.FilePath ?? "保存后可在访达或资源管理器中打开");
        ToolTip.SetTip(_name, _document.FilePath ?? "未命名文档");
    }
    private void HandleAction(string action)
    {
        if (_busy || _controller.IsClosingSession) return;
        switch (action)
        {
            case "reading": ToggleReading(); break;
            case "new": _controller.ShowMarkdownEditor(); break;
            case "open": _ = RunAsync(OpenAsync); break;
            case "save": _ = RunAsync(async () => { await SaveAsync(); }); break;
            case "save-as": _ = RunAsync(async () => { await SaveAsync(saveAs: true); }); break;
            case "source": _editor.ToggleSource(); break;
            case "close": _controller.CloseCurrentWindow(this); break;
            case "quit": _ = _controller.QuitAsync(); break;
            case "library": _controller.ShowLibrary(); break;
            case "settings": _controller.ShowSettings(); break;
        }
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _controller.IsClosingSession) return;
        _busy = true; _commands.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); }
        finally { _busy = false; _commands.IsEnabled = true; }
    }
    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开 Markdown 文档", AllowMultiple = false, FileTypeFilter = [MarkdownFiles, FilePickerFileTypes.All] });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath() ?? throw new IOException("请选择本地 Markdown 文件。");
        if (_controller.FindDocument(path) is { } existing)
        { existing.Show(); existing.WindowState = WindowState.Normal; existing.Activate(); existing.FocusEditor(); return; }
        if (!await ConfirmCloseAsync()) return;
        _document.Load(path);
        // A fresh identity also rejects delayed bridge messages from the previous file.
        _note = new Note { Color = "white", Markdown = _document.Content };
        _editor.SetNote(_note); _editor.FocusEditor(); RefreshTitle();
    }
    public void FocusEditor() => _editor.FocusEditor();
    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        string? path = _document.FilePath;
        if (saveAs || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存 Markdown 文档", SuggestedFileName = _document.Name, DefaultExtension = "md", FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }], ShowOverwritePrompt = true });
            if (file is null) return false;
            path = file.TryGetLocalPath() ?? throw new IOException("请选择本地保存位置。");
        }
        if (!await _editor.FlushAsync()) throw new IOException("编辑器尚未准备好，无法读取当前内容。请稍后重试保存。");
        _document.Save(path); RefreshTitle(); return true;
    }
    public async Task<bool> ConfirmCloseAsync()
    {
        var flushed = await _editor.FlushAsync();
        if (!flushed && _editor.IsReady) { await ShowErrorAsync("无法读取编辑器内容，请稍后重试。"); return false; }
        if (_document.CanCloseWithoutSaving) return true;
        if (!flushed) { await ShowErrorAsync("无法读取编辑器内容，请稍后重试。"); return false; }
        Activate();
        var dialog = new Window { Title = "保存更改", Width = 430, CanResize = false, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = UI.Stack(Orientation.Vertical, 18); panel.Margin = new Thickness(24);
        var text = UI.Text($"保存对“{_document.Name}”的更改？", 15, "Text", FontWeight.SemiBold); text.TextWrapping = TextWrapping.Wrap; panel.Children.Add(text);
        panel.Children.Add(UI.Text("未保存的更改将在关闭后丢失。", 12));
        var buttons = UI.Stack(Orientation.Horizontal, 8); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var (label, result) in new[] { ("取消", "cancel"), ("不保存", "discard"), ("保存", "save") })
        {
            var button = new Button { Content = label, Classes = { result == "save" ? "primary" : "quiet" }, IsDefault = result == "save", IsCancel = result == "cancel" };
            button.Click += (_, _) => dialog.Close(result); buttons.Children.Add(button);
        }
        panel.Children.Add(buttons); dialog.Content = panel;
        var choice = await dialog.ShowDialog<string?>(this);
        if (choice == "discard") return true;
        if (choice != "save") return false;
        try { return await SaveAsync(); }
        catch (Exception ex) { await ShowErrorAsync(ex.Message); return false; }
    }
    private async Task ShowErrorAsync(string message)
    {
        var dialog = new Window { Title = "文档操作失败", Width = 440, CanResize = false, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = UI.Stack(Orientation.Vertical, 18); panel.Margin = new Thickness(24);
        var text = UI.Text(message, 13); text.TextWrapping = TextWrapping.Wrap; panel.Children.Add(text);
        var ok = new Button { Content = "知道了", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close(); panel.Children.Add(ok); dialog.Content = panel;
        await dialog.ShowDialog(this);
    }
    public void CloseForSessionEnd() { _closing = true; Close(); }
}
