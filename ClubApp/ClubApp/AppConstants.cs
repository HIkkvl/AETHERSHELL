using System;
using System.Collections.Generic;
using System.IO;

namespace AetherShell.Client
{
    public static class AppConstants
    {
        // Объявлено первым намеренно: статические инициализаторы выполняются
        // сверху вниз, а свойства ниже читают уже загруженный конфиг.
        private static readonly Dictionary<string, string> Settings = LoadSettings();

        /// <summary>Полный адрес сервера из server.config (SERVER_URL), например https://aether.example.com.</summary>
        public static string SERVER_URL { get; } = LoadServerUrl();

        /// <summary>Ключ клуба из server.config (CLUB_KEY): по нему сервер понимает, к какому клубу относится этот ПК.</summary>
        public static string CLUB_KEY { get; } = ReadSetting("CLUB_KEY", "");

        /// <summary>Пароль высокого доступа (Ctrl+Alt+P). Читается из server.config (EXIT_PASSWORD=...). По умолчанию "1478".</summary>
        public static string EXIT_SHELL_PASSWORD { get; } = ReadSetting("EXIT_PASSWORD", "1478");

        private const string DefaultServerUrl = "http://localhost:5232";

        private static string LoadServerUrl()
        {
            var url = ReadSetting("SERVER_URL", "").TrimEnd('/');
            if (url.Length == 0) return DefaultServerUrl;

            // Установщик может записать голый хост — достраиваем схему, иначе Uri не соберётся.
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out _) ? url : DefaultServerUrl;
        }

        private static string ReadSetting(string key, string fallback)
        {
            string value;
            return Settings.TryGetValue(key, out value) && value.Length > 0 ? value : fallback;
        }

        private static Dictionary<string, string> LoadSettings()
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var configPath = GetConfigPath();
                if (string.IsNullOrEmpty(configPath)) return settings;

                foreach (var line in File.ReadAllLines(configPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                    var separator = trimmed.IndexOf('=');
                    if (separator <= 0) continue;

                    settings[trimmed.Substring(0, separator).Trim()] = trimmed.Substring(separator + 1).Trim();
                }
            }
            catch
            {
                // Без конфига клиент поднимется на дефолтах и покажет ошибку подключения.
            }

            return settings;
        }

        private static string GetConfigPath()
        {
            // Ищем рядом с exe (bin\Debug при F5, C:\AetherShell при установке).
            // AppContext.BaseDirectory на .NET Framework иногда расходится с реальной
            // папкой exe — поэтому проверяем несколько кандидатов.
            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "server.config"),
                Path.Combine(AppContext.BaseDirectory ?? "", "server.config"),
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "server.config"),
                @"C:\AetherShell\server.config",
            };

            foreach (var path in candidates)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }

            return null;
        }

        // API Endpoints
        public const string API_APPS = "/api/Apps";
        public const string API_AUTH_LOGIN = "/api/Auth/Login";
        public const string API_AUTH_REGISTER = "/api/Auth/Register";
        public const string API_AUTH_STATUS = "/api/Auth/status";
        public const string API_AUTH_BUY = "/api/Auth/buy";
        public const string API_AUTH_LOGOUT = "/api/Auth/logout";
        public const string API_TARIFFS = "/api/Tariffs";
        public const string API_PRODUCTS = "/api/Products";
        public const string API_ORDERS = "/api/Orders";
        public const string API_PAYMENT_CREATE_LINK = "/api/payment/create-link";
        public const string API_BANNERS = "/api/Banners";

        /// <summary>Заголовок, которым каждый запрос сообщает серверу свой клуб.</summary>
        public const string CLUB_KEY_HEADER = "X-Club-Key";

        // SignalR Hub
        public const string HUB_NAME = "/clubhub";

        // Warning thresholds
        public const int WARNING_MINUTES_10 = 10;
        public const int WARNING_MINUTES_5 = 5;
        public const double WARNING_MINUTES_5_THRESHOLD = 9.9;

        // File paths
        public const string SOUND_WARNING_PATH = "Sounds/warning.mp3";

        // Window styles
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        // Window positions
        public static readonly System.IntPtr HWND_BOTTOM = new System.IntPtr(1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
    }
}
