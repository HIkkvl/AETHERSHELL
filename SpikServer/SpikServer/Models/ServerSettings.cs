namespace AetherShell.Server.Data
{
    /// <summary>
    /// Настройки развёртывания самого сервера. Всё, что относится к конкретному клубу
    /// (название, лояльность, ключ подключения), живёт в таблице Clubs, а не здесь.
    ///
    /// Секреты (пароль БД, JWT, SMTP) задавайте переменными окружения — значения
    /// из этого файла остаются только для локальной разработки.
    /// </summary>
    public class ServerSettings
    {
        /// <summary>Адреса, которые слушает Kestrel. Раньше порт нигде не задавался и служба садилась на 5000.</summary>
        public string Urls { get; set; } = "http://0.0.0.0:5232";

        /// <summary>
        /// Публичный адрес сервиса (https://panel.example.com). Попадает в server.config
        /// установщика, поэтому должен быть тем адресом, который видят клубы.
        /// </summary>
        public string PublicUrl { get; set; } = "";

        // Расширенные настройки сервера
        public bool EnableSwagger { get; set; } = false;

        /// <summary>Сервер стоит за реверс-прокси (nginx, Caddy), который терминирует TLS.</summary>
        public bool BehindReverseProxy { get; set; } = true;

        /// <summary>Требовать HTTPS самим приложением. Нужно только если TLS терминирует Kestrel.</summary>
        public bool EnableHttps { get; set; } = false;

        // Rate Limiting (защита от брутфорса)
        public int LoginRateLimit { get; set; } = 5;           // Попыток в минуту
        public int ApiRateLimit { get; set; } = 100;           // Запросов в минуту

        // База данных
        public string DbHost { get; set; } = "localhost";
        public int DbPort { get; set; } = 5432;
        public string DbName { get; set; } = "spikclub";
        public string DbUser { get; set; } = "postgres";
        public string DbPassword { get; set; } = "";

        // JWT
        public string JwtSecretKey { get; set; } = "";
        public int JwtExpirationHours { get; set; } = 24;

        // SMTP для писем с доступами и восстановления пароля
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string SmtpEmail { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
        public bool SmtpUseSsl { get; set; } = true;

        /// <summary>
        /// Дополнительные origin-ы для CORS. Панель и кабинет отдаются с того же origin,
        /// что и API, поэтому нужны только для `npm run dev`.
        /// </summary>
        public string CorsOrigins { get; set; } = "";

        // Логирование
        public int LogRetentionDays { get; set; } = 90;
        public string LogLevel { get; set; } = "Information";

        // Telegram-бот заявок (python bot.py стартует вместе с сервером, если задан токен)
        public bool TelegramBotEnabled { get; set; } = true;
        public string TelegramBotToken { get; set; } = "";
        public string TelegramChatId { get; set; } = "";
        public string TelegramBotSecret { get; set; } = "";

        /// <summary>Email владельца шелла (PlatformAdmin). Fallback, если нет AETHER_ADMIN_EMAIL.</summary>
        public string AdminEmail { get; set; } = "";

        /// <summary>Пароль владельца шелла. Fallback, если нет AETHER_ADMIN_PASSWORD.</summary>
        public string AdminPassword { get; set; } = "";
    }
}
