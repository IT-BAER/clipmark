using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Output;

/// <summary>Windows share sheet for a desktop window via IDataTransferManagerInterop (no CoreWindow).</summary>
internal static class ShareSheet
{
    private static readonly Guid InteropIid = new("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
    private static readonly Guid ManagerIid = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");

    private static DataTransferManager? s_manager;
    private static StorageFile? s_file;
    private static bool s_isImage;

    /// <summary>Raised when the sheet asked for the data (test evidence that the sheet opened).</summary>
    public static event Action? DataRequested;

    /// <summary>Writes the PNG to the temp folder and opens the share sheet for it.</summary>
    public static async Task ShowAsync(nint hwnd, byte[] png)
    {
        s_file = await StorageFile.GetFileFromPathAsync(await CaptureOutput.SaveAsync(CaptureOutput.TempFolder, png));
        s_isImage = true;
        Show(hwnd);
    }

    /// <summary>Opens the share sheet for an existing file (recordings).</summary>
    public static async Task ShowFileAsync(nint hwnd, string path)
    {
        s_file = await StorageFile.GetFileFromPathAsync(path);
        s_isImage = false;
        Show(hwnd);
    }

    private static unsafe void Show(nint hwnd)
    {
        nint interop = Factory();
        try
        {
            // vtable after IUnknown: 3 GetForWindow(HWND, REFIID, void**), 4 ShowShareUIForWindow(HWND)
            var vtbl = *(void***)interop;
            if (s_manager is null)
            {
                nint manager;
                Guid iid = ManagerIid;
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtbl[3])(interop, hwnd, &iid, &manager));
                s_manager = DataTransferManager.FromAbi(manager);
                Marshal.Release(manager);
                s_manager.DataRequested += OnDataRequested;
            }
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, int>)vtbl[4])(interop, hwnd));
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private static nint Factory()
    {
        const string name = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, (uint)name.Length, out nint hstring));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, InteropIid, out nint factory));
            return factory;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    private static void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs e)
    {
        if (s_file is null)
            return;
        var data = e.Request.Data;
        data.Properties.Title = s_file.Name;
        data.SetStorageItems([s_file]);
        if (s_isImage)
            data.SetBitmap(RandomAccessStreamReference.CreateFromFile(s_file));
        DataRequested?.Invoke();
    }
}
