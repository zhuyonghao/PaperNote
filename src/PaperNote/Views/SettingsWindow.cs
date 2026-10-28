using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PaperNote.Models;
using PaperNote.Services;
using PaperNote.Services.Platform;
namespace PaperNote.Views;

public sealed class SettingsWindow : Window
{
    private readonly DesktopController _controller;
    private readonly ToggleSwitch _startup = new() { OnContent = "开", OffContent = "关" }, _global = new() { OnContent = "开", OffContent = "关" }, _restore = new() { OnContent = "开", OffContent = "关" };
    private readonly TextBlock _startupInfo = UI.Text("", 12, "Muted");
    private readonly TextBlock _shortcutInfo = UI.Text("", 12, "Muted");
    private readonly TextBlock _message = UI.Text("", 12, "Accent");
    private readonly Button _record, _save, _approval;
    private ShortcutSpec _candidate;
    private bool _recording, _updating;
    public SettingsWindow(DesktopController controller)
    {
        _controller = controller; _candidate = controller.Preferences.Current.NewNoteShortcut;
        Title = "设置 · 纸间"; Width = 660; Height = 720; MinWidth = 600; MinHeight = 620;
        Icon = UI.AppIcon(); Background = UI.Brush("Canvas"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var body = UI.Stack(Orientation.Vertical, 20); body.Margin = new Thickness(32, 26, 32, 26); scroll.Content = body; Content = scroll;
        var title = UI.Stack(Orientation.Vertical, 5); title.Children.Add(UI.Text("让纸间按你的习惯工作", 24, "Text", FontWeight.SemiBold)); title.Children.Add(UI.Text("桌面上的小事，用顺手的方式记录。", 13, "Muted")); body.Children.Add(title);

        var appearance = UI.Stack(Orientation.Vertical, 0); appearance.Children.Add(SectionTitle("外观"));
        var modes = new ComboBox { ItemsSource = new[] { "跟随系统", "白天模式", "黑夜模式" }, MinWidth = 140,
            SelectedIndex = controller.Preferences.Current.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 } };
        Avalonia.Automation.AutomationProperties.SetName(modes, "外观模式");
        modes.SelectionChanged += (_, _) => {
            var old = controller.Preferences.Current.ThemeMode;
            var mode = modes.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            controller.Preferences.Current.ThemeMode = mode;
            if (!controller.Preferences.Save()) { controller.Preferences.Current.ThemeMode = old; ShowMessage(controller.Preferences.Error!, true); return; }
            AppTheme.Apply(mode);
        };
        appearance.Children.Add(Row("日夜模式", "便签库、桌面便签与阅读页面同步切换。", modes)); body.Children.Add(Card(appearance));

        var general = UI.Stack(Orientation.Vertical, 0); general.Children.Add(SectionTitle("启动与桌面"));
        _startupInfo.TextWrapping = TextWrapping.Wrap;
        var state = controller.AutoStart.GetState();
        _startup.IsChecked = state is AutoStartState.Enabled or AutoStartState.RequiresApproval;
        Avalonia.Automation.AutomationProperties.SetName(_startup, "开机自启");
        general.Children.Add(Row("登录后自动启动", "开机登录时打开纸间，不用再手动寻找。", _startup));
        _startupInfo.Margin = new Thickness(20, 0, 20, 12); general.Children.Add(_startupInfo);
        _approval = new Button { Content = "前往系统设置完成授权", Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 0, 12) };
        _approval.Click += (_, _) => controller.AutoStart.OpenApprovalSettings(); general.Children.Add(_approval); UpdateStartupDescription(state);
        general.Children.Add(Divider());
        _restore.IsChecked = controller.Preferences.Current.RestoreDesktop; Avalonia.Automation.AutomationProperties.SetName(_restore, "启动时恢复桌面便签");
        general.Children.Add(Row("恢复桌面便签", "启动时保留上次的位置、大小和置顶状态。", _restore));
        body.Children.Add(Card(general));

