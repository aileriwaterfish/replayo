using System.Runtime.InteropServices;

namespace Replayo.Input;

/// Raccourci clavier global via RegisterHotKey sur un thread à boucle de messages dédié.
public sealed class HotkeyManager : IDisposable
{
    public const uint MOD_ALT = 0x0001;
    public const uint VK_F10 = 0x79;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    private const uint WM_HOTKEY = 0x0312, WM_QUIT = 0x0012;
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _ok;

    public bool Enregistrer(uint modificateurs, uint toucheVk, Action rappel)
    {
        using var pret = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _ok = RegisterHotKey(IntPtr.Zero, 1, modificateurs, toucheVk);
            pret.Set();
            if (!_ok) return;
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == WM_HOTKEY) rappel();
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "Replayo-Hotkey" };
        _thread.Start();
        pret.Wait();
        return _ok;
    }

    public void Desenregistrer()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Desenregistrer();
}
