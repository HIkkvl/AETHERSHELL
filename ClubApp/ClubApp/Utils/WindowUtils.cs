using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AetherShell.Client;

namespace AetherShell.Client.Utils
{
    public static class WindowUtils
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public static void SetWindowGhostMode(Window window, bool enableGhost)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                int exStyle = GetWindowLong(hWnd, AppConstants.GWL_EXSTYLE);
                if (enableGhost)
                {
                    SetWindowLong(hWnd, AppConstants.GWL_EXSTYLE, 
                        (exStyle | AppConstants.WS_EX_TOOLWINDOW) & ~AppConstants.WS_EX_APPWINDOW);
                }
                else
                {
                    SetWindowLong(hWnd, AppConstants.GWL_EXSTYLE, 
                        (exStyle & ~AppConstants.WS_EX_TOOLWINDOW) | AppConstants.WS_EX_APPWINDOW);
                }
            }
            catch { }
        }

        public static void SendToBack(Window window)
        {
            try
            {
                IntPtr hWnd = new WindowInteropHelper(window).Handle;
                SetWindowPos(hWnd, AppConstants.HWND_BOTTOM, 0, 0, 0, 0, 
                    AppConstants.SWP_NOMOVE | AppConstants.SWP_NOSIZE | AppConstants.SWP_NOACTIVATE);
            }
            catch { }
        }

        public static class Msg
        {
            /// <summary>
            /// ???????? ??????? ????????? (?????? MessageBox.Show)
            /// </summary>
            public static void Show(string message, string title = "Info")
            {
                var msgBox = new Windows.MessageBox(message, title, false);
                msgBox.ShowDialog();
            }

            /// <summary>
            /// ???????? ?????? (??/???). ?????????? true ???? ?????? ??.
            /// </summary>
            public static bool Ask(string message, string title = "?????????????")
            {
                var msgBox = new Windows.MessageBox(message, title, true);
                msgBox.ShowDialog();
                return msgBox.Result;
            }
        }

        public static void MaximizeWindow(Window window)
        {
            window.WindowState = WindowState.Normal;
            window.WindowState = WindowState.Maximized;
            window.Width = SystemParameters.PrimaryScreenWidth;
            window.Height = SystemParameters.PrimaryScreenHeight;
            window.Top = 0;
            window.Left = 0;
        }
    }
}
