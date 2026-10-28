using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PaperNote.Services;
using PaperNote.Views;

namespace PaperNote;
public partial class App : Application
{
    internal static SingleInstanceService? Instance { get; set; }
    internal static string[] LaunchArguments { get; set; } = [];
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") == "1") Name = "纸间测试";
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataDir = Environment.GetEnvironmentVariable("PAPERNOTE_DATA_DIR");
            var store = new NoteStore(string.IsNullOrWhiteSpace(dataDir) ? null : Path.Combine(dataDir, "notes.json"));
            var settings = new SettingsStore(Path.GetDirectoryName(store.DataPath)!);
            AppTheme.Initialize(settings.Current.ThemeMode);
            var controller = new DesktopController(store, desktop, settings);
            Instance?.SetHandler(args => Avalonia.Threading.Dispatcher.UIThread.Post(() => controller.HandleLaunch(args)));
            desktop.Startup += (_, _) => controller.Start(LaunchArguments);
            desktop.Exit += (_, _) => store.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
