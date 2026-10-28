using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PaperNote.Models;
using PaperNote.Services;

namespace PaperNote.Views;
public sealed class StickyWindow : Window
{
    private readonly EditorPane _editor;
    private readonly NoteStore _store;
    private readonly Note _note;
    private readonly DesktopController _controller;
    private readonly Button _pin, _format, _add, _readingButton;
    private readonly TextBlock _caption = UI.Text("", 11, "Text", FontWeight.Medium);
    private readonly TextBlock _status = UI.Text("已保存", 10, "Muted");
    private readonly Grid _root;
    private readonly Grid _footer;
    private readonly Border _footerSurface;
    private readonly Flyout _options = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
    private readonly List<(Button Button, Func<bool> Selected)> _optionChoices = [];
    private readonly StackPanel _colors = UI.Stack(Orientation.Horizontal, 0);
    private readonly DispatcherTimer _geometryTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _closing, _closePending, _restoring = true;
    private readonly int _offset;

    public StickyWindow(NoteStore store, Note note, DesktopController controller, int offset = 0)
    {
        _store = store; _note = note; _controller = controller; _offset = offset;
        _editor = new EditorPane(store, compact: true);
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://PaperNote/Assets/icon.png")));
        Title = note.Title; MinWidth = 280; MinHeight = 220; Width = 340; Height = 380;
        Background = UI.Brush(NoteColors.Get(note.Color).Paper);
        Classes.Add("sticky");
        ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 44;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _root = new Grid { RowDefinitions = new RowDefinitions("44,*,40") }; Content = _root;
        _options.FlyoutPresenterClasses.Add("note-options");

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(OperatingSystem.IsMacOS() ? 79 : 13, 0, OperatingSystem.IsWindows() ? 140 : 8, 0) };
        _caption.TextTrimming = TextTrimming.CharacterEllipsis; _caption.Margin = new Thickness(0, 0, 8, 0); bar.Children.Add(_caption);
        var actions = UI.Stack(Orientation.Horizontal, 2);
        _add = SmallIcon("plus", "新建桌面便签 · ⌘/Ctrl N", () => _controller.NewNote(_note.Color, Position)); actions.Children.Add(_add);
        _pin = SmallIcon("pin", "始终置于最前", () => _store.UpdateDesktop(note, d => d.Topmost = !d.Topmost)); actions.Children.Add(_pin);
        _readingButton = SmallIcon("book", "沉浸阅读（只读） · ⌘/Ctrl Shift R", ToggleReading); actions.Children.Add(_readingButton);
        var more = SmallIcon("more", "便签选项", () => { });
        Avalonia.Automation.AutomationProperties.SetName(more, "便签选项");
        more.Flyout = _options;
        _options.Opening += (_, _) => _options.Content = BuildOptionsPanel();
        actions.Children.Add(more); Grid.SetColumn(actions, 1); bar.Children.Add(actions); _root.Children.Add(new Border { Child = bar, BorderBrush = UI.Brush("HoverOverlay"), BorderThickness = new Thickness(0, 0, 0, 1) });
        bar.PointerPressed += (_, e) => {
            if (e.Source is Control c && (c is Button || c.FindAncestorOfType<Button>() is not null)) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { e.Handled = true; ToggleCollapsed(); } else BeginMoveDrag(e);
        };
        Grid.SetRow(_editor, 1); _root.Children.Add(_editor);
        _footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(8, 0, 8, 0) };
        var left = UI.Stack(Orientation.Horizontal, 1);
        _format = SmallIcon("format", "格式工具 · ⌘/Ctrl Shift F", () => { });
        Avalonia.Automation.AutomationProperties.SetName(_format, "展开或收起格式工具");
        ToolTip.SetTip(_format, "格式工具 · ⌘/Ctrl Shift F");
        _format.Click += (_, _) => _store.UpdateDesktop(note, d => d.ShowFormatting = !d.ShowFormatting);
        left.Children.Add(_format); left.Children.Add(_colors); _footer.Children.Add(left);
        _status.HorizontalAlignment = HorizontalAlignment.Right; _status.Margin = new Thickness(4, 0, 6, 0); _status.Cursor = new Cursor(StandardCursorType.Hand);
        _status.PointerPressed += async (_, _) => await FlushAsync(); Grid.SetColumn(_status, 1); _footer.Children.Add(_status);
        var library = SmallIcon("notes", "打开便签库 · ⌘/Ctrl Shift O", () => _controller.ShowLibrary()); Grid.SetColumn(library, 2); _footer.Children.Add(library);
        _footerSurface = new Border { Child = _footer, BorderBrush = UI.Brush("HoverOverlay"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(_footerSurface, 2); _root.Children.Add(_footerSurface);

        _store.Changed += OnChanged; _store.DesktopChanged += OnDesktopChanged; _store.SaveStateChanged += OnSaveStateChanged;
        _editor.ActionRequested += action => {
            if (action == "close") { _controller.CloseCurrentWindow(this); return; }
            Dispatcher.UIThread.Post(() => HandleAction(action));
        };
        _editor.ModeChanged += mode => _caption.Text = mode ? "Markdown 源码" : DisplayTitle;
        _editor.CountChanged += count => ToolTip.SetTip(_status, $"{count} 字 · 自动保存到此设备");
        _geometryTimer.Tick += (_, _) => { _geometryTimer.Stop(); SaveGeometry(); };
        PositionChanged += (_, _) => ScheduleGeometry(); SizeChanged += (_, _) => ScheduleGeometry();
        Opened += (_, _) => {
            var areas = Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => new DesktopWorkArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling)).ToList();
            var placement = DesktopPlacement.Resolve(note.Desktop, areas, _offset);
            Width = placement.Width; Height = placement.Height; Position = new PixelPoint(placement.X, placement.Y);
            ApplyDesktopState(); _restoring = false; SaveGeometry();
        };
        KeyDown += OnKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => { _store.Changed -= OnChanged; _store.DesktopChanged -= OnDesktopChanged; _store.SaveStateChanged -= OnSaveStateChanged; _geometryTimer.Stop(); };
        _editor.SetNote(note); UpdateContent(); ApplyDesktopState();
    }
    private string DisplayTitle => _note.Markdown.Length == 0 ? "随手写点什么" : _note.Title;
    private Button SmallIcon(string name, string label, Action action)
    {
        var button = UI.IconButton(name, label, action); button.Classes.Add("note-tool"); button.Padding = new Thickness(6); button.MinHeight = 28; button.Height = 28; button.Width = 28; button.Content = UI.Icon(name, "Muted", 15); return button;
    }
    private Control BuildOptionsPanel()
    {
        _optionChoices.Clear();
        var panel = UI.Stack(Orientation.Vertical, 4); panel.Width = 236;
        panel.Children.Add(new TextBlock { Text = "便签选项", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = UI.Brush("Muted"), Margin = new Thickness(8, 2, 0, 6) });
        Button ActionButton(string text, string icon, Action action, bool danger = false)
        {
            var content = UI.Stack(Orientation.Horizontal, 10);
            content.Children.Add(UI.Icon(icon, danger ? "Danger" : "Muted", 15));
            content.Children.Add(UI.Text(text, 12, danger ? "Danger" : "Text"));
            var button = new Button { Content = content, Classes = { "quiet", "note-option" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 7), MinHeight = 32 };
            Avalonia.Automation.AutomationProperties.SetName(button, text);
            button.Click += (_, _) => { _options.Hide(); action(); };
            return button;
        }
        void Divider() => panel.Children.Add(new Border { Height = 1, Background = UI.Brush("Border"), Margin = new Thickness(4, 6) });
        void Choices(string label, (string Label, Action Apply, Func<bool> Selected)[] choices)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = UI.Brush("Muted"), Margin = new Thickness(8, 5, 0, 5) });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(4, 0, 4, 2) };
            for (var i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                var button = new Button { Content = choice.Label, FontSize = 12, Classes = { "quiet", "note-choice" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 6), Margin = new Thickness(2, 0), MinHeight = 30 };
                Avalonia.Automation.AutomationProperties.SetName(button, label + " " + choice.Label);
                button.Click += (_, _) => choice.Apply();
                _optionChoices.Add((button, choice.Selected));
                Grid.SetColumn(button, i); row.Children.Add(button);
            }
            panel.Children.Add(row);
        }
        panel.Children.Add(ActionButton(_note.Desktop.ReadOnly ? "退出沉浸阅读" : "沉浸阅读（只读）", "book", ToggleReading));
        panel.Children.Add(ActionButton(_note.Desktop.Collapsed ? "展开便签" : "收起为标题条", "collapse", ToggleCollapsed));
        panel.Children.Add(ActionButton(_editor.SourceMode ? "返回富文本编辑" : "Markdown 源码", "code", () => { Expand(); _editor.ToggleSource(); }));
        panel.Children.Add(ActionButton("导出 Markdown…", "export", () => _ = ExportAsync()));
        panel.Children.Add(ActionButton("新建 Markdown 文档…", "code", _controller.ShowMarkdownEditor));
        Divider();
        Choices("字号", new[] { 13, 15, 17, 20 }.Select(size => (size.ToString(), (Action)(() => _store.UpdateDesktop(_note, d => d.FontSize = size)), (Func<bool>)(() => _note.Desktop.FontSize == size))).ToArray());
        Choices("不透明度", new[] { .7, .8, .9, 1.0 }.Select(value => ($"{value:P0}", (Action)(() => _store.UpdateDesktop(_note, d => d.Opacity = value)), (Func<bool>)(() => Math.Abs(_note.Desktop.Opacity - value) < .01))).ToArray());
        Divider();
        panel.Children.Add(ActionButton("放回便签库", "notes", Close));
        panel.Children.Add(ActionButton("设置…", "settings", _controller.ShowSettings));
        Divider();
        panel.Children.Add(ActionButton("移到最近删除", "trash", () => _ = _controller.TrashNotesAsync([_note]), danger: true));
        RefreshOptionChoices();
        return new ScrollViewer { Content = panel, MaxHeight = 460, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    private void RefreshOptionChoices()
    {
        foreach (var (button, selected) in _optionChoices)
        {
            var active = selected();
            button.Classes.Set("selected", active);
            Avalonia.Automation.AutomationProperties.SetHelpText(button, active ? "当前选项" : "");
        }
    }
    private void UpdateContent()
    {
        Title = _note.Title; _caption.Text = _editor.SourceMode ? "Markdown 源码" : DisplayTitle;
        Background = UI.Brush(NoteColors.Get(_note.Color).Paper); _editor.SetNote(_note);
        _colors.Children.Clear();
        foreach (var color in NoteColors.All)
        {
            var circle = new Ellipse { Width = 10, Height = 10, Fill = UI.Brush(color.Dot), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ring = new Border { Child = circle, Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = _note.Color == color.Key ? UI.Brush("Accent") : Brushes.Transparent };
            var b = new Button { Content = ring, Width = 24, Height = 28, MinHeight = 28, Padding = new Thickness(3, 5), Classes = { "quiet", "note-tool" } };
            Avalonia.Automation.AutomationProperties.SetName(b, color.Name); ToolTip.SetTip(b, color.Name);
            b.Click += (_, _) => _store.Update(_note, n => n.Color = color.Key); _colors.Children.Add(b);
        }
    }
    private void ApplyDesktopState()
    {
        var state = _note.Desktop; Topmost = state.Topmost;
        Opacity = Math.Clamp(double.IsFinite(state.Opacity) ? state.Opacity : 1, .7, 1);
        _pin.Background = Topmost ? UI.Brush("HoverOverlay") : Brushes.Transparent;
        ToolTip.SetTip(_pin, Topmost ? "取消始终置顶" : "始终置于最前");
        Avalonia.Automation.AutomationProperties.SetName(_pin, Topmost ? "取消始终置顶" : "始终置于最前");
        _format.Background = state.ShowFormatting ? UI.Brush("HoverOverlay") : Brushes.Transparent;
        _editor.ConfigureCompact(state.ShowFormatting && !state.ReadOnly, state.FontSize);
        _editor.SetReadOnly(state.ReadOnly);
        _format.IsEnabled = !state.ReadOnly;
        _add.IsVisible = _pin.IsVisible = !state.ReadOnly;
        _readingButton.Width = state.ReadOnly ? 82 : 28;
        _readingButton.Content = state.ReadOnly ? UI.Text("退出阅读 · Esc", 10, "Muted") : UI.Icon("book", "Muted", 15);
        ToolTip.SetTip(_readingButton, state.ReadOnly ? "退出沉浸阅读 · Esc" : "沉浸阅读（只读）");
        Avalonia.Automation.AutomationProperties.SetName(_readingButton, state.ReadOnly ? "退出沉浸阅读" : "沉浸阅读（只读）");
        _colors.IsVisible = !state.ReadOnly;
        _editor.IsVisible = !state.Collapsed;
        _footer.IsVisible = _footerSurface.IsVisible = !state.Collapsed && !state.ReadOnly;
        if (state.Collapsed) _options.Hide();
        RefreshOptionChoices();
        _root.RowDefinitions[1].Height = state.Collapsed ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _root.RowDefinitions[2].Height = new GridLength(state.Collapsed || state.ReadOnly ? 0 : 40);
        MinHeight = state.Collapsed ? 44 : 220; CanResize = !state.Collapsed;
        if (state.Collapsed) Height = 44;
        else if (Height < 220) Height = Math.Max(220, state.Height);
    }
    public void ToggleReading() { Expand(); _store.UpdateDesktop(_note, d => d.ReadOnly = !d.ReadOnly); }
    public void Expand() { if (_note.Desktop.Collapsed) _store.UpdateDesktop(_note, d => d.Collapsed = false); }
    private void ToggleCollapsed()
    {
        if (!_note.Desktop.Collapsed) SaveGeometry();
        _store.UpdateDesktop(_note, d => d.Collapsed = !d.Collapsed);
    }
    private void OnChanged(Guid? id) => Dispatcher.UIThread.Post(() => {
        if (id != _note.Id) return;
        if (_note.IsDeleted) { Close(); return; }
        UpdateContent();
    });
    private void OnDesktopChanged(Guid id) => Dispatcher.UIThread.Post(() => { if (id == _note.Id) ApplyDesktopState(); });
    private void OnSaveStateChanged() => Dispatcher.UIThread.Post(() => {
        _status.Text = _store.Error is not null ? "重试保存" : _store.IsDirty ? "保存中" : "已保存";
        _status.Foreground = UI.Brush(_store.Error is not null ? "Danger" : "Muted");
        if (_store.Error is not null) ToolTip.SetTip(_status, _store.Error);
    });
    private void ScheduleGeometry()
    {
        if (_restoring || _closing) return;
        _geometryTimer.Stop(); _geometryTimer.Start();
    }
    private void SaveGeometry()
    {
        if (_restoring || WindowState != WindowState.Normal) return;
        var state = _note.Desktop;
        var width = state.Collapsed ? state.Width : Width;
        var height = state.Collapsed ? state.Height : Height;
        if (state.X == Position.X && state.Y == Position.Y && Math.Abs(state.Width - width) < 1 && Math.Abs(state.Height - height) < 1) return;
        _store.UpdateDesktop(_note, d => { d.X = Position.X; d.Y = Position.Y; d.Width = width; d.Height = height; });
    }
    private void HandleAction(string action)
    {
        if (_controller.IsClosingSession) return;
        switch (action)
        {
            case "reading": ToggleReading(); break;
            case "new": _controller.NewNote(_note.Color, Position); break;
            case "search": _controller.ShowLibrary(search: true); break;
            case "source": Expand(); _editor.ToggleSource(); break;
            case "save": _ = FlushAsync(); break;
            case "format": _store.UpdateDesktop(_note, d => d.ShowFormatting = !d.ShowFormatting); break;
            case "library": _controller.ShowLibrary(); break;
            case "collapse": ToggleCollapsed(); break;
            case "quit": _ = _controller.QuitAsync(); break;
            case "close": _controller.CloseCurrentWindow(this); break;
            case "settings": _controller.ShowSettings(); break;
        }
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _note.Desktop.ReadOnly && !_options.IsOpen) { ToggleReading(); e.Handled = true; return; }
        if (e.Key == Key.Escape && _options.IsOpen) { _options.Hide(); e.Handled = true; _editor.FocusEditor(); return; }
        if ((e.KeyModifiers & (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) == 0) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var action = e.Key switch { Key.R when shift => "reading", Key.N => "new", Key.S => "save", Key.Q => OperatingSystem.IsMacOS() ? "close" : "quit", Key.W => "close", Key.OemComma => "settings", Key.F when shift => "format", Key.F => "search", Key.O when shift => "library", Key.L when shift => "collapse", Key.M when shift => "source", _ => null };
        if (action is null) return; e.Handled = true; HandleAction(action);
    }
    private async Task ExportAsync()
    {
        if (!await FlushAsync()) return;
        var name = string.Join("_", _note.Title.Split(System.IO.Path.GetInvalidFileNameChars()));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出 Markdown", SuggestedFileName = name + ".md", DefaultExtension = "md", FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }] });
        if (file is null) return;
        try { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(_note.Markdown); }
        catch (Exception ex) { _status.Text = "导出失败"; ToolTip.SetTip(_status, ex.Message); }
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing || _controller.IsQuitting) return;
        e.Cancel = true;
        if (_closePending) return; _closePending = true;
        try
        {
            if (!await FlushAsync()) return;
            if (!_note.IsDeleted) _store.DiscardEmpty(_note);
            _store.UpdateDesktop(_note, d => d.IsOpen = false); _store.Save(); _closing = true; Close();
        }
        finally { _closePending = false; }
    }
    public void FocusEditor() => _editor.FocusEditor();
    public string PaperColor => _note.Color;
    public async Task<bool> FlushAsync()
    {
        SaveGeometry();
        if (!await _editor.FlushAsync() && _editor.IsReady)
        { _status.Text = "无法读取内容"; ToolTip.SetTip(_status, "请稍后重试，当前便签已保留。"); return false; }
        return _store.Save();
    }
    public async Task<bool> PrepareSessionEndAsync()
    {
        if (!await FlushAsync()) return false;
        _store.DiscardEmpty(_note); return true;
    }
    public void CloseForSessionEnd() { _closing = true; Close(); }
}