        var shortcut = UI.Stack(Orientation.Vertical, 0); shortcut.Children.Add(SectionTitle("随时新建"));
        _global.IsChecked = controller.Preferences.Current.GlobalShortcutEnabled; Avalonia.Automation.AutomationProperties.SetName(_global, "启用全局快捷键");
        shortcut.Children.Add(Row("全局快捷键", "在其他应用中，也能立即新建一张桌面便签。", _global));
        var capture = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 0, 20, 12), ColumnSpacing = 10 };
        _record = new Button { Content = _candidate.Display(OperatingSystem.IsMacOS()), Classes = { "shortcut" }, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 48, FontSize = 18 };
        ToolTip.SetTip(_record, "点击后按下新的快捷键；Esc 取消"); Avalonia.Automation.AutomationProperties.SetName(_record, "录制新建便签快捷键");
        _record.Click += async (_, _) => await BeginRecording(); capture.Children.Add(_record);
        _save = new Button { Content = "保存快捷键", Classes = { "primary" }, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        _save.Click += async (_, _) => await SaveShortcut(); Grid.SetColumn(_save, 1); capture.Children.Add(_save); shortcut.Children.Add(capture);
        _shortcutInfo.Text = controller.ShortcutStatus; _shortcutInfo.TextWrapping = TextWrapping.Wrap; _shortcutInfo.Margin = new Thickness(20, 0, 20, 8); shortcut.Children.Add(_shortcutInfo);
        var reset = new Button { Content = "恢复默认快捷键", Classes = { "quiet" }, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 11, Margin = new Thickness(13, 0, 0, 10) };
        reset.Click += async (_, _) => { if (_recording) await CancelRecording(); _candidate = ShortcutSpec.Default; _record.Content = _candidate.Display(OperatingSystem.IsMacOS()); await SaveShortcut(); }; shortcut.Children.Add(reset);
        body.Children.Add(Card(shortcut));

        var defaults = UI.Stack(Orientation.Vertical, 0); defaults.Children.Add(SectionTitle("新的便签"));
        var colors = UI.Stack(Orientation.Horizontal, 7); colors.Margin = new Thickness(20, 0, 20, 18);
        foreach (var color in NoteColors.All)
        {
            var swatch = new Button { Content = UI.Text(color.Name, 11, "Text"), Background = UI.Brush(color.Paper), BorderBrush = UI.Brush(color.Dot), BorderThickness = new Thickness(controller.Preferences.Current.DefaultColor == color.Key ? 2 : 1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 8) };
            swatch.Click += (_, _) => {
                var old = controller.Preferences.Current.DefaultColor; controller.Preferences.Current.DefaultColor = color.Key;
                if (!controller.Preferences.Save()) { controller.Preferences.Current.DefaultColor = old; ShowMessage(controller.Preferences.Error!, true); return; }
                foreach (var child in colors.Children.OfType<Button>()) child.BorderThickness = new Thickness(1);
                swatch.BorderThickness = new Thickness(2); ShowMessage("新建便签的默认颜色已保存。");
            }; colors.Children.Add(swatch);
        }
        defaults.Children.Add(colors); body.Children.Add(Card(defaults));
        _message.TextWrapping = TextWrapping.Wrap; body.Children.Add(_message);
        body.Children.Add(UI.Text("纸间  ·  本地记录，自由书写", 11, "Faint"));
        _startup.PropertyChanged += (_, e) => { if (!_updating && e.Property == ToggleSwitch.IsCheckedProperty) ChangeStartup(); };
        _restore.PropertyChanged += (_, e) => {
            if (_updating || e.Property != ToggleSwitch.IsCheckedProperty) return;
            var old = controller.Preferences.Current.RestoreDesktop; controller.Preferences.Current.RestoreDesktop = _restore.IsChecked == true;
            if (!controller.Preferences.Save()) { controller.Preferences.Current.RestoreDesktop = old; _updating = true; _restore.IsChecked = old; _updating = false; ShowMessage(controller.Preferences.Error!, true); }
        };
        _global.PropertyChanged += async (_, e) => {
            if (_updating || e.Property != ToggleSwitch.IsCheckedProperty) return;
            _global.IsEnabled = false;
            var result = await controller.SaveShortcutAsync(controller.Preferences.Current.NewNoteShortcut, _global.IsChecked == true);
            _updating = true; _global.IsChecked = controller.Preferences.Current.GlobalShortcutEnabled; _updating = false; _global.IsEnabled = true;
            ShowMessage(result.Success ? controller.ShortcutStatus : result.Error!, !result.Success);
        };
        controller.IntegrationChanged += RefreshStatus;
        AddHandler(KeyDownEvent, OnRecordKey, RoutingStrategies.Tunnel);
        Deactivated += async (_, _) => { if (_recording) await CancelRecording(); };
        Closed += async (_, _) => { controller.IntegrationChanged -= RefreshStatus; if (_recording) { _recording = false; await controller.RefreshShortcutAsync(); } };
    }
    private static Control SectionTitle(string text) => new Border { Child = UI.Text(text, 14, "Text", FontWeight.SemiBold), Padding = new Thickness(20, 17, 20, 7) };
    private static Border Card(Control child) => new() { Child = child, Background = UI.Brush("Surface"), BorderBrush = UI.Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13) };
    private static Border Divider() => new() { Background = UI.Brush("Border"), Height = 1, Margin = new Thickness(20, 0) };
    private static Grid Row(string title, string description, Control action)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 10, 20, 15), ColumnSpacing = 20 };
        var text = UI.Stack(Orientation.Vertical, 5); text.Children.Add(UI.Text(title, 13, "Text", FontWeight.Medium));
        var sub = UI.Text(description, 11, "Muted"); sub.TextWrapping = TextWrapping.Wrap; text.Children.Add(sub); row.Children.Add(text);
        action.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(action, 1); row.Children.Add(action); return row;
    }
    private void UpdateStartupDescription(AutoStartState state)
    {
        _startupInfo.Text = state switch { AutoStartState.Enabled => "已加入系统登录项。", AutoStartState.RequiresApproval => "系统需要你允许纸间作为登录项运行。", AutoStartState.Unsupported => "当前系统不支持此功能。macOS 需要 13 或更新版本。", _ => OperatingSystem.IsMacOS() ? "建议将纸间放到“应用程序”后开启。" : "只为当前 Windows 用户设置，无需管理员权限。" };
        _approval.IsVisible = state == AutoStartState.RequiresApproval; _startup.IsEnabled = state != AutoStartState.Unsupported;
    }
    private void ChangeStartup()
    {
        _startup.IsEnabled = false;
        var result = _controller.AutoStart.SetEnabled(_startup.IsChecked == true);
        _updating = true; _startup.IsChecked = result.State is AutoStartState.Enabled or AutoStartState.RequiresApproval; _updating = false;
        UpdateStartupDescription(result.State);
        ShowMessage(result.Success ? result.State == AutoStartState.RequiresApproval ? "请到系统设置完成登录项授权。" : "开机自启设置已更新。" : result.Error ?? "无法更新登录项，请重试。", !result.Success);
    }
    private async Task BeginRecording()
    {
        var result = await _controller.SuspendShortcutAsync();
        if (!result.Success) { ShowMessage(result.Error!, true); return; }
        _recording = true; _record.Content = "按下新的组合键…"; _record.Focus(); _save.IsEnabled = false;
        _global.IsEnabled = false;
        _shortcutInfo.Text = "支持字母、数字或空格；Esc 取消。";
    }
    private async Task CancelRecording()
    {
        _recording = false; _global.IsEnabled = true;
        _candidate = _controller.Preferences.Current.NewNoteShortcut;
        _record.Content = _candidate.Display(OperatingSystem.IsMacOS());
        await _controller.RefreshShortcutAsync();
    }
    private async void OnRecordKey(object? sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            await CancelRecording(); return;
        }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var physical = e.PhysicalKey.ToString();
        var key = physical.Length == 1 && char.IsAsciiLetter(physical[0]) ? physical
            : physical.StartsWith("Digit") ? physical[5..]
            : e.PhysicalKey == PhysicalKey.Space ? "Space"
            : e.Key is >= Key.A and <= Key.Z ? e.Key.ToString()
            : e.Key is >= Key.D0 and <= Key.D9 ? ((int)e.Key - (int)Key.D0).ToString() : "";
        var modifiers = ShortcutModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ShortcutModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= ShortcutModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ShortcutModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= ShortcutModifiers.Meta;
        var candidate = new ShortcutSpec(key, modifiers);
        if (candidate.Validate() is { } error) { _shortcutInfo.Text = error; return; }
        _recording = false; _global.IsEnabled = true; _candidate = candidate; _record.Content = candidate.Display(OperatingSystem.IsMacOS());
        await _controller.RefreshShortcutAsync(); _save.IsEnabled = true; _shortcutInfo.Text = "点击“保存快捷键”后，在所有应用中生效。";
    }
    private async Task SaveShortcut()
    {
        _save.IsEnabled = false;
        var result = await _controller.SaveShortcutAsync(_candidate, _controller.Preferences.Current.GlobalShortcutEnabled);
        ShowMessage(result.Success ? "快捷键已保存。" : result.Error!, !result.Success);
        if (!result.Success) _save.IsEnabled = true;
    }
    private void RefreshStatus() => _shortcutInfo.Text = _controller.ShortcutStatus;
    private void ShowMessage(string message, bool error = false) { _message.Text = message; _message.Foreground = UI.Brush(error ? "Danger" : "Accent"); }
}
