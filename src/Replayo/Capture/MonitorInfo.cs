using System.Runtime.InteropServices;

namespace Replayo.Capture;

/// Un écran physique détecté (EnumDisplayMonitors).
public sealed record MonitorInfo(IntPtr Handle, string Nom, int Largeur, int Hauteur, bool Principal, int Index)
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);

    public static List<MonitorInfo> EnumererEcrans()
    {
        var liste = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT r, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfoW(h, ref mi);
            liste.Add(new(h, mi.szDevice, mi.rcMonitor.R - mi.rcMonitor.L, mi.rcMonitor.B - mi.rcMonitor.T,
                          (mi.dwFlags & 1) != 0, liste.Count));
            return true;
        }, IntPtr.Zero);
        return liste;
    }
}
