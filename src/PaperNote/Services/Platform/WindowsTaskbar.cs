using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
namespace PaperNote.Services.Platform;

[SupportedOSPlatform("windows")]
internal static class WindowsTaskbar
{
    internal const string AppId = "PaperNote.Desktop";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    internal static void SetIdentity() => SetCurrentProcessExplicitAppUserModelID(AppId);
    internal static string InstallTasks()
    {
        object? destination = null, collection = null, removed = null;
        var links = new List<object>();
        try
        {
            var executable = Environment.ProcessPath ?? throw new IOException("找不到应用程序路径。");
            destination = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6"))!)!;
            var list = (ICustomDestinationList)destination; list.SetAppID(AppId);
            var arrayId = typeof(IObjectArray).GUID; list.BeginList(out _, ref arrayId, out removed);
            collection = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a"))!)!;
            var objects = (IObjectCollection)collection;
            foreach (var (title, arguments) in new[] { ("新建桌面便签", "--new-note"), ("打开便签库", "--library"), ("设置", "--settings") })
            {
                var link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!; links.Add(link);
                var shell = (IShellLinkW)link; shell.SetPath(executable); shell.SetArguments(arguments); shell.SetWorkingDirectory(Path.GetDirectoryName(executable)!); shell.SetDescription(title); shell.SetIconLocation(executable, 0);
                var properties = (IPropertyStore)link;
                var key = new PropertyKey { Format = new Guid("f29f85e0-4ff9-1068-ab91-08002b27b3d9"), Id = 2 };
                var value = new PropertyVariant { Type = 31, String = Marshal.StringToCoTaskMemUni(title) };
                try { properties.SetValue(ref key, ref value); properties.Commit(); }
                finally { Marshal.FreeCoTaskMem(value.String); }
                objects.AddObject(link);
            }
            list.AddUserTasks((IObjectArray)collection); list.CommitList(); return "任务栏右键菜单已就绪";
        }
        catch (Exception ex)
        {
            if (destination is ICustomDestinationList list) { try { list.AbortList(); } catch { } }
            System.Diagnostics.Trace.WriteLine(ex); return "任务栏菜单未能注册；系统托盘菜单仍可使用。";
        }
        finally
        {
            foreach (var link in links) Marshal.ReleaseComObject(link);
            if (removed is not null) Marshal.ReleaseComObject(removed);
            if (collection is not null) Marshal.ReleaseComObject(collection);
            if (destination is not null) Marshal.ReleaseComObject(destination);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropertyVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr String; }
    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
    }
    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object value);
        void AddFromArray(IObjectArray array);
        void RemoveObjectAt(uint index);
        void Clear();
    }
    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        void BeginList(out uint slots, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string name, IObjectArray items);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object items);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string id);
        void AbortList();
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyVariant value);
        void SetValue(ref PropertyKey key, ref PropertyVariant value);
        void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder icon, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
