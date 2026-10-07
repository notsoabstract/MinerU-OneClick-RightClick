using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

[ComImport, Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray items, [MarshalAs(UnmanagedType.LPWStr)] out string title);
    [PreserveSig] int GetIcon(IShellItemArray items, [MarshalAs(UnmanagedType.LPWStr)] out string icon);
    [PreserveSig] int GetToolTip(IShellItemArray items, [MarshalAs(UnmanagedType.LPWStr)] out string tooltip);
    [PreserveSig] int GetCanonicalName(out Guid name);
    [PreserveSig] int GetState(IShellItemArray items, [MarshalAs(UnmanagedType.Bool)] bool allowSlow, out uint state);
    [PreserveSig] int Invoke(IShellItemArray items, IntPtr bindContext);
    [PreserveSig] int GetFlags(out uint flags);
    [PreserveSig] int EnumSubCommands(out IntPtr commands);
}

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray
{
    [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetPropertyStore(int flags, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr result);
    [PreserveSig] int GetAttributes(int flags, uint mask, out uint attributes);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetItemAt(uint index, out IntPtr item);
    [PreserveSig] int EnumItems(out IntPtr items);
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr result);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool shouldLock);
}

internal static class ReviewNative
{
    private static readonly Guid ClassId = new Guid("A118B38A-7D91-4DB1-A0A5-87C624F11B79");
    private static readonly Guid ItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid ArrayId = new Guid("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private static readonly Guid FactoryId = new Guid("00000001-0000-0000-C000-000000000046");
    private static readonly Guid CommandId = new Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9");
    private static readonly List<string> Results = new List<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        bool registered = args.Length >= 1 && args[0] == "--registered";
        string libraryPath = Path.GetFullPath(args[registered ? 1 : 0]);
        string reportDirectory = Path.GetFullPath(args[registered ? 2 : 1]);
        Directory.CreateDirectory(reportDirectory);
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "MinerUReviewNative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        IntPtr library = IntPtr.Zero;
        IClassFactory factory = null;
        IExplorerCommand command = null;
        CanUnloadDelegate canUnload = null;
        try
        {
            if (registered)
            {
                command = (IExplorerCommand)Activator.CreateInstance(Type.GetTypeFromCLSID(ClassId, true));
                Assert(command != null, "Actual registered COM activation");
            }
            else
            {
                library = LoadLibraryW(libraryPath);
                if (library == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Native DLL load failed");
                Assert(true, "Native DLL loaded");
                IntPtr getClassAddress = GetProcAddress(library, "DllGetClassObject");
                IntPtr canUnloadAddress = GetProcAddress(library, "DllCanUnloadNow");
                Assert(getClassAddress != IntPtr.Zero && canUnloadAddress != IntPtr.Zero, "Native COM exports found");
                GetClassDelegate getClass = (GetClassDelegate)Marshal.GetDelegateForFunctionPointer(getClassAddress, typeof(GetClassDelegate));
                canUnload = (CanUnloadDelegate)Marshal.GetDelegateForFunctionPointer(canUnloadAddress, typeof(CanUnloadDelegate));
                Assert(canUnload() == 0, "Native DLL initially unloadable");
                Guid clsid = ClassId, iid = FactoryId;
                IntPtr rawFactory;
                Marshal.ThrowExceptionForHR(getClass(ref clsid, ref iid, out rawFactory));
                try { factory = (IClassFactory)Marshal.GetObjectForIUnknown(rawFactory); }
                finally { Marshal.Release(rawFactory); }
                Assert(factory != null && canUnload() == 1, "Native class factory pins DLL");
                iid = CommandId;
                IntPtr rawCommand;
                Marshal.ThrowExceptionForHR(factory.CreateInstance(IntPtr.Zero, ref iid, out rawCommand));
                try { command = (IExplorerCommand)Marshal.GetObjectForIUnknown(rawCommand); }
                finally { Marshal.Release(rawCommand); }
                Assert(command != null, "Native factory created IExplorerCommand");
            }

            string title;
            Marshal.ThrowExceptionForHR(command.GetTitle(null, out title));
            Assert(title == "用 MinerU 识别", "Chinese title matches exactly");
            if (!registered)
            {
                string icon;
                Marshal.ThrowExceptionForHR(command.GetIcon(null, out icon));
                string expectedIcon = Path.Combine(Path.GetDirectoryName(libraryPath), "MinerURightClick.exe") + ",0";
                Assert(String.Equals(icon, expectedIcon, StringComparison.OrdinalIgnoreCase), "Icon refers to adjacent application exactly");
            }
            uint state;
            Marshal.ThrowExceptionForHR(command.GetState(null, false, out state));
            Assert(state == 2, "Null selection is hidden");
            foreach (string extension in new[] { ".pdf", ".docx", ".xlsx", ".PDF" })
            {
                string file = Path.Combine(temporaryDirectory, "识别验证 A&B (1)" + extension);
                if (!File.Exists(file)) File.WriteAllBytes(file, new byte[0]);
                CheckFileState(command, file, 0, "Supported " + extension + " is enabled");
            }
            string textFile = Path.Combine(temporaryDirectory, "hidden.txt");
            File.WriteAllBytes(textFile, new byte[0]);
            CheckFileState(command, textFile, 2, "Unsupported .txt is hidden");
            string folder = Path.Combine(temporaryDirectory, "folder.pdf");
            Directory.CreateDirectory(folder);
            CheckFileState(command, folder, 2, "Folder named .pdf is hidden");
        }
        catch (Exception error)
        {
            Results.Add("FAIL " + error.GetType().Name + ": " + error.Message + " HRESULT=0x" + error.HResult.ToString("X8"));
        }
        finally
        {
            if (command != null) Marshal.FinalReleaseComObject(command);
            if (factory != null) Marshal.FinalReleaseComObject(factory);
            if (canUnload != null) Assert(canUnload() == 0, "Released COM objects allow DLL unload");
            if (library != IntPtr.Zero) FreeLibrary(library);
            Directory.Delete(temporaryDirectory, true);
            File.WriteAllLines(Path.Combine(reportDirectory, registered ? "ReviewNative-registered-results.txt" : "ReviewNative-results.txt"), Results);
            Console.WriteLine(String.Join(Environment.NewLine, Results));
        }
        return Results.Exists(s => s.StartsWith("FAIL ", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static void CheckFileState(IExplorerCommand command, string path, uint expectedState, string description)
    {
        IntPtr item = IntPtr.Zero, array = IntPtr.Zero;
        IShellItemArray selected = null;
        try
        {
            Guid iid = ItemId;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
            iid = ArrayId;
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, ref iid, out array));
            selected = (IShellItemArray)Marshal.GetObjectForIUnknown(array);
            uint count;
            Marshal.ThrowExceptionForHR(selected.GetCount(out count));
            Assert(count == 1, "Windows Shell selection contains one item");
            uint state;
            Marshal.ThrowExceptionForHR(command.GetState(selected, false, out state));
            Assert(state == expectedState, description);
        }
        finally
        {
            if (selected != null) Marshal.FinalReleaseComObject(selected);
            if (array != IntPtr.Zero) Marshal.Release(array);
            if (item != IntPtr.Zero) Marshal.Release(item);
        }
    }

    private static void Assert(bool condition, string description)
    {
        Results.Add((condition ? "PASS " : "FAIL ") + description);
        if (!condition) throw new InvalidOperationException(description);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetClassDelegate(ref Guid clsid, ref Guid iid, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CanUnloadDelegate();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string fileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string function);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IntPtr item);
    [DllImport("shell32.dll", PreserveSig = true)] private static extern int SHCreateShellItemArrayFromShellItem(IntPtr item, ref Guid iid, out IntPtr items);
}
