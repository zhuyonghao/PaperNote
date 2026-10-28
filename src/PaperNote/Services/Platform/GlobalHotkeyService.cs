using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PaperNote.Models;
namespace PaperNote.Services.Platform;

public sealed record HotkeyResult(bool Success, string? Error = null);
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly IBackend _backend;
    public GlobalHotkeyService(Action pressed)
    {
        _backend = OperatingSystem.IsMacOS() ? new MacBackend(pressed) : OperatingSystem.IsWindows() ? new WindowsBackend(pressed) : new UnsupportedBackend();
    }
    public async Task<HotkeyResult> ApplyAsync(ShortcutSpec? shortcut)
    {
        if (shortcut is not null && shortcut.Validate() is { } error) return new HotkeyResult(false, error);
        try { return await _backend.ApplyAsync(shortcut); }
        catch (Exception ex) { return new HotkeyResult(false, "快捷键服务无法响应：" + ex.Message); }
    }
    public void Dispose() => _backend.Dispose();
    private interface IBackend : IDisposable { Task<HotkeyResult> ApplyAsync(ShortcutSpec? shortcut); }
    private sealed class UnsupportedBackend : IBackend
    {
        public Task<HotkeyResult> ApplyAsync(ShortcutSpec? s) => Task.FromResult(new HotkeyResult(s is null, "当前系统暂不支持全局快捷键。"));
        public void Dispose() { }
    }
    [SupportedOSPlatform("macos")]
    private sealed class MacBackend : IBackend
    {
        private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
        private readonly Action _pressed;
        private readonly Handler _handler;
        private IntPtr _eventHandler, _hotkey;
        private uint _id;
        private static int _nextMacId;
        private ShortcutSpec? _current;
        private readonly int _installStatus;
        public MacBackend(Action pressed)
        {
            _pressed = pressed; _handler = OnEvent;
            var type = new EventType { EventClass = 0x6B657962, EventKind = 6 };
            _installStatus = InstallEventHandler(GetApplicationEventTarget(), _handler, 1, ref type, IntPtr.Zero, out _eventHandler);
        }
        private int OnEvent(IntPtr next, IntPtr evt, IntPtr user)
        {
            // Carbon sends hotkey events to every installed handler. Dispatch only our ID.
            if (GetEventParameter(evt, 0x2D2D2D2D, 0x686B6964, IntPtr.Zero, (uint)Marshal.SizeOf<HotKeyId>(), IntPtr.Zero, out var id) != 0
                || id.Signature != 0x50415052 || id.Id != _id || _current is null) return -9874;
            try { _pressed(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
            return 0;
        }
        public Task<HotkeyResult> ApplyAsync(ShortcutSpec? shortcut)
        {
            if (shortcut == _current) return Task.FromResult(new HotkeyResult(true));
            if (shortcut is null)
            {
                if (_hotkey != IntPtr.Zero) UnregisterEventHotKey(_hotkey);
                _hotkey = IntPtr.Zero; _current = null; return Task.FromResult(new HotkeyResult(true));
            }
            if (_installStatus != 0) return Task.FromResult(new HotkeyResult(false, $"macOS 快捷键服务不可用（{_installStatus}）。"));
            var code = MacKeyCode(shortcut.Key);
            uint mods = 0;
            if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Meta)) mods |= 1u << 8;
            if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift)) mods |= 1u << 9;
            if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt)) mods |= 1u << 11;
            if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control)) mods |= 1u << 12;
            var nextId = (uint)Interlocked.Increment(ref _nextMacId);
            var status = RegisterEventHotKey(code, mods, new HotKeyId { Signature = 0x50415052, Id = nextId }, GetApplicationEventTarget(), 0, out var replacement);
            if (status != 0) return Task.FromResult(new HotkeyResult(false, $"这个快捷键已被系统或其他应用占用，请换一个组合。（{status}）"));
            if (_hotkey != IntPtr.Zero) UnregisterEventHotKey(_hotkey);
            _hotkey = replacement; _id = nextId; _current = shortcut; return Task.FromResult(new HotkeyResult(true));
        }
        private static uint MacKeyCode(string key) => key.ToUpperInvariant() switch
        {
            "A" => 0, "S" => 1, "D" => 2, "F" => 3, "H" => 4, "G" => 5, "Z" => 6, "X" => 7, "C" => 8, "V" => 9,
            "B" => 11, "Q" => 12, "W" => 13, "E" => 14, "R" => 15, "Y" => 16, "T" => 17, "1" => 18, "2" => 19,
            "3" => 20, "4" => 21, "6" => 22, "5" => 23, "9" => 25, "7" => 26, "8" => 28, "0" => 29,
            "O" => 31, "U" => 32, "I" => 34, "P" => 35, "L" => 37, "J" => 38, "K" => 40, "N" => 45, "M" => 46,
            "SPACE" => 49, _ => throw new ArgumentException("不支持的按键。")
        };
        public void Dispose()
        {
            if (_hotkey != IntPtr.Zero) UnregisterEventHotKey(_hotkey);
            if (_eventHandler != IntPtr.Zero) RemoveEventHandler(_eventHandler);
            _hotkey = _eventHandler = IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct EventType { public uint EventClass, EventKind; }
        [StructLayout(LayoutKind.Sequential)] private struct HotKeyId { public uint Signature, Id; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Handler(IntPtr next, IntPtr evt, IntPtr user);
        [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
        [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, Handler callback, uint count, ref EventType types, IntPtr user, out IntPtr handler);
        [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handler);
        [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint code, uint modifiers, HotKeyId id, IntPtr target, uint options, out IntPtr key);
        [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr key);
        [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr evt, uint name, uint type, IntPtr actualType, uint size, IntPtr actualSize, out HotKeyId value);
    }
    [SupportedOSPlatform("windows")]
    private sealed class WindowsBackend : IBackend
    {
        private const uint HotkeyMessage = 0x0312, WorkMessage = 0x8001, QuitMessage = 0x0012;
        private readonly Thread _thread;
        private readonly ConcurrentQueue<Action> _work = new();
        private readonly ManualResetEventSlim _ready = new();
        private readonly Action _pressed;
        private uint _threadId;
        private int _registered, _nextId = 100;
        private ShortcutSpec? _current;
        private volatile bool _disposed;
        public WindowsBackend(Action pressed)
        {
            _pressed = pressed;
            _thread = new Thread(Run) { IsBackground = true, Name = "PaperNote hotkey" };
            _thread.Start(); _ready.Wait(TimeSpan.FromSeconds(3));
        }
        private void Run()
        {
            _threadId = GetCurrentThreadId(); PeekMessage(out _, IntPtr.Zero, 0, 0, 0); _ready.Set();
            try
            {
                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.Message == HotkeyMessage && _registered != 0) { try { _pressed(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); } }
                    else if (msg.Message == WorkMessage) while (_work.TryDequeue(out var action)) action();
                }
            }
            finally { if (_registered != 0) UnregisterHotKey(IntPtr.Zero, _registered); }
        }
        public Task<HotkeyResult> ApplyAsync(ShortcutSpec? shortcut)
        {
            if (_disposed || _threadId == 0) return Task.FromResult(new HotkeyResult(false, "快捷键服务已关闭。"));
            var completion = new TaskCompletionSource<HotkeyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _work.Enqueue(() => {
                if (shortcut == _current) { completion.TrySetResult(new(true)); return; }
                if (shortcut is null)
                {
                    if (_registered != 0) UnregisterHotKey(IntPtr.Zero, _registered);
                    _registered = 0; _current = null; completion.TrySetResult(new(true)); return;
                }
                uint mods = 0x4000; // MOD_NOREPEAT
                if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt)) mods |= 1;
                if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control)) mods |= 2;
                if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift)) mods |= 4;
                if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Meta)) mods |= 8;
                uint key = shortcut.Key == "Space" ? 0x20u : char.ToUpperInvariant(shortcut.Key[0]);
                var id = ++_nextId;
                if (!RegisterHotKey(IntPtr.Zero, id, mods, key)) { completion.TrySetResult(new(false, "这个快捷键已被占用，请换一个组合。")); return; }
                if (_registered != 0) UnregisterHotKey(IntPtr.Zero, _registered);
                _registered = id; _current = shortcut; completion.TrySetResult(new(true));
            });
            if (!PostThreadMessage(_threadId, WorkMessage, UIntPtr.Zero, IntPtr.Zero)) completion.TrySetResult(new(false, "无法连接快捷键服务。"));
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (_threadId != 0) PostThreadMessage(_threadId, QuitMessage, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000); _ready.Dispose();
        }
        [StructLayout(LayoutKind.Sequential)] private struct MessageData { public IntPtr HWnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern int GetMessage(out MessageData message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out MessageData message, IntPtr window, uint min, uint max, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint id, uint message, UIntPtr wParam, IntPtr lParam);
    }
}
