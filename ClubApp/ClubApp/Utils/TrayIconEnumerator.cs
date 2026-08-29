using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AetherShell.Client.Utils
{
    public class TrayIconItem : INotifyPropertyChanged
    {
        public IntPtr OwnerHwnd { get; set; }
        public uint Id { get; set; }
        public uint CallbackMessage { get; set; }
        public string Tip { get; set; }
        public string Key => OwnerHwnd.ToInt64() + ":" + Id;

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set
            {
                if (_icon == value) return;
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyClick(int mouseMsg)
        {
            if (OwnerHwnd == IntPtr.Zero || CallbackMessage == 0) return;
            // Для Win10/11 часто нужен и DOWN, и UP
            if (mouseMsg == 0x0202) // LBUTTONUP
                PostMessage(OwnerHwnd, CallbackMessage, new IntPtr(unchecked((int)Id)), new IntPtr(0x0201));
            if (mouseMsg == 0x0205) // RBUTTONUP
                PostMessage(OwnerHwnd, CallbackMessage, new IntPtr(unchecked((int)Id)), new IntPtr(0x0204));
            PostMessage(OwnerHwnd, CallbackMessage, new IntPtr(unchecked((int)Id)), new IntPtr(mouseMsg));
        }

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }

    public static class TrayIconEnumerator
    {
        private const uint TB_BUTTONCOUNT = 0x0418;
        private const uint TB_GETBUTTON = 0x0417;
        private const uint TB_GETBUTTONTEXTW = 0x044B;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int BM_CLICK = 0x00F5;

        public static List<TrayIconItem> GetTrayIcons()
        {
            var results = new List<TrayIconItem>();
            var seen = new HashSet<string>();

            foreach (var toolbar in FindTrayToolbars())
            {
                try { CollectFromToolbar(toolbar, results, seen); }
                catch { }
            }
            return results;
        }

        public static void InvokeLeftClick(TrayIconItem item) => item?.NotifyClick(WM_LBUTTONUP);
        public static void InvokeRightClick(TrayIconItem item) => item?.NotifyClick(WM_RBUTTONUP);

        public static bool IsOverflowVisible()
        {
            try
            {
                foreach (var hwnd in FindOverflowWindows())
                {
                    if (IsWindowVisible(hwnd)) return true;
                }
            }
            catch { }
            return false;
        }

        public static void ToggleOverflow()
        {
            try
            {
                if (IsOverflowVisible())
                {
                    foreach (var hwnd in FindOverflowWindows())
                        ShowWindow(hwnd, 0);
                    return;
                }

                // 1) Классическое окно overflow
                foreach (var hwnd in FindOverflowWindows())
                {
                    ShowWindow(hwnd, 5);
                    SetForegroundWindow(hwnd);
                    return;
                }

                // 2) Клик по кнопке «^» в TrayNotifyWnd
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero)
                {
                    IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (notify != IntPtr.Zero)
                    {
                        IntPtr btn = FindWindowEx(notify, IntPtr.Zero, "Button", null);
                        if (btn != IntPtr.Zero)
                        {
                            // Временно показать tray, кликнуть chevron, снова спрятать
                            bool wasVisible = IsWindowVisible(tray);
                            if (!wasVisible) ShowWindow(tray, 5);
                            SendMessage(btn, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                            if (!wasVisible)
                            {
                                // Небольшой отступ: overflow уже показан, сам Shell_TrayWnd прячем снова
                                var t = new System.Windows.Threading.DispatcherTimer
                                {
                                    Interval = TimeSpan.FromMilliseconds(200)
                                };
                                t.Tick += (_, __) =>
                                {
                                    t.Stop();
                                    ShowWindow(tray, 0);
                                };
                                t.Start();
                            }
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private static IEnumerable<IntPtr> FindOverflowWindows()
        {
            var list = new List<IntPtr>();
            IntPtr a = FindWindow("NotifyIconOverflowWindow", null);
            if (a != IntPtr.Zero) list.Add(a);
            IntPtr b = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
            if (b != IntPtr.Zero) list.Add(b);
            return list;
        }

        private static IEnumerable<IntPtr> FindTrayToolbars()
        {
            var list = new List<IntPtr>();
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
                CollectToolbarsRecursive(tray, list);

            foreach (var overflow in FindOverflowWindows())
                CollectToolbarsRecursive(overflow, list);

            return list;
        }

        private static void CollectToolbarsRecursive(IntPtr parent, List<IntPtr> list)
        {
            EnumChildWindows(parent, (hwnd, _) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() == "ToolbarWindow32" && !list.Contains(hwnd))
                    list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
        }

        private static void CollectFromToolbar(IntPtr toolbar, List<TrayIconItem> results, HashSet<string> seen)
        {
            GetWindowThreadProcessId(toolbar, out uint pid);
            if (pid == 0) return;

            uint access = PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE
                          | PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION;
            IntPtr hProcess = OpenProcess(access, false, pid);
            if (hProcess == IntPtr.Zero) return;

            try
            {
                int count = (int)SendMessage(toolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (count <= 0 || count > 64) return;

                int tbbuttonSize = IntPtr.Size == 8 ? 32 : 20;
                IntPtr remote = VirtualAllocEx(hProcess, IntPtr.Zero, (IntPtr)4096, MEM_COMMIT, PAGE_READWRITE);
                if (remote == IntPtr.Zero) return;

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            if (SendMessage(toolbar, TB_GETBUTTON, new IntPtr(i), remote) == IntPtr.Zero)
                                continue;

                            byte[] btnBuf = new byte[tbbuttonSize];
                            if (!ReadProcessMemory(hProcess, remote, btnBuf, btnBuf.Length, out _))
                                continue;

                            IntPtr dwData = IntPtr.Size == 8
                                ? new IntPtr(BitConverter.ToInt64(btnBuf, 16))
                                : new IntPtr(BitConverter.ToInt32(btnBuf, 12));
                            if (dwData == IntPtr.Zero) continue;

                            int traySize = IntPtr.Size == 8 ? 32 : 24;
                            byte[] trayBuf = new byte[Math.Max(traySize, 40)];
                            if (!ReadProcessMemory(hProcess, dwData, trayBuf, trayBuf.Length, out _))
                                continue;

                            IntPtr owner;
                            uint uId, callback;
                            IntPtr hIcon;
                            if (IntPtr.Size == 8)
                            {
                                owner = new IntPtr(BitConverter.ToInt64(trayBuf, 0));
                                uId = BitConverter.ToUInt32(trayBuf, 8);
                                callback = BitConverter.ToUInt32(trayBuf, 12);
                                hIcon = new IntPtr(BitConverter.ToInt64(trayBuf, 24));
                            }
                            else
                            {
                                owner = new IntPtr(BitConverter.ToInt32(trayBuf, 0));
                                uId = BitConverter.ToUInt32(trayBuf, 4);
                                callback = BitConverter.ToUInt32(trayBuf, 8);
                                hIcon = new IntPtr(BitConverter.ToInt32(trayBuf, 20));
                            }

                            if (owner == IntPtr.Zero || !IsWindow(owner)) continue;
                            string key = owner.ToInt64() + ":" + uId;
                            if (!seen.Add(key)) continue;

                            string tip = GetButtonTip(toolbar, hProcess, remote, i);
                            results.Add(new TrayIconItem
                            {
                                OwnerHwnd = owner,
                                Id = uId,
                                CallbackMessage = callback,
                                Tip = string.IsNullOrWhiteSpace(tip) ? "Трей" : tip,
                                Icon = HIconToImageSource(hIcon)
                            });
                        }
                        catch { }
                    }
                }
                finally
                {
                    VirtualFreeEx(hProcess, remote, IntPtr.Zero, MEM_RELEASE);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private static string GetButtonTip(IntPtr toolbar, IntPtr hProcess, IntPtr remote, int index)
        {
            try
            {
                int chars = (int)SendMessage(toolbar, TB_GETBUTTONTEXTW, new IntPtr(index), remote);
                if (chars <= 0) return null;
                byte[] buf = new byte[Math.Min(chars + 1, 256) * 2];
                if (!ReadProcessMemory(hProcess, remote, buf, buf.Length, out _)) return null;
                return Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
            }
            catch { return null; }
        }

        private static ImageSource HIconToImageSource(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
                source.Freeze();
                return source;
            }
            catch { return null; }
        }

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);
    }
}
