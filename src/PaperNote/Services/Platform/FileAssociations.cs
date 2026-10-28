using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PaperNote.Services.Platform;

internal static class FileAssociations
{
    [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [SupportedOSPlatform("windows")]
    public static void RegisterWindows()
    {
        try
        {
            var executable = Environment.ProcessPath ?? throw new IOException("找不到应用程序路径。");
            const string progId = "PaperNote.Markdown";
            const string classes = @"Software\Classes\";
            var command = $"\"{executable}\" --open \"%1\"";
            void Set(string path, string name, string value)
            {
                using var key = Registry.CurrentUser.CreateSubKey(path);
                key.SetValue(name, value, RegistryValueKind.String);
            }
            Set(classes + progId, "", "Markdown 文档 · 纸间");
            Set(classes + progId + @"\DefaultIcon", "", $"\"{executable}\",0");
            Set(classes + progId + @"\shell\open\command", "", command);
            Set(classes + @"Applications\PaperNote.exe", "FriendlyAppName", "纸间");
            Set(classes + @"Applications\PaperNote.exe\shell\open\command", "", command);
            foreach (var extension in LaunchRequest.Extensions.Where(e => e != ".txt"))
            {
                Set(classes + extension + @"\OpenWithProgids", progId, "");
                Set(classes + @"Applications\PaperNote.exe\SupportedTypes", extension, "");
                Set(@"Software\PaperNote\Capabilities\FileAssociations", extension, progId);
            }
            Set(@"Software\PaperNote\Capabilities", "ApplicationName", "纸间");
            Set(@"Software\PaperNote\Capabilities", "ApplicationDescription", "所见即所得 Markdown 编辑器与桌面便签");
            Set(@"Software\RegisteredApplications", "PaperNote", @"Software\PaperNote\Capabilities");
            // Add an Open With candidate; the user's existing default remains their choice.
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { System.Diagnostics.Trace.WriteLine("文件打开方式注册失败：" + ex.Message); }
    }
}
