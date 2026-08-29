using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AetherShell.Client
{
    /// <summary>
    /// Прячет системный taskbar / Start. Windows любит снова показать TrayWnd,
    /// когда фокус уходит с шелла — поэтому есть KeepHidden-таймер.
    /// </summary>
    public static class TaskbarBlocker
    {
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private static DispatcherTimer _keepHiddenTimer;
        private static bool _keepHidden;
        private static int? _savedHideIcons;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static void Hide()
        {
            HideShellChrome();
            HideDesktopIcons();
        }

        public static void Show()
        {
            StopKeepHidden();
            ShowShellChrome();
            RestoreDesktopIcons();
        }

        /// <summary>На время сессии периодически снова прячет taskbar/иконки рабочего стола.</summary>
        public static void StartKeepHidden()
        {
            _keepHidden = true;
            Hide();

            if (_keepHiddenTimer == null)
            {
                _keepHiddenTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(750)
                };
                _keepHiddenTimer.Tick += (_, __) =>
                {
                    if (!_keepHidden) return;
                    HideShellChrome();
                    // Explorer иногда снова показывает DefView при смене фокуса
                    HideDesktopDefView();
                };
            }

            if (!_keepHiddenTimer.IsEnabled)
                _keepHiddenTimer.Start();
        }

        public static void StopKeepHidden()
        {
            _keepHidden = false;
            if (_keepHiddenTimer != null && _keepHiddenTimer.IsEnabled)
                _keepHiddenTimer.Stop();
        }

        private static void HideShellChrome()
        {
            HideByClass("Shell_TrayWnd");
            HideByClass("Shell_SecondaryTrayWnd");

            // Все вторичные панели задач (мультимонитор)
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string name = cls.ToString();
                if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                    ShowWindow(hwnd, SW_HIDE);
                return true;
            }, IntPtr.Zero);

            // Старая кнопка «Пуск»
            IntPtr start = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Button", "Start");
            if (start != IntPtr.Zero)
                ShowWindow(start, SW_HIDE);
        }

        private static void ShowShellChrome()
        {
            ShowByClass("Shell_TrayWnd");
            ShowByClass("Shell_SecondaryTrayWnd");

            EnumWindows((hwnd, _) =>
            {
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string name = cls.ToString();
                if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
                    ShowWindow(hwnd, SW_SHOW);
                return true;
            }, IntPtr.Zero);

            IntPtr start = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Button", "Start");
            if (start != IntPtr.Zero)
                ShowWindow(start, SW_SHOW);
        }

        private static void HideByClass(string className)
        {
            IntPtr hwnd = FindWindow(className, null);
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_HIDE);
        }

        private static void ShowByClass(string className)
        {
            IntPtr hwnd = FindWindow(className, null);
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_SHOW);
        }

        /// <summary>Скрывает иконки рабочего стола — иначе они «просвечивают», когда шелл SendToBack.</summary>
        private static void HideDesktopIcons()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    if (key == null) return;
                    if (_savedHideIcons == null)
                    {
                        object current = key.GetValue("HideIcons");
                        _savedHideIcons = current is int i ? i : 0;
                    }
                    key.SetValue("HideIcons", 1, RegistryValueKind.DWord);
                }
                RefreshDesktop(hide: true);
            }
            catch { }
        }

        private static void RestoreDesktopIcons()
        {
            try
            {
                if (_savedHideIcons == null) return;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", true))
                {
                    if (key != null)
                        key.SetValue("HideIcons", _savedHideIcons.Value, RegistryValueKind.DWord);
                }
                _savedHideIcons = null;
                RefreshDesktop(hide: false);
            }
            catch { }
        }

        private static void HideDesktopDefView()
        {
            try
            {
                RefreshDesktop(hide: true);
            }
            catch { }
        }

        private static void RefreshDesktop(bool hide)
        {
            try
            {
                IntPtr defView = IntPtr.Zero;
                IntPtr progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                    defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

                if (defView == IntPtr.Zero)
                {
                    EnumWindows((hwnd, _) =>
                    {
                        var cls = new System.Text.StringBuilder(64);
                        GetClassName(hwnd, cls, cls.Capacity);
                        if (cls.ToString() == "WorkerW")
                        {
                            IntPtr view = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                            if (view != IntPtr.Zero)
                            {
                                defView = view;
                                return false;
                            }
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                if (defView != IntPtr.Zero)
                    ShowWindow(defView, hide ? SW_HIDE : SW_SHOW);
            }
            catch { }
        }
    }
}
