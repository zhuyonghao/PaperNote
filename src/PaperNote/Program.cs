using Avalonia;
using PaperNote.Services;
using PaperNote.Services.Platform;

namespace PaperNote;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var scope = Environment.GetEnvironmentVariable("PAPERNOTE_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperNote");
        using var instance = new SingleInstanceService(Path.GetFullPath(scope));
        if (!instance.IsPrimary) return instance.SendAsync(args).GetAwaiter().GetResult() ? 0 : 1;
        App.Instance = instance; App.LaunchArguments = args;
        if (OperatingSystem.IsWindows()) WindowsTaskbar.SetIdentity();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions { ShowInDock = true })
        .With(new AvaloniaNativePlatformOptions { OverlayPopups = false })
        .With(new Win32PlatformOptions { OverlayPopups = false })
        .LogToTrace();
}
