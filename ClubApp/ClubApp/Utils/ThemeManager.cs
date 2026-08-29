using System.Windows;
using System.Windows.Media;

namespace AetherShell.Client.Utils
{
    public static class ThemeManager
    {
        private static readonly (string Key, string Normal, string HighAccess)[] BrushKeys =
        {
            ("AppBackgroundBrush", "#0A0612", "#1A1A1C"),
            ("AccentBrush", "#FF5ED7", "#8A8A8E"),
            ("AccentGoldBrush", "#FF5ED7", "#8A8A8E"),
            ("PanelGlassBrush", "#0AFFFFFF", "#CC2A2A2E"),
            ("TextMutedBrush", "#A299B8", "#9A9A9E"),
            ("TextWhiteBrush", "#FFFFFF", "#E8E8EA"),
            ("BorderSubtleBrush", "#1FFFFFFF", "#555558"),
        };

        private static readonly (string Key, string Normal, string HighAccess)[] ColorKeys =
        {
            ("ColorBg", "#0A0612", "#1A1A1C"),
            ("ColorPanel", "#140C1C", "#2A2A2E"),
            ("ColorGlass", "#1A1224", "#333338"),
            ("ColorAccent", "#BF5AF2", "#8A8A8E"),
            ("ColorAccentDeep", "#FF375F", "#6E6E72"),
            ("ColorAccentPurple", "#8B5CF6", "#555558"),
            ("ColorMuted", "#A299B8", "#9A9A9E"),
            ("ColorBorder", "#1FFFFFFF", "#555558"),
            ("ColorPrimaryGold", "#FF5ED7", "#8A8A8E"),
            ("ColorAccentGold", "#E04BC0", "#6E6E72"),
        };

        public static void ApplyNormalTheme() => Apply(highAccess: false);
        public static void ApplyHighAccessTheme() => Apply(highAccess: true);

        private static void Apply(bool highAccess)
        {
            var app = Application.Current;
            if (app?.Resources == null) return;

            foreach (var (key, normal, gray) in ColorKeys)
            {
                try
                {
                    string hex = highAccess ? gray : normal;
                    app.Resources[key] = (Color)ColorConverter.ConvertFromString(hex);
                }
                catch { }
            }

            foreach (var (key, normal, gray) in BrushKeys)
            {
                try
                {
                    string hex = highAccess ? gray : normal;
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    app.Resources[key] = new SolidColorBrush(color);
                }
                catch { }
            }
        }

        public static void ApplyDarkTheme(Window window)
        {
            SetResourceColor(window, "WindowBackgroundBrush", "#0A0612");
            SetResourceColor(window, "SecondaryBackgroundBrush", "#140C1C");
            SetResourceColor(window, "PanelBackgroundBrush", "#1A1224");
            SetResourceColor(window, "PrimaryBrush", "#FF5ED7");
            SetResourceColor(window, "CardBackgroundBrush", "#1A1224");
            SetResourceColor(window, "ActionBtnBackgroundBrush", "#080C17");
            SetResourceColor(window, "PrimaryTextBrush", "#1A1A1A");
            SetResourceColor(window, "TextBrush", "#FFFFFF");
            SetResourceColor(window, "MutedTextBrush", "#A299B8");
        }

        public static void ApplyLightTheme(Window window)
        {
            SetResourceColor(window, "WindowBackgroundBrush", "#F8F9FA");
            SetResourceColor(window, "SecondaryBackgroundBrush", "#FFFFFF");
            SetResourceColor(window, "PanelBackgroundBrush", "#E9ECEF");
            SetResourceColor(window, "PrimaryBrush", "#F4C430");
            SetResourceColor(window, "CardBackgroundBrush", "#FFFFFF");
            SetResourceColor(window, "ActionBtnBackgroundBrush", "#F8F9FA");
            SetResourceColor(window, "PrimaryTextBrush", "#1A1A1A");
            SetResourceColor(window, "TextBrush", "#212529");
            SetResourceColor(window, "MutedTextBrush", "#6C757D");
        }

        private static void SetResourceColor(Window window, string key, string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                window.Resources[key] = new SolidColorBrush(color);
            }
            catch { }
        }
    }
}
