using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AetherShell.Client.Utils
{
    public class RunningWindowItem : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }

        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
            }
        }

        public string ProcessName { get; set; }

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

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }

        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title)) return ProcessName ?? "App";
                return Title.Length > 28 ? Title.Substring(0, 27) + "…" : Title;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public static class WindowEnumerator
    {
        private const int GWL_EXSTYLE = -20;
        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int SW_RESTORE = 9;
        private const uint GA_ROOTOWNER = 3;

        private static readonly HashSet<string> ExcludedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AetherShell.Client",
            "ApplicationFrameHost",
            "SystemSettings",
            "TextInputHost",
            "ShellExperienceHost",
            "SearchUI",
            "SearchApp",
            "StartMenuExperienceHost",
            "RuntimeBroker"
        };

        private static readonly HashSet<string> ExcludedClassNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "Progman",
            "WorkerW",
            "Windows.UI.Core.CoreWindow",
            "ForegroundStaging"
        };

        public static List<RunningWindowItem> GetRunningWindows(params IntPtr[] excludeHwnds)
        {
            var exclude = new HashSet<IntPtr>(excludeHwnds ?? Array.Empty<IntPtr>());
            var results = new List<RunningWindowItem>();
            var foreground = GetForegroundWindow();

            EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    if (exclude.Contains(hwnd)) return true;
                    if (!IsWindowVisible(hwnd)) return true;
                    if (GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;

                    // Cloaked UWP / shell surfaces
                    if (IsCloaked(hwnd)) return true;

                    int style = GetWindowLong(hwnd, GWL_STYLE);
                    if ((style & WS_VISIBLE) == 0) return true;

                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    bool isTool = (exStyle & WS_EX_TOOLWINDOW) != 0;
                    bool isApp = (exStyle & WS_EX_APPWINDOW) != 0;
                    if (isTool && !isApp) return true;

                    // Skip windows that are not the root owner of their chain
                    if (GetAncestor(hwnd, GA_ROOTOWNER) != hwnd) return true;

                    var className = new StringBuilder(256);
                    GetClassName(hwnd, className, className.Capacity);
                    if (ExcludedClassNames.Contains(className.ToString())) return true;

                    var title = new StringBuilder(512);
                    GetWindowText(hwnd, title, title.Capacity);
                    string titleStr = title.ToString().Trim();
                    if (titleStr.Length == 0) return true;

                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == 0) return true;

                    string processName;
                    string exePath = null;
                    try
                    {
                        using (var proc = Process.GetProcessById((int)pid))
                        {
                            processName = proc.ProcessName;
                            if (ExcludedProcessNames.Contains(processName)) return true;
                            try { exePath = proc.MainModule?.FileName; } catch { /* access denied */ }
                        }
                    }
                    catch
                    {
                        return true;
                    }

                    results.Add(new RunningWindowItem
                    {
                        Hwnd = hwnd,
                        Title = titleStr,
                        ProcessName = processName,
                        Icon = TryGetIcon(exePath, hwnd),
                        IsActive = hwnd == foreground
                    });
                }
                catch
                {
                    // skip broken hwnd
                }
                return true;
            }, IntPtr.Zero);

            return results;
        }

        public static void ActivateWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return;

            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);

            // AttachThreadInput helps SetForegroundWindow succeed when another app owns focus.
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint currentThread = GetCurrentThreadId();

            bool attachedFg = false;
            bool attachedTarget = false;
            try
            {
                if (foregroundThread != currentThread)
                    attachedFg = AttachThreadInput(currentThread, foregroundThread, true);
                if (targetThread != currentThread && targetThread != foregroundThread)
                    attachedTarget = AttachThreadInput(currentThread, targetThread, true);

                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
                if (attachedFg) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        public static void ActivateWpfWindow(Window window)
        {
            if (window == null) return;
            try
            {
                if (!window.IsVisible) window.Show();
                if (window.WindowState == WindowState.Minimized)
                    window.WindowState = WindowState.Normal;

                window.Activate();
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                    ActivateWindow(hwnd);
            }
            catch { }
        }

        private static ImageSource TryGetIcon(string exePath, IntPtr hwnd)
        {
            try
            {
                Icon icon = null;
                if (!string.IsNullOrEmpty(exePath))
                {
                    try { icon = Icon.ExtractAssociatedIcon(exePath); } catch { }
                }

                if (icon == null)
                {
                    IntPtr hIcon = SendMessage(hwnd, 0x007F /* WM_GETICON */, new IntPtr(1) /* ICON_SMALL */, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero)
                        hIcon = SendMessage(hwnd, 0x007F, new IntPtr(0) /* ICON_BIG */, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero)
                        hIcon = GetClassLongPtr(hwnd, -14 /* GCL_HICON */);
                    if (hIcon != IntPtr.Zero)
                        icon = Icon.FromHandle(hIcon);
                }

                if (icon == null) return null;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(20, 20));
                source.Freeze();
                return source;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCloaked(IntPtr hwnd)
        {
            try
            {
                int cloaked = 0;
                int hr = DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out cloaked, sizeof(int));
                return hr == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLongPtr32(hWnd, nIndex);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    }
}
