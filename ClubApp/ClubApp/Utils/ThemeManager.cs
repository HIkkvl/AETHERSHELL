using System.Windows;
using System.Windows.Media;

namespace AetherShell.Client.Utils
{
    public static class ThemeManager
    {
        private static readonly BrushConverter _brushConverter = new BrushConverter();

        public static void ApplyDarkTheme(Window window)
        {
            SetResourceColor(window, "WindowBackgroundBrush", "#241820");
            SetResourceColor(window, "SecondaryBackgroundBrush", "#1A141B");
            SetResourceColor(window, "PanelBackgroundBrush", "#342735");
            SetResourceColor(window, "PrimaryBrush", "#FF5ED7");
            SetResourceColor(window, "CardBackgroundBrush", "#3C2F3D");
            SetResourceColor(window, "ActionBtnBackgroundBrush", "#080C17");
            SetResourceColor(window, "PrimaryTextBrush", "#1A1A1A");
            SetResourceColor(window, "TextBrush", "#FFFFFF");
            SetResourceColor(window, "MutedTextBrush", "#6B7280");
        }

        public static void ApplyLightTheme(Window window)
        {
            // Светлая тема с современной палитрой
            SetResourceColor(window, "WindowBackgroundBrush", "#F8F9FA"); // Светлый фон окна
            SetResourceColor(window, "SecondaryBackgroundBrush", "#FFFFFF"); // Белый фон
            SetResourceColor(window, "PanelBackgroundBrush", "#E9ECEF"); // Светло-серый для панелей
            SetResourceColor(window, "PrimaryBrush", "#F4C430"); // Яркий золотой для акцентов
            SetResourceColor(window, "CardBackgroundBrush", "#FFFFFF"); // Белый для карточек
            SetResourceColor(window, "ActionBtnBackgroundBrush", "#F8F9FA"); // Светлый для кнопок
            SetResourceColor(window, "PrimaryTextBrush", "#1A1A1A"); // Почти черный для текста на золотом
            SetResourceColor(window, "TextBrush", "#212529"); // Темный текст
            SetResourceColor(window, "MutedTextBrush", "#6C757D"); // Приглушенный текст
            SetResourceColor(window, "SecondaryMutedBrush", "#868E96"); // Дополнительный приглушенный
            SetResourceColor(window, "LabelTextBrush", "#495057"); // Для меток
            SetResourceColor(window, "DarkBackgroundBrush", "#DEE2E6"); // Для темных элементов на светлом
            SetResourceColor(window, "AdBackgroundBrush", "#F1F3F5"); // Фон для рекламы
            SetResourceColor(window, "DividerBrush", "#DEE2E6"); // Разделители
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
