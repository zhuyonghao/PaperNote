using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PaperNote.Services;
using PaperNote.Views;

internal static class Program
{
    // Requires a desktop session. Exercise native window visibility without
    // keyboard/mouse input, system integration, WebViews or real user data.
    [STAThread]
    private static void Main()
    {
        AppBuilder.Configure<TestApp>().UsePlatformDetect().SetupWithoutStarting();
        var directory = Path.Combine(Path.GetTempPath(), "papernote-ui-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        MainWindow? window = null;
        try
        {
            using var store = new NoteStore(Path.Combine(directory, "notes.json"));
            var lifetime = new ClassicDesktopStyleApplicationLifetime();
            var controller = new DesktopController(store, lifetime, new SettingsStore(directory));
            window = new MainWindow(store, controller)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(-10000, -10000), ShowInTaskbar = false
            };
            var cards = Field<WrapPanel>(window, "_cards");
            Check(cards.Children.Count == 0, "a hidden library starts without card controls");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var original = Snapshot(cards);
            Check(original.Count == store.Notes.Count, "showing the library creates the current cards");
            Refresh(window);
            Check(Snapshot(cards).All(pair => ReferenceEquals(pair.Value, original[pair.Key])),
                "refreshing unchanged data reuses every card");

            var edited = store.Notes.MaxBy(n => n.UpdatedAt)!;
            store.UpdateMarkdown(edited.Id, "# 更新后的标题\n\n最新正文");
            Refresh(window);
            var updated = Snapshot(cards);
            Check(!ReferenceEquals(updated[edited.Id], original[edited.Id])
                && updated.Where(pair => pair.Key != edited.Id).All(pair => ReferenceEquals(pair.Value, original[pair.Key])),
                "editing one note replaces its card and reuses the others");

            var search = Field<TextBox>(window, "_search");
            search.Text = "最新正文"; Refresh(window);
            Check(cards.Children.Count == 1 && cards.Children[0] == updated[edited.Id], "searching retains matching card controls");
            search.Text = "不存在的内容"; Refresh(window); Refresh(window);
            Check(cards.Children.Count == 1 && cards.Children[0] is not Button, "empty results keep exactly one empty state");
            search.Text = ""; Refresh(window);

            Field<Button>(window, "_manage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var managing = Snapshot(cards);
            managing[edited.Id].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selected = Snapshot(cards);
            Check(Field<HashSet<Guid>>(window, "_selected").SetEquals([edited.Id])
                && !ReferenceEquals(selected[edited.Id], managing[edited.Id])
                && selected.Where(pair => pair.Key != edited.Id).All(pair => ReferenceEquals(pair.Value, managing[pair.Key])),
                "selecting a note updates only its card and preserves selection by ID");

            window.Hide();
            Check(cards.Children.Count == 0 && Field<System.Collections.IDictionary>(window, "_renderedCards").Count == 0,
                "hiding the library releases cards and their cached state");
            store.UpdateMarkdown(edited.Id, "# 隐藏期间修改\n\n重开后显示最新内容");
            var added = store.Add("# 隐藏期间新增");
            Refresh(window);
            Check(cards.Children.Count == 0, "edits while hidden do not rebuild the library");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var reopened = Snapshot(cards);
            Check(reopened.Count == store.Notes.Count && reopened.ContainsKey(added.Id)
                && Avalonia.Automation.AutomationProperties.GetName(reopened[edited.Id]) == "隐藏期间修改",
                "reopening restores current content and notes added while hidden");
            Check(Field<HashSet<Guid>>(window, "_selected").SetEquals([edited.Id]), "reopening preserves multi-select state");
            Console.WriteLine("11 native UI checks passed.");
        }
        finally
        {
            window?.CloseForShutdown();
            Directory.Delete(directory, true);
        }
    }

    private static Dictionary<Guid, Button> Snapshot(WrapPanel cards) => cards.Children.OfType<Button>().ToDictionary(b => (Guid)b.Tag!, b => b);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Refresh(MainWindow window) => typeof(MainWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

internal sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
