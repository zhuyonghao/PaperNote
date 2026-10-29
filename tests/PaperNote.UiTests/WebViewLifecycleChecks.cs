using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using PaperNote.Models;
using PaperNote.Views;

internal static class WebViewLifecycleChecks
{
    // Separate opt-in suite: requires an installed native browser runtime.
    public static void Run()
    {
        var note = new Note { Markdown = "# Native editor\n\n最后一次输入 📝" };
        var pane = new EditorPane(compact: true) { IsVisible = false };
        pane.SetNote(note);
        var window = new Window { Content = pane, Width = 320, Height = 300, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(-10000, -10000) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Check(!pane.Children.OfType<NativeWebView>().Any() && !pane.IsReady,
                "a collapsed editor does not attach or start a native browser");
            pane.IsVisible = true;
            WaitUntil(() => pane.IsReady);
            var web = pane.Children.OfType<NativeWebView>().Single();
            WaitUntil(() => Script<string>(web, "paper.getMarkdown()") == note.Markdown);
            Check(true, "expanding initializes the editor with the original note");

            if (OperatingSystem.IsWindows())
            {
                Check(TargetLevel(web) == 0, "a visible Windows editor uses the normal memory target");
                pane.IsVisible = false; Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 1, "collapsing switches WebView2 to its low memory target");
                pane.IsVisible = true; Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 0, "expanding restores the normal memory target");
                window.Hide(); Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 1, "hiding the window switches WebView2 to low memory");
                window.Show(); Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 0, "showing the window restores normal memory");
                window.WindowState = WindowState.Minimized; Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 1, "minimizing a document switches WebView2 to low memory");
                window.WindowState = WindowState.Normal; Dispatcher.UIThread.RunJobs();
                Check(TargetLevel(web) == 0, "restoring a document restores normal memory");
            }

            Script<int>(web, "window.configures = 0; const configure = paper.configure; paper.configure = options => { window.configures++; configure(options); }; 0");
            for (var i = 0; i < 100; i++) { pane.ConfigureCompact(false, 15); pane.SetReadOnly(false); }
            Check(Complete(pane.FlushAsync()) && Script<int>(web, "window.configures") == 0,
                "unchanged geometry/settings do not resend editor configuration");

            pane.SetViewMode("source");
            Check(Complete(pane.FlushAsync()) && Script<int>(web, "document.querySelectorAll('.tiptap').length") == 0,
                "the native source view releases its rich document");
            Script<int>(web, "const source = document.querySelector('#source'); source.value = '尚未同步的最后修改 📝'; source.dispatchEvent(new Event('input', { bubbles: true })); 0");
            Check(Complete(pane.FlushAsync()) && note.Markdown == "尚未同步的最后修改 📝",
                "saving synchronously reads pending input before the debounce fires");
            pane.IsVisible = false; Dispatcher.UIThread.RunJobs();
            Check(Complete(pane.FlushAsync()) && note.Markdown == "尚未同步的最后修改 📝",
                "a hidden low-memory editor still flushes safely");
            pane.IsVisible = true; pane.SetViewMode("rich");
            Check(Complete(pane.FlushAsync()) && Script<string>(web, "document.querySelector('.tiptap').textContent") == note.Markdown,
                "restoring rich mode keeps the final source edit");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        WaitUntil(() => !pane.IsReady);
        Check(true, "closing destroys the native adapter and clears its ready state");
    }

    private static T Script<T>(NativeWebView web, string script)
    {
        var result = Complete(web.InvokeScript(script));
        return JsonSerializer.Deserialize<T>(result!)!;
    }
    private static T Complete<T>(Task<T> task)
    {
        WaitUntil(() => task.IsCompleted);
        return task.GetAwaiter().GetResult();
    }
    private static void WaitUntil(Func<bool> condition)
    {
        if (condition()) return;
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var checking = false;
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => {
            if (checking) return;
            checking = true;
            try {
                if (condition()) frame.Continue = false;
                else if (watch.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("Native editor did not reach the expected state");
            }
            catch (Exception ex) { failure = ex; frame.Continue = false; }
            finally { checking = false; }
        };
        timer.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { timer.Stop(); }
        if (failure is not null) throw failure;
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReadTargetLevel(IntPtr instance, out int level);
    private static int TargetLevel(NativeWebView web)
    {
        var handle = (IWindowsWebView2PlatformHandle)web.TryGetPlatformHandle()!;
        var iid = new Guid("6921F954-79B0-437F-A997-C85811897C68");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(handle.CoreWebView2, in iid, out var memory));
        try
        {
            var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(memory), 119 * IntPtr.Size);
            Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<ReadTargetLevel>(method)(memory, out var level));
            return level;
        }
        finally { Marshal.Release(memory); }
    }
}
