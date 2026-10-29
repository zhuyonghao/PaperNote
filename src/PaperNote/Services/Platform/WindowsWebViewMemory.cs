using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace PaperNote.Services.Platform;

internal static class WindowsWebViewMemory
{
    private static readonly Guid MemoryInterface = new("6921F954-79B0-437F-A997-C85811897C68");
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetTargetLevel(IntPtr instance, int level);

    public static bool SetLowMemory(NativeWebView web, bool low)
    {
        if (!OperatingSystem.IsWindows() || web.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle
            || handle.CoreWebView2 == IntPtr.Zero) return false;
        var iid = MemoryInterface;
        // Older runtimes can keep working without this optional optimization.
        if (Marshal.QueryInterface(handle.CoreWebView2, in iid, out var memory) < 0) return false;
        try
        {
            // WebView2 SDK's ICoreWebView2_19 vtable: IUnknown (0..2), inherited
            // methods (3..118), get_MemoryUsageTargetLevel (119), put (120).
            // ABI verified against Microsoft.Web.WebView2 1.0.1823.32 WebView2.h;
            // https://learn.microsoft.com/microsoft-edge/webview2/reference/win32/icorewebview2_19
            // Call the borrowed COM interface directly; do not create or release
            // a shared RCW owned by Avalonia, or retain the native view after close.
            var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(memory), 120 * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<SetTargetLevel>(method)(memory, low ? 1 : 0) >= 0;
        }
        finally { Marshal.Release(memory); }
    }
}
