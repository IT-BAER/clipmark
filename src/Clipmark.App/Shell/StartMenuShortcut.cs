using System.Runtime.InteropServices;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Shell;

/// <summary>
/// Start menu shortcut "Clipmark" carrying the app's AUMID. Windows shows toast banners of an unpackaged app only when a
/// Start menu shortcut has its AUMID (measured: without it toasts go straight to the notification center).
/// Raw COM vtables: Native AOT has no built-in COM interop.
/// </summary>
internal static unsafe class StartMenuShortcut
{
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkIid = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PropertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid PersistFileIid = new("0000010B-0000-0000-C000-000000000046");
    private static readonly Guid AppUserModelFmtid = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), App.DisplayName + ".lnk");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid Fmtid; public uint Pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Vt; [FieldOffset(8)] public nint Pointer; }

    private static T Slot<T>(nint obj, int index) where T : unmanaged => ((T*)*(void**)obj)[index];

    /// <summary>Writes the shortcut to the running exe with <paramref name="appId"/> (replaces an older one).</summary>
    public static void Write(string appId)
    {
        nint link = 0, store = 0, file = 0;
        nint value = Marshal.StringToCoTaskMemUni(appId);
        try
        {
            Guid clsid = ShellLinkClsid, iid = ShellLinkIid;
            Marshal.ThrowExceptionForHR(CoCreateInstance(&clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, &iid, &link));
            fixed (char* target = Environment.ProcessPath, dir = AppContext.BaseDirectory)
            {
                // IShellLinkW 20 SetPath, 9 SetWorkingDirectory
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot<nint>(link, 20))(link, target));
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot<nint>(link, 9))(link, dir));
            }
            Guid storeIid = PropertyStoreIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(link, in storeIid, out store));
            var key = new PropertyKey { Fmtid = AppUserModelFmtid, Pid = 5 }; // PKEY_AppUserModel_ID
            var pv = new PropVariant { Vt = 31 /* VT_LPWSTR */, Pointer = value };
            // IPropertyStore 6 SetValue(REFPROPERTYKEY, REFPROPVARIANT), 7 Commit
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, PropertyKey*, PropVariant*, int>)Slot<nint>(store, 6))(store, &key, &pv));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>)Slot<nint>(store, 7))(store));
            Guid fileIid = PersistFileIid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(link, in fileIid, out file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            fixed (char* path = Path)
                // IPersistFile 6 Save(LPCOLESTR, BOOL remember)
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot<nint>(file, 6))(file, path, 1));
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
            if (file != 0) Marshal.Release(file);
            if (store != 0) Marshal.Release(store);
            if (link != 0) Marshal.Release(link);
        }
    }

    public static void Remove()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException) { }
    }
}
