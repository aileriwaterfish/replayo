using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Replayo.Clip;

/// Nom de l'application au premier plan au moment du clip (pour le rangement).
public static class ForegroundAppTracker
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    public static string NomApplication()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "Bureau";
            GetWindowThreadProcessId(h, out var pid);
            var nom = Process.GetProcessById((int)pid).ProcessName;
            return string.IsNullOrWhiteSpace(nom) || nom is "explorer" ? "Bureau" : nom;
        }
        catch { return "Bureau"; }
    }
}
