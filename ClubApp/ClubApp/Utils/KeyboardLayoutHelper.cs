using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AetherShell.Client.Utils
{
    public static class KeyboardLayoutHelper
    {
        /// <summary>Короткий код раскладки: ENG, РУС, UKR и т.д.</summary>
        public static string GetCurrentLayoutLabel()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint threadId = GetWindowThreadProcessId(foreground, out _);
                IntPtr hkl = GetKeyboardLayout(threadId);
                int langId = unchecked((ushort)hkl.ToInt64());
                if (langId == 0) return "—";

                var culture = new CultureInfo(langId);
                string iso = culture.TwoLetterISOLanguageName?.ToUpperInvariant() ?? "??";

                switch (iso)
                {
                    case "EN": return "ENG";
                    case "RU": return "РУС";
                    case "UK": return "УКР";
                    case "DE": return "DEU";
                    case "FR": return "FRA";
                    case "ES": return "ESP";
                    case "IT": return "ITA";
                    case "PL": return "POL";
                    case "KK": return "ҚАЗ";
                    default: return iso;
                }
            }
            catch
            {
                return "—";
            }
        }

        /// <summary>Переключить на следующую установленную раскладку.</summary>
        public static void CycleNextLayout()
        {
            try
            {
                int count = GetKeyboardLayoutList(0, null);
                if (count <= 1) return;

                var layouts = new IntPtr[count];
                GetKeyboardLayoutList(count, layouts);

                IntPtr foreground = GetForegroundWindow();
                uint threadId = GetWindowThreadProcessId(foreground, out _);
                IntPtr current = GetKeyboardLayout(threadId);

                int idx = Array.IndexOf(layouts, current);
                if (idx < 0) idx = 0;
                IntPtr next = layouts[(idx + 1) % layouts.Length];

                ActivateKeyboardLayout(next, 0);
                PostMessage(foreground, 0x0050 /* WM_INPUTLANGCHANGEREQUEST */, IntPtr.Zero, next);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("user32.dll")]
        private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}
