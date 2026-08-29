using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AetherShell.Client
{
    public static class KeyboardBlocker
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        // Коды клавиш
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const int VK_TAB = 0x09;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_F4 = 0x73;
        private const int VK_SPACE = 0x20;
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_DELETE = 0x2E;
        private const int VK_P = 0x50;
        private const int VK_A = 0x41;

        private static IntPtr _hookID = IntPtr.Zero;

        /// <summary>Вызывается при нажатии Ctrl+Alt+P (закрытие шелла по паролю). Вызвать из UI-потока.</summary>
        public static Action? OnExitShellHotkeyPressed { get; set; }

        /// <summary>Вызывается при нажатии Ctrl+Alt+A (режим администратора). Вызвать из UI-потока.</summary>
        public static Action? OnAdminHotkeyPressed { get; set; }
        private static LowLevelKeyboardProc _proc = HookCallback;

        // Флаг полной блокировки (когда время вышло)
        public static bool IsFullLock { get; set; } = true;

        public static void Block()
        {
            if (_hookID == IntPtr.Zero)
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                        GetModuleHandle(curModule.ModuleName), 0);
                }
            }
        }

        public static void Unblock()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Маршалим структуру данных клавиши
                KBDLLHOOKSTRUCT objKeyInfo = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = (int)objKeyInfo.vkCode;

                // Проверяем нажатие (KEYDOWN или SYSKEYDOWN)
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);

                if (isKeyDown)
                {
                    // Проверяем статус модификаторов (Ctrl, Shift, Alt)
                    // GetAsyncKeyState надёжнее для проверки "зажата ли прямо сейчас" (работает на всех раскладках)
                    bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                    bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 || (objKeyInfo.flags & 0x20) != 0; // Alt: состояние клавиши или флаг в сообщении

                    // --- 1. БЛОКИРОВКА КЛАВИШ WINDOWS (Всегда) ---
                    // Блокируем само нажатие Win и любые комбинации с ним (Win+R, Win+X и т.д.)
                    if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    {
                        return (IntPtr)1; // Блокируем
                    }

                    // --- 2. БЛОКИРОВКА ДИСПЕТЧЕРА ЗАДАЧ И МЕНЮ ПУСК (Ctrl+Esc) ---
                    // Ctrl + Esc (Открывает Пуск)
                    if (vkCode == VK_ESCAPE && ctrlDown) return (IntPtr)1;

                    // Ctrl + Shift + Esc (Диспетчер задач)
                    if (vkCode == VK_ESCAPE && ctrlDown && shiftDown) return (IntPtr)1;

                    // --- 3. Горячая клавиша закрытия шелла (Ctrl+Alt+P) ---
                    if (vkCode == VK_P && ctrlDown && altDown && !shiftDown)
                    {
                        try { OnExitShellHotkeyPressed?.Invoke(); } catch { }
                        return (IntPtr)1;
                    }

                    // --- 3.1 Режим администратора (Ctrl+Alt+A) ---
                    if (vkCode == VK_A && ctrlDown && altDown && !shiftDown)
                    {
                        try { OnAdminHotkeyPressed?.Invoke(); } catch { }
                        return (IntPtr)1;
                    }

                    // --- 4. БЛОКИРОВКА ПРИ ИСТЕЧЕНИИ ВРЕМЕНИ (IsFullLock) ---
                    if (IsFullLock)
                    {
                        // Alt + Tab
                        if (vkCode == VK_TAB && altDown) return (IntPtr)1;

                        // Alt + F4 (Закрытие окна)
                        if (vkCode == VK_F4 && altDown) return (IntPtr)1;

                        // Alt + Space (Системное меню окна)
                        if (vkCode == VK_SPACE && altDown) return (IntPtr)1;

                        // Alt + Esc (Переключение окон без меню)
                        if (vkCode == VK_ESCAPE && altDown) return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // --- Структура для хука (Важно!) ---
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // --- DllImports ---
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Используем GetAsyncKeyState вместо GetKeyState для проверки текущего состояния
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}