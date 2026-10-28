using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace PaperNote.Services.Platform;

[SupportedOSPlatform("macos")]
internal static class MacNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] internal static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] internal static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern IntPtr Send(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern IntPtr SendPtr(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint SendInteger(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SendIntegerBool(IntPtr target, IntPtr selector, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendVoidPtr(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SendError(IntPtr target, IntPtr selector, out IntPtr error);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendVoid(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendUtf8(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    internal static IntPtr Sel(string name) => sel_registerName(name);
    internal static IntPtr String(string value) => SendUtf8(objc_getClass("NSString"), Sel("stringWithUTF8String:"), value);
    internal static string Text(IntPtr value) => value == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Send(value, Sel("UTF8String"))) ?? "";
    internal static IntPtr App => Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
    internal static IntPtr KeyWindow => Send(App, Sel("keyWindow"));
    internal static string BundlePath => Text(Send(Send(objc_getClass("NSBundle"), Sel("mainBundle")), Sel("bundlePath")));
    internal static string DockStatus { get; private set; } = "尚未检查";
    internal static void EnsureDockIcon()
    {
        var app = App;
        var before = SendInteger(app, Sel("activationPolicy"));
        _ = SendIntegerBool(app, Sel("setActivationPolicy:"), 0); // NSApplicationActivationPolicyRegular
        var imagePath = Path.Combine(BundlePath, "Contents", "Resources", "PaperNote.icns");
        var loaded = false;
        if (File.Exists(imagePath))
        {
            var image = SendPtr(Send(objc_getClass("NSImage"), Sel("alloc")), Sel("initWithContentsOfFile:"), String(imagePath));
            if (image != IntPtr.Zero) { SendVoidPtr(app, Sel("setApplicationIconImage:"), image); loaded = true; SendVoid(image, Sel("release")); }
        }
        var after = SendInteger(app, Sel("activationPolicy"));
        DockStatus = $"Dock 策略 {before} → {after} · 图标{(loaded ? "已加载" : "未加载")} · {(after == 0 ? "正常" : "异常")}";
    }
}
