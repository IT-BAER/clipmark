using System.Runtime.InteropServices;
using Clipmark.Core.Capture;
using Windows.Graphics.Capture;
using static Clipmark.App.Interop.Native;

namespace Clipmark.App.Recording;

/// <summary>GraphicsCaptureItem for a monitor via IGraphicsCaptureItemInterop (no picker; desktop apps only).</summary>
internal static class CaptureItems
{
    private static readonly Guid InteropIid = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static unsafe GraphicsCaptureItem ForMonitor(PixelRect monitor)
    {
        nint hmonitor = MonitorFromPoint(new POINT { X = monitor.X + monitor.Width / 2, Y = monitor.Y + monitor.Height / 2 }, MONITOR_DEFAULTTONEAREST);
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, (uint)name.Length, out nint hstring));
        nint interop;
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, InteropIid, out interop));
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
        try
        {
            // vtable after IUnknown: 3 CreateForWindow(HWND, REFIID, void**), 4 CreateForMonitor(HMONITOR, REFIID, void**)
            var vtbl = *(void***)interop;
            nint item;
            Guid iid = ItemIid;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtbl[4])(interop, hmonitor, &iid, &item));
            try
            {
                return GraphicsCaptureItem.FromAbi(item);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }
}
