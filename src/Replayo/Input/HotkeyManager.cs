using System.Runtime.InteropServices;

namespace Replayo.Input;

/// Raccourci clavier global via hook bas niveau (WH_KEYBOARD_LL) sur un thread
/// à boucle de messages dédié. RegisterHotKey ne déclenche pas quand un jeu en
/// raw input (League of Legends…) a le focus — vérifié par diagnostic : le hook
/// voit la frappe, WM_HOTKEY n'est jamais délivré. Approche OBS/ShadowPlay.
public sealed class HotkeyManager : IDisposable
{
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004;
    public const uint VK_F10 = 0x79;

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookExW(int type, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_QUIT = 0x0012;
    private const uint LLKHF_ALTDOWN = 0x20;
    private const int VK_CONTROL = 0x11, VK_SHIFT = 0x10;

    private Thread? _thread;
    private uint _threadId;
    private volatile bool _ok;
    private HookProc? _proc; // référence gardée : sans elle le GC ramasse le délégué du hook

    /// Vrai si la touche pressée et les modificateurs enfoncés correspondent
    /// exactement au raccourci configuré. Logique pure et testable.
    public static bool Correspond(uint vkPresse, uint modsEnfonces, uint vkConfig, uint modsConfig)
        => vkPresse == vkConfig && modsEnfonces == modsConfig;

    public bool Enregistrer(uint modificateurs, uint toucheVk, Action rappel)
    {
        using var pret = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _proc = (code, w, l) =>
            {
                if (code >= 0 && (uint)w is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    uint vk = (uint)Marshal.ReadInt32(l);       // KBDLLHOOKSTRUCT.vkCode
                    uint flags = (uint)Marshal.ReadInt32(l, 8); // KBDLLHOOKSTRUCT.flags
                    uint mods = 0;
                    if ((flags & LLKHF_ALTDOWN) != 0) mods |= MOD_ALT;
                    if (GetAsyncKeyState(VK_CONTROL) < 0) mods |= MOD_CONTROL;
                    if (GetAsyncKeyState(VK_SHIFT) < 0) mods |= MOD_SHIFT;
                    if (Correspond(vk, mods, toucheVk, modificateurs)) rappel();
                }
                return CallNextHookEx(IntPtr.Zero, code, w, l);
            };
            var hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            _ok = hook != IntPtr.Zero;
            pret.Set();
            if (!_ok) return;
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { } // WM_QUIT → sortie
            UnhookWindowsHookEx(hook);
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
