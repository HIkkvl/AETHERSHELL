using System.Net;
using System.Net.Mail;
using AetherShell.Server.Data;

namespace AetherShell.Server.Services
{
    public class EmailService
    {
        private readonly ServerSettings _settings;

        public EmailService(ServerSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Настроен ли SMTP — позволяет вызывающему не обещать письмо, которое не уйдёт.</summary>
        public bool IsConfigured =>
            !string.IsNullOrEmpty(ResolveHost()) && !string.IsNullOrEmpty(ResolveUser());

        public async Task SendEmailAsync(string toEmail, string subject, string message)
        {
            var smtpHost = ResolveHost();
            var smtpUser = ResolveUser();
            var smtpPass = ResolvePassword();
            var smtpPort = ResolvePort();

            if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser))
            {
                Console.WriteLine("[Email] SMTP не настроен: задайте SPIK_SMTP_HOST, SPIK_SMTP_USER, SPIK_SMTP_PASSWORD");
                return;
            }

            try
            {
                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(smtpUser, smtpPass),
                    EnableSsl = _settings.SmtpUseSsl
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(smtpUser, "AetherShell"),
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
                Console.WriteLine($"[Email] Отправлено на {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Email] Ошибка отправки: {ex.Message}");
                throw new InvalidOperationException("Не удалось отправить письмо. Проверьте настройки SMTP.", ex);
            }
        }

        // Пароль SMTP — секрет, поэтому только окружение и server-settings.json,
        // который не попадает в репозиторий.
        private string? ResolveHost() =>
            Environment.GetEnvironmentVariable("SPIK_SMTP_HOST") ?? NullIfEmpty(_settings.SmtpHost);

        private string? ResolveUser() =>
            Environment.GetEnvironmentVariable("SPIK_SMTP_USER") ?? NullIfEmpty(_settings.SmtpEmail);

        private string? ResolvePassword() =>
            Environment.GetEnvironmentVariable("SPIK_SMTP_PASSWORD") ?? NullIfEmpty(_settings.SmtpPassword);

        private int ResolvePort()
        {
            var raw = Environment.GetEnvironmentVariable("SPIK_SMTP_PORT");
            if (int.TryParse(raw, out var fromEnv)) return fromEnv;
            return _settings.SmtpPort > 0 ? _settings.SmtpPort : 587;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
