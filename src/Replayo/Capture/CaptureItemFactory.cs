using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Replayo.Capture;

/// Crée un GraphicsCaptureItem à partir d'un HMONITOR (interop COM documenté par Microsoft).
public static class CaptureItemFactory
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, ref Guid iid);
        IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
    }

    private static Guid _iidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IID IGraphicsCaptureItem

    public static GraphicsCaptureItem DepuisEcran(IntPtr hmon)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var abi = interop.CreateForMonitor(hmon, ref _iidItem);
        return GraphicsCaptureItem.FromAbi(abi);
    }
}
