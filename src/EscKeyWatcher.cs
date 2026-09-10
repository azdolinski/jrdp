// jrdp — EscKeyWatcher.cs
//
// Two independent Esc sniffers, belt and braces:
//
//   EscKeyFilter        — thread-level IMessageFilter. PreFilterMessage runs
//     for every message before it dispatches to a control.
//   LowLevelKeyboardHook — WH_KEYBOARD_LL. Sees keys before ANY process-level
//     filter, message pump, or higher-level hook. Needed because the RDP OCX
//     installs its own WH_KEYBOARD_LL hook and grabs keys before they reach
//     our thread's message pump, so the filter alone misses Esc while the OCX
//     has keyboard capture (e.g. fullscreen).
//
// Neither consumes the keystroke — Esc still reaches the OCX for its own use.
// We only observe, so a host can implement gestures like "3x Esc to release
// keyboard capture".

using System;
using System.Windows.Forms;

namespace Jrdp
{
    internal class EscKeyFilter : IMessageFilter
    {
        private const int WM_KEYDOWN    = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_ESCAPE     = 0x1B;

        private readonly Action onEsc;

        public EscKeyFilter(Action onEsc) { this.onEsc = onEsc; }

        public bool PreFilterMessage(ref Message m)
        {
            if ((m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
                && (int)m.WParam == VK_ESCAPE)
            {
                onEsc();
            }
            return false;  // never consume — let the OCX handle Esc normally
        }
    }

    internal static class LowLevelKeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int VK_ESCAPE      = 0x1B;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        // Kept in a static field so the GC can't collect the delegate while
        // Windows still holds the unmanaged function pointer.
        private static LowLevelKeyboardProc proc = HookCallback;
        private static IntPtr hookId = IntPtr.Zero;
        private static Action onEsc;
        private static Action<string> onWarning;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void Install(Action onEscPressed, Action<string> warn)
        {
            if (hookId != IntPtr.Zero) return;
            onEsc = onEscPressed;
            onWarning = warn;
            var hMod = GetModuleHandleW(System.Diagnostics.Process.GetCurrentProcess().MainModule.ModuleName);
            hookId = SetWindowsHookExW(WH_KEYBOARD_LL, proc, hMod, 0);
            if (hookId == IntPtr.Zero && onWarning != null)
            {
                onWarning("WH_KEYBOARD_LL install failed (LastError="
                          + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
            }
        }

        public static void Uninstall()
        {
            if (hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookId);
                hookId = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        var data = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                        if (data.vkCode == VK_ESCAPE && onEsc != null)
                        {
                            onEsc();
                        }
                    }
                }
            }
            catch { /* never let the hook callback throw */ }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }
}
