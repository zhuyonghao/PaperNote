using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaperNote.Models;
using PaperNote.Services;
namespace PaperNote.Views;

public sealed class MainWindow : Window
{
    private readonly NoteStore _store;
    private readonly DesktopController _controller;
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal };
    private readonly Border _board = new();
    private readonly TextBox _search = new() { PlaceholderText = "搜索标题或内容", Classes = { "search" }, Height = 38, MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _heading = UI.Text("全部便签", 26, "Text", FontWeight.SemiBold);
    private readonly TextBlock _summary = UI.Text("", 12, "Muted");
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly Dictionary<string, TextBlock> _counts = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly HashSet<Guid> _selected = [];
    private readonly StackPanel _selectionBar = UI.Stack(Orientation.Horizontal, 7);
    private readonly Button _manage = new() { Content = "管理便签", Classes = { "quiet" }, FontSize = 12 };
    private readonly TextBlock _selectionCount = UI.Text("", 12, "Muted");
    private readonly Button _batchDelete = new() { Content = "移到最近删除", Classes = { "quiet", "danger" } };
    private readonly Button _batchRestore = new() { Content = "恢复所选", Classes = { "quiet" } };
    private bool _managing;
    private List<Note> _visibleNotes = [];
    private string _filter = "all";
    private bool _sortByTitle, _closing;
    public MainWindow(NoteStore store, DesktopController controller)
    {
        _store = store; _controller = controller;
        Classes.Add("library");
        Title = "便签库 · 纸间"; Icon = UI.AppIcon(); Width = 1120; Height = 760; MinWidth = 800; MinHeight = 560;
        Background = UI.Brush("Canvas"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 56;
        if (OperatingSystem.IsWindows()) Classes.Add("custom-titlebar");
        var root = new Grid { RowDefinitions = new RowDefinitions("56,*") }; Content = root;
        var titlebar = new Grid { ColumnDefinitions = new ColumnDefinitions("224,*,Auto"), Background = UI.Brush("Canvas") };
        titlebar.PointerPressed += (_, e) => { if (e.Source is Control c && (c is Button || c.FindAncestorButton())) return; if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        var brand = UI.Stack(Orientation.Horizontal, 8); brand.Margin = new Thickness(OperatingSystem.IsMacOS() ? 83 : 20, 0, 0, 0);
        brand.Children.Add(new Border { Width = 30, Height = 30, Background = UI.Brush("Accent"), CornerRadius = new CornerRadius(9), Child = UI.Icon("notes", "White", 17) });
        brand.Children.Add(UI.Text("纸间", 16, "Text", FontWeight.SemiBold)); titlebar.Children.Add(brand);
        var label = UI.Stack(Orientation.Horizontal, 8); label.Margin = new Thickness(32, 0, 0, 0);
        label.Children.Add(UI.Text("工作空间", 11, "Faint")); label.Children.Add(UI.Text("/", 11, "Faint")); label.Children.Add(UI.Text("便签库", 11, "Muted"));
        Grid.SetColumn(label, 1); titlebar.Children.Add(label);
        var actions = UI.Stack(Orientation.Horizontal, 10); actions.Margin = new Thickness(0, 0, OperatingSystem.IsWindows() ? 145 : 24, 0);
        var desktop = new Button { Content = controller.DesktopHidden ? "显示便签" : "隐藏便签", Classes = { "quiet" }, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        desktop.Click += (_, _) => controller.ToggleVisibility(); actions.Children.Add(desktop);
        void RefreshVisibility() => desktop.Content = controller.DesktopHidden ? "显示便签" : "隐藏便签";
        controller.VisibilityChanged += RefreshVisibility;
        Closed += (_, _) => controller.VisibilityChanged -= RefreshVisibility;
        ToolTip.SetTip(desktop, "暂时隐藏或恢复桌面便签，可在设置中自定义快捷键");
        var addContent = UI.Stack(Orientation.Horizontal, 7); addContent.Children.Add(UI.Icon("plus", "White", 14)); addContent.Children.Add(UI.Text("新建便签", 12, "White", FontWeight.Medium));
        var add = new Button { Content = addContent, Classes = { "primary" }, VerticalAlignment = VerticalAlignment.Center, Height = 36 }; add.Click += (_, _) => controller.NewNote();
        Avalonia.Automation.AutomationProperties.SetName(add, "新建便签");
        Grid.SetColumn(actions, 2); titlebar.Children.Add(actions); root.Children.Add(titlebar);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("224,*") }; Grid.SetRow(body, 1); root.Children.Add(body);
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = UI.Brush("Chrome") };
        var navigation = UI.Stack(Orientation.Vertical, 5); navigation.Margin = new Thickness(14, 24, 14, 0);
        var section = UI.Text("便签空间", 10, "Faint"); section.Margin = new Thickness(12, 0, 0, 14); navigation.Children.Add(section);
        AddNavigation(navigation, "all", "全部便签", "notes"); AddNavigation(navigation, "desktop", "桌面上", "pop");
        AddNavigation(navigation, "pinned", "已收藏", "pin"); AddNavigation(navigation, "trash", "最近删除", "trash");
        sidebar.Children.Add(navigation);
        var bottom = UI.Stack(Orientation.Vertical, 8); bottom.Margin = new Thickness(16, 0, 16, 20);
        var markdown = new Button { Content = SidebarAction("code", "Markdown 编辑器"), Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 11 };
        markdown.Click += (_, _) => controller.ShowMarkdownEditor(); bottom.Children.Add(markdown);
        Avalonia.Automation.AutomationProperties.SetName(markdown, "Markdown 编辑器");
        var import = new Button { Content = SidebarAction("import", "导入 Markdown"), Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 11 };
        import.Click += async (_, _) => await ImportAsync(); bottom.Children.Add(import);
        Avalonia.Automation.AutomationProperties.SetName(import, "导入 Markdown");
        bottom.Children.Add(new Border { Height = 1, Background = UI.Brush("Border"), Margin = new Thickness(4, 6) });
        var settings = new Button { Content = SidebarAction("settings", "设置与快捷键"), Classes = { "settings-link" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        settings.Click += (_, _) => controller.ShowSettings(); bottom.Children.Add(settings);
        Avalonia.Automation.AutomationProperties.SetName(settings, "设置与快捷键");
        var local = UI.Stack(Orientation.Horizontal, 7); local.Margin = new Thickness(12, 8, 0, 0);
        local.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = UI.Brush("#89A594"), VerticalAlignment = VerticalAlignment.Center });
        local.Children.Add(UI.Text("已保存在本机", 10, "Faint")); bottom.Children.Add(local);
        Grid.SetRow(bottom, 2); sidebar.Children.Add(bottom); body.Children.Add(new Border { Child = sidebar, BorderBrush = UI.Brush("Border"), BorderThickness = new Thickness(0, 0, 1, 0) });

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(32, 28, 16, 16) }; _board.Child = content;
        Grid.SetColumn(_board, 1); body.Children.Add(_board);
        var top = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 16, 0) };
        var headline = UI.Stack(Orientation.Vertical, 8); headline.Children.Add(_heading); headline.Children.Add(_summary); top.Children.Add(headline);
        Grid.SetColumn(add, 1); top.Children.Add(add);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 26, 0, 24), ColumnSpacing = 10 };
        var searchBox = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,Auto"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var searchIcon = UI.Icon("search", "Faint", 15); searchIcon.HorizontalAlignment = HorizontalAlignment.Center; searchBox.Children.Add(searchIcon);
        _search.BorderThickness = new Thickness(0); _search.Background = Brushes.Transparent; _search.Padding = new Thickness(0, 8); Grid.SetColumn(_search, 1); searchBox.Children.Add(_search);
        var shortcut = UI.Pill(OperatingSystem.IsMacOS() ? "⌘ F" : "Ctrl F"); shortcut.VerticalAlignment = VerticalAlignment.Center; shortcut.Margin = new Thickness(6, 0, 10, 0); Grid.SetColumn(shortcut, 2); searchBox.Children.Add(shortcut);
        toolbar.Children.Add(new Border { Background = UI.Brush("Surface"), BorderBrush = UI.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = searchBox });
        var sort = new Button { Content = SidebarAction("sort", "最近编辑"), Classes = { "quiet" }, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        Avalonia.Automation.AutomationProperties.SetName(sort, "切换排序方式");
        sort.Click += (_, _) => { _sortByTitle = !_sortByTitle; sort.Content = SidebarAction("sort", _sortByTitle ? "标题排序" : "最近编辑"); Refresh(); }; Grid.SetColumn(sort, 1); toolbar.Children.Add(sort);
        _manage.Click += (_, _) => { _managing = !_managing; _selected.Clear(); Refresh(); };
        Grid.SetColumn(_manage, 2); toolbar.Children.Add(_manage);
        _search.TextChanged += (_, _) => Refresh(); Grid.SetRow(toolbar, 1); Grid.SetColumnSpan(toolbar, 2); top.Children.Add(toolbar); content.Children.Add(top);
        _selectionBar.Margin = new Thickness(0, 0, 14, 16);
        _selectionBar.Children.Add(_selectionCount);
        var selectAll = new Button { Content = "全选", Classes = { "quiet" } };
        selectAll.Click += (_, _) => { foreach (var n in _visibleNotes) _selected.Add(n.Id); Refresh(); }; _selectionBar.Children.Add(selectAll);
        var clear = new Button { Content = "取消选择", Classes = { "quiet" } };
        clear.Click += (_, _) => { _selected.Clear(); Refresh(); }; _selectionBar.Children.Add(clear);
        _batchRestore.Click += (_, _) => { foreach (var n in _store.Notes.Where(n => _selected.Contains(n.Id)).ToArray()) _store.Update(n, x => x.IsDeleted = false); _selected.Clear(); Refresh(); };
        _selectionBar.Children.Add(_batchRestore);
        _batchDelete.Click += async (_, _) => await DeleteSelectedAsync(); _selectionBar.Children.Add(_batchDelete);
        Grid.SetRow(_selectionBar, 1); content.Children.Add(_selectionBar);
        var scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 2); content.Children.Add(scroll);
        _cards.SizeChanged += (_, _) => LayoutCards();
        _refresh.Tick += (_, _) => { _refresh.Stop(); Refresh(); };
        _store.Changed += NoteChanged; _store.DesktopChanged += DesktopChanged;
        KeyDown += (_, e) => {
            if ((e.KeyModifiers & (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) == 0) return;
            if (e.Key == Key.N) { e.Handled = true; controller.NewNote(); }
            else if (e.Key == Key.F) { e.Handled = true; FocusSearch(); }
            else if (e.Key == Key.OemComma) { e.Handled = true; controller.ShowSettings(); }
            else if (e.Key == Key.Q) { e.Handled = true; if (OperatingSystem.IsMacOS()) controller.CloseCurrentWindow(this); else _ = controller.QuitAsync(); }
            else if (e.Key == Key.W) { e.Handled = true; controller.CloseCurrentWindow(this); }
        };
        Closing += (_, e) => { if (_closing || controller.IsQuitting) return; e.Cancel = true; _store.Save(); Hide(); };
        Closed += (_, _) => { _store.Changed -= NoteChanged; _store.DesktopChanged -= DesktopChanged; _refresh.Stop(); };
        Refresh();
    }
    private static Control SidebarAction(string icon, string label)
    {
        var row = UI.Stack(Orientation.Horizontal, 9);
        row.Children.Add(UI.Icon(icon, "Muted", 14)); row.Children.Add(UI.Text(label, 11, "Muted")); return row;
    }
    private void AddNavigation(StackPanel panel, string key, string title, string icon)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("29,*,Auto") }; row.Children.Add(UI.Icon(icon, "Muted", 16));
        var text = UI.Text(title, 12, "Muted"); Grid.SetColumn(text, 1); row.Children.Add(text);
        var count = UI.Text("", 10, "Faint"); Grid.SetColumn(count, 2); row.Children.Add(count); _counts[key] = count;
        var button = new Button { Content = row, Classes = { "nav" }, Height = 42 }; Avalonia.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => SetFilter(key); _navigation[key] = button; panel.Children.Add(button);
    }
    private void SetFilter(string key) { _filter = key; _selected.Clear(); Refresh(); }
    private bool Matches(Note note) => _filter switch { "all" => !note.IsDeleted, "desktop" => !note.IsDeleted && note.Desktop.IsOpen, "pinned" => !note.IsDeleted && note.IsPinned, "trash" => note.IsDeleted, _ => !note.IsDeleted && note.Color == _filter };
    private void Refresh()
    {
        foreach (var (key, button) in _navigation) button.Classes.Set("selected", key == _filter);
        foreach (var (key, text) in _counts) text.Text = _store.Notes.Count(n => key switch { "all" => !n.IsDeleted, "desktop" => !n.IsDeleted && n.Desktop.IsOpen, "pinned" => !n.IsDeleted && n.IsPinned, _ => n.IsDeleted }).ToString();
        var notes = _store.Notes.Where(Matches).Where(n => string.IsNullOrWhiteSpace(_search.Text) || n.Markdown.Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
        notes = _sortByTitle ? notes.OrderBy(n => n.Title) : notes.OrderByDescending(n => n.UpdatedAt);
        var list = notes.ToList(); _visibleNotes = list;
        _selected.IntersectWith(list.Select(n => n.Id));
        _manage.Content = _managing ? "完成管理" : "管理便签";
        _selectionBar.IsVisible = _managing;
        _selectionCount.Text = $"已选择 {_selected.Count} 张";
        _batchDelete.Content = _filter == "trash" ? "永久删除所选…" : "移到最近删除";
        _batchDelete.IsEnabled = _selected.Count > 0;
        _batchRestore.IsVisible = _filter == "trash"; _batchRestore.IsEnabled = _selected.Count > 0;
        _heading.Text = _filter switch { "all" => "全部便签", "desktop" => "桌面便签", "pinned" => "已收藏", "trash" => "最近删除", _ => NoteColors.Get(_filter).Name };
        _summary.Text = _filter == "trash" ? $"{list.Count} 张便签 · 可恢复或永久删除" : $"{list.Count} 张便签 · 让想法有处安放";
        _cards.Children.Clear();
        foreach (var note in list) _cards.Children.Add(CreateCard(note));
        if (list.Count == 0)
        {
            var empty = UI.Stack(Orientation.Vertical, 10); empty.Margin = new Thickness(20, 65); empty.Children.Add(UI.Text("这里留着一点空白", 18, "Muted", FontWeight.Medium)); empty.Children.Add(UI.Text(!string.IsNullOrWhiteSpace(_search.Text) ? "换一个关键词，再找找看。" : _filter == "trash" ? "没有需要恢复或清理的便签。" : "新建一张便签，写下第一个想法。", 12, "Faint")); _cards.Children.Add(empty);
        }
        LayoutCards();
    }
    private Button CreateCard(Note note)
    {
        var color = NoteColors.Get(note.Color);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, -6, 0) };
        if (_managing) { var selection = UI.Icon(_selected.Contains(note.Id) ? "check" : "square", "Accent", 17); selection.HorizontalAlignment = HorizontalAlignment.Left; tools.Children.Add(selection); }
        else if (note.IsPinned) { var pin = UI.Icon("pin", "Muted", 14); pin.HorizontalAlignment = HorizontalAlignment.Left; tools.Children.Add(pin); }
        var remove = UI.IconButton(note.IsDeleted ? "restore" : "trash", note.IsDeleted ? "恢复便签" : "移到最近删除", () => {
            if (note.IsDeleted) _store.Update(note, n => n.IsDeleted = false);
            else _ = _controller.TrashNotesAsync([note]);
        });
        remove.Classes.Add("card-action");
        remove.Width = 28; remove.Height = 28; remove.Padding = new Thickness(6); remove.IsVisible = !_managing;
        Grid.SetColumn(remove, 1); tools.Children.Add(remove);

        var title = UI.Text(note.Title, 15, "Text", FontWeight.SemiBold); title.TextWrapping = TextWrapping.Wrap; title.MaxLines = 2; title.LineHeight = 21; title.TextTrimming = TextTrimming.CharacterEllipsis; title.VerticalAlignment = VerticalAlignment.Top; Grid.SetRow(title, 0); grid.Children.Add(title);
        var preview = UI.Text(note.Preview, 13, "Muted"); preview.TextWrapping = TextWrapping.Wrap; preview.MaxLines = 4; preview.LineHeight = 21; preview.TextTrimming = TextTrimming.CharacterEllipsis; preview.VerticalAlignment = VerticalAlignment.Top; preview.Margin = new Thickness(0, 13, 0, 12); Grid.SetRow(preview, 1); grid.Children.Add(preview);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        footer.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = UI.Brush(color.Dot), VerticalAlignment = VerticalAlignment.Center });
        var date = UI.Text(RelativeTime(note.UpdatedAt), 10, "Faint"); date.Margin = new Thickness(7, 0, 0, 0); Grid.SetColumn(date, 1); footer.Children.Add(date);
        var state = UI.Text(note.IsDeleted ? "已删除" : note.Desktop.IsOpen ? "桌面上" : note.IsPinned ? "已收藏" : "打开", 10, note.Desktop.IsOpen ? "Accent" : "Faint"); var badge = new Border { Child = state, Background = UI.Brush("Chrome"), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 3), Margin = new Thickness(4, 0, 6, 0) };
        Grid.SetColumn(badge, 2); footer.Children.Add(badge);
        Grid.SetColumn(tools, 3); footer.Children.Add(tools);
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var button = new Button { Content = grid, Height = 224, Margin = new Thickness(0, 0, 16, 16), Background = UI.Brush("Paper-" + color.Key), BorderBrush = UI.Brush("Border"), Classes = { "note-card" }, Padding = new Thickness(22, 22, 18, 14) };
        button.Template = new FuncControlTemplate<Button>((b, _) => {
            var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = b.Padding, Child = new ContentPresenter { Content = b.Content, Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch } };
            frame.Classes.Add("card-frame");
            frame.Bind(Border.BackgroundProperty, b.GetObservable(BackgroundProperty));
            frame.Bind(Border.BorderBrushProperty, b.GetObservable(BorderBrushProperty));
            return frame;
        });
        if (_selected.Contains(note.Id)) button.BorderBrush = UI.Brush("Accent");
        Avalonia.Automation.AutomationProperties.SetName(button, note.Title); ToolTip.SetTip(button, note.IsDeleted ? "点击恢复便签" : "点击打开桌面便签；右键可管理");
        button.Click += (_, e) => {
            if (e.Source is Control source && source != button && (source is Button || source.FindAncestorButton())) return;
            if (_managing) { if (!_selected.Add(note.Id)) _selected.Remove(note.Id); Refresh(); }
            else if (note.IsDeleted) _store.Update(note, n => n.IsDeleted = false);
            else _controller.OpenNote(note);
        };
        var menu = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.FlyoutPresenterClasses.Add("note-options");
        var menuItems = UI.Stack(Orientation.Vertical, 3); menuItems.Width = 210; menu.Content = menuItems;
        void Item(string label, string icon, Action action, bool danger = false)
        {
            var content = UI.Stack(Orientation.Horizontal, 10);
            content.Children.Add(UI.Icon(icon, danger ? "Danger" : "Muted", 15));
            content.Children.Add(UI.Text(label, 12, danger ? "Danger" : "Text"));
            var item = new Button { Content = content, Classes = { "quiet", "note-option" }, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 8) };
            Avalonia.Automation.AutomationProperties.SetName(item, label);
            item.Click += (_, _) => { menu.Hide(); action(); }; menuItems.Children.Add(item);
        }
        if (note.IsDeleted)
        {
            Item("恢复便签", "restore", () => _store.Update(note, n => n.IsDeleted = false));
            Item("永久删除…", "trash", async () => { if (await ConfirmAsync("永久删除这张便签？", "删除后无法恢复。")) _store.RemovePermanently(note); }, danger: true);
        }
        else
        {
            Item("打开桌面便签", "pop", () => _controller.OpenNote(note));
            Item(note.IsPinned ? "取消收藏" : "收藏便签", "pin", () => _store.Update(note, n => n.IsPinned = !n.IsPinned));
            Item("导出 Markdown…", "export", () => _ = ExportAsync(note)); menuItems.Children.Add(new Border { Height = 1, Background = UI.Brush("Border"), Margin = new Thickness(4, 6) });
            Item("移到最近删除", "trash", () => _ = _controller.TrashNotesAsync([note]), danger: true);
        }
        button.ContextFlyout = menu;
        var more = UI.IconButton("more", "更多便签操作", () => menu.ShowAt(button));
        more.Classes.Add("card-action");
        more.Width = 28; more.Height = 28; more.Padding = new Thickness(6); more.IsVisible = !_managing;
        Grid.SetColumn(more, 2); tools.Children.Add(more);
        return button;
    }
    private async Task DeleteSelectedAsync()
    {
        var notes = _visibleNotes.Where(n => _selected.Contains(n.Id)).ToArray();
        if (_filter == "trash")
        {
            if (!await ConfirmAsync($"永久删除这 {notes.Length} 张便签？", "删除后无法恢复。")) return;
            foreach (var note in notes) _store.RemovePermanently(note);
        }
        else await _controller.TrashNotesAsync(notes);
        _selected.Clear(); Refresh();
    }
    private void LayoutCards()
    {
        // Use the arranged viewport: the board also includes padding and the
        // vertical scrollbar. Rounding up can push the last card onto a new row.
        var available = _cards.Bounds.Width;
        if (available <= 0) return;
        var columns = Math.Max(1, (int)(available / 264));
        var width = Math.Max(1, Math.Min(344, Math.Floor(available / columns) - 16));
        foreach (var card in _cards.Children.OfType<Button>()) card.Width = width;
    }
    private static string RelativeTime(DateTimeOffset date)
    {
        var diff = DateTimeOffset.Now - date;
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} 分钟前";
        if (date.Date == DateTimeOffset.Now.Date) return $"今天 {date:HH:mm}";
        if (date.Date == DateTimeOffset.Now.AddDays(-1).Date) return "昨天";
        return date.ToString("M月d日");
    }
    private void NoteChanged(Guid? _) => Dispatcher.UIThread.Post(() => { _refresh.Stop(); _refresh.Start(); });
    private void DesktopChanged(Guid _) => NoteChanged(null);
    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入便签", AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("Markdown / 纯文本") { Patterns = ["*.md", "*.markdown", "*.txt"] }] });
        try
        {
            foreach (var file in files) { await using var stream = await file.OpenReadAsync(); using var reader = new StreamReader(stream); _store.Add(await reader.ReadToEndAsync(), _controller.Preferences.Current.DefaultColor); }
            if (files.Count > 0) { _filter = "all"; _search.Text = ""; Refresh(); }
        }
        catch (Exception ex) { await MessageAsync("导入失败", ex.Message); }
    }
    private async Task ExportAsync(Note note)
    {
        var name = string.Join("_", note.Title.Split(System.IO.Path.GetInvalidFileNameChars()));
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出 Markdown", SuggestedFileName = name + ".md", DefaultExtension = "md", FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }] });
        if (file is null) return;
        try { await using var stream = await file.OpenWriteAsync(); stream.SetLength(0); await using var writer = new StreamWriter(stream); await writer.WriteAsync(note.Markdown); }
        catch (Exception ex) { await MessageAsync("导出失败", ex.Message); }
    }
    private Task MessageAsync(string title, string message) => DialogAsync(title, message, false);
    private Task<bool> ConfirmAsync(string title, string message) => DialogAsync(title, message, true);
    private async Task<bool> DialogAsync(string title, string message, bool confirm)
    {
        var dialog = new Window { Title = title, Width = 390, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = UI.Stack(Orientation.Vertical, 18); stack.Margin = new Thickness(25); stack.Children.Add(UI.Text(title, 17, "Text", FontWeight.SemiBold)); var text = UI.Text(message, 12); text.TextWrapping = TextWrapping.Wrap; stack.Children.Add(text);
        var buttons = UI.Stack(Orientation.Horizontal, 10); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        if (confirm) { var cancel = new Button { Content = "取消" }; cancel.Click += (_, _) => dialog.Close(false); buttons.Children.Add(cancel); }
        var ok = new Button { Content = confirm ? "确定" : "知道了", Classes = { "primary" } }; ok.Click += (_, _) => dialog.Close(true); buttons.Children.Add(ok); stack.Children.Add(buttons); dialog.Content = stack;
        return await dialog.ShowDialog<bool>(this);
    }
    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }
    public Task FlushAsync() { _store.Save(); return Task.CompletedTask; }
    public void CloseForShutdown() { _closing = true; Close(); }
}
