using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
namespace PaperNote.Services.Platform;

public enum AutoStartState { Disabled, Enabled, RequiresApproval, Unsupported }
public sealed record AutoStartResult(bool Success, AutoStartState State, string? Error = null);
public sealed class AutoStartService
{
    private static readonly Lazy<IntPtr> MacFramework = new(() => NativeLibrary.Load("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement"));
    private readonly bool _testMode = Environment.GetEnvironmentVariable("PAPERNOTE_TEST_MODE") == "1";
    private AutoStartState _testState;
    public AutoStartState GetState()
    {
        if (_testMode) return _testState;
        if (OperatingSystem.IsMacOSVersionAtLeast(13)) return GetMacState();
        if (OperatingSystem.IsWindows()) return WindowsGet();
        return AutoStartState.Unsupported;
    }
    public AutoStartResult SetEnabled(bool enabled)
    {
        if (_testMode) { _testState = enabled ? AutoStartState.Enabled : AutoStartState.Disabled; return new(true, _testState); }
        try
        {
            if (OperatingSystem.IsMacOSVersionAtLeast(13)) return MacSet(enabled);
            if (OperatingSystem.IsWindows()) return WindowsSet(enabled);
            return new(false, AutoStartState.Unsupported, "此功能需要 macOS 13 或 Windows 10 及以上版本。");
        }
        catch (Exception ex) { return new(false, GetState(), ex.Message); }
    }
    public void OpenApprovalSettings()
    {
        if (!_testMode && OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            MacService(); MacNative.SendVoid(MacNative.objc_getClass("SMAppService"), MacNative.Sel("openSystemSettingsLoginItems"));
        }
    }
    [SupportedOSPlatform("macos13.0")]
    private static IntPtr MacService()
    {
        _ = MacFramework.Value;
        return MacNative.Send(MacNative.objc_getClass("SMAppService"), MacNative.Sel("mainAppService"));
    }
    [SupportedOSPlatform("macos13.0")]
    private static AutoStartState GetMacState()
    {
        var service = MacService();
        if (service == IntPtr.Zero) return AutoStartState.Unsupported;
        return MacNative.SendInteger(service, MacNative.Sel("status")) switch { 1 => AutoStartState.Enabled, 2 => AutoStartState.RequiresApproval, _ => AutoStartState.Disabled };
    }
    [SupportedOSPlatform("macos13.0")]
    private static AutoStartResult MacSet(bool enabled)
    {
        var service = MacService();
        if (service == IntPtr.Zero) return new(false, AutoStartState.Unsupported, "无法访问 macOS 登录项服务。");
        var ok = MacNative.SendError(service, MacNative.Sel(enabled ? "registerAndReturnError:" : "unregisterAndReturnError:"), out var error);
        var state = GetMacState();
        var reached = enabled ? state is AutoStartState.Enabled or AutoStartState.RequiresApproval : state == AutoStartState.Disabled;
        return new(ok || reached, state, ok || reached ? null : MacNative.Text(MacNative.Send(error, MacNative.Sel("localizedDescription"))));
    }
    [SupportedOSPlatform("windows")]
    private static AutoStartState WindowsGet()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("PaperNote") is string ? AutoStartState.Enabled : AutoStartState.Disabled;
    }
    [SupportedOSPlatform("windows")]
    private static AutoStartResult WindowsSet(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("PaperNote", $"\"{Environment.ProcessPath}\" --login");
        else key.DeleteValue("PaperNote", false);
        return new(true, enabled ? AutoStartState.Enabled : AutoStartState.Disabled);
    }
}
