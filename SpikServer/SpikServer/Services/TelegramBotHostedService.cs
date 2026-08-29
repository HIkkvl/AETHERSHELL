using System.Diagnostics;
using System.Text;
using AetherShell.Server.Data;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Поднимает python-бота заявок вместе с сервером.
    /// Если токен не задан или bot.py / Python не найдены — тихо пропускает
    /// (в Docker бот обычно живёт отдельным контейнером).
    /// </summary>
    public sealed class TelegramBotHostedService : BackgroundService
    {
        private readonly ILogger<TelegramBotHostedService> _log;
        private readonly ServerSettings _settings;
        private Process? _process;

        public TelegramBotHostedService(
            ILogger<TelegramBotHostedService> log,
            ServerSettings settings)
        {
            _log = log;
            _settings = settings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Даём Kestrel подняться, чтобы бот не долбил недоступный SERVER_URL.
            try
            {
                await Task.Delay(2500, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var envFile = FindBotEnvFile();
            var fileEnv = envFile != null ? ReadDotEnv(envFile) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var token = FirstNonEmpty(
                Environment.GetEnvironmentVariable("BOT_TOKEN"),
                Environment.GetEnvironmentVariable("TG_BOT_TOKEN"),
                _settings.TelegramBotToken,
                Get(fileEnv, "BOT_TOKEN"),
                Get(fileEnv, "TG_BOT_TOKEN"));

            var chatId = FirstNonEmpty(
                Environment.GetEnvironmentVariable("TG_CHAT_ID"),
                _settings.TelegramChatId,
                Get(fileEnv, "TG_CHAT_ID"));

            if (!_settings.TelegramBotEnabled)
            {
                _log.LogInformation("Telegram-бот выключен (TelegramBotEnabled=false)");
                return;
            }

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            {
                _log.LogInformation("Telegram-бот не запущен: задайте BOT_TOKEN/TG_BOT_TOKEN и TG_CHAT_ID (или telegram-bot/.env)");
                return;
            }

            var botPy = FindBotScript();
            if (botPy == null)
            {
                _log.LogInformation("Telegram-бот не запущен: bot.py не найден рядом с сервером");
                return;
            }

            var python = FindPython();
            if (python == null)
            {
                _log.LogWarning("Telegram-бот не запущен: Python не найден в PATH (python / python3 / py)");
                return;
            }

            var serverUrl = FirstNonEmpty(
                Environment.GetEnvironmentVariable("SERVER_URL"),
                Get(fileEnv, "SERVER_URL"),
                string.IsNullOrWhiteSpace(_settings.PublicUrl) ? null : _settings.PublicUrl.TrimEnd('/'),
                "http://127.0.0.1:5232")!;

            var botSecret = FirstNonEmpty(
                Environment.GetEnvironmentVariable("TG_BOT_SECRET"),
                _settings.TelegramBotSecret,
                Get(fileEnv, "TG_BOT_SECRET")) ?? "";

            var adminEmail = FirstNonEmpty(
                Environment.GetEnvironmentVariable("AETHER_ADMIN_EMAIL"),
                Get(fileEnv, "AETHER_ADMIN_EMAIL")) ?? "";

            var adminPassword = FirstNonEmpty(
                Environment.GetEnvironmentVariable("AETHER_ADMIN_PASSWORD"),
                Get(fileEnv, "AETHER_ADMIN_PASSWORD")) ?? "";

            var poll = FirstNonEmpty(
                Environment.GetEnvironmentVariable("POLL_SECONDS"),
                Get(fileEnv, "POLL_SECONDS"),
                "60")!;

            _log.LogInformation("Запускаю Telegram-бот: {Python} {Script} → {Server}", python, botPy, serverUrl);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunBotOnceAsync(python, botPy, token!, chatId!, serverUrl, botSecret, adminEmail, adminPassword, poll, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Telegram-бот упал, перезапуск через 5 с");
                }

                if (stoppingToken.IsCancellationRequested) break;
                try { await Task.Delay(5000, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RunBotOnceAsync(
            string python,
            string botPy,
            string token,
            string chatId,
            string serverUrl,
            string botSecret,
            string adminEmail,
            string adminPassword,
            string pollSeconds,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = Quote(botPy),
                WorkingDirectory = Path.GetDirectoryName(botPy)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // Windows `py -3 script.py`
            if (python.Equals("py", StringComparison.OrdinalIgnoreCase))
                psi.Arguments = $"-3 {Quote(botPy)}";

            psi.Environment["BOT_TOKEN"] = token;
            psi.Environment["TG_CHAT_ID"] = chatId;
            psi.Environment["SERVER_URL"] = serverUrl;
            psi.Environment["TG_BOT_SECRET"] = botSecret;
            psi.Environment["AETHER_ADMIN_EMAIL"] = adminEmail;
            psi.Environment["AETHER_ADMIN_PASSWORD"] = adminPassword;
            psi.Environment["POLL_SECONDS"] = pollSeconds;
            psi.Environment["PYTHONUTF8"] = "1";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _log.LogInformation("[tg-bot] {Line}", e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    _log.LogWarning("[tg-bot] {Line}", e.Data);
            };

            if (!_process.Start())
                throw new InvalidOperationException("Не удалось стартовать процесс Python");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            try
            {
                await _process.WaitForExitAsync(ct);
                _log.LogWarning("Telegram-бот завершился с кодом {Code}", _process.ExitCode);
            }
            finally
            {
                TryKill(_process);
                _process = null;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            TryKill(_process);
            _process = null;
            await base.StopAsync(cancellationToken);
        }

        private static void TryKill(Process? process)
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch { /* already gone */ }
            finally
            {
                process.Dispose();
            }
        }

        private static string? FindBotScript()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "telegram-bot", "bot.py"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "telegram-bot", "bot.py")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "telegram-bot", "bot.py")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "telegram-bot", "bot.py")),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? FindBotEnvFile()
        {
            var script = FindBotScript();
            if (script == null) return null;
            var env = Path.Combine(Path.GetDirectoryName(script)!, ".env");
            return File.Exists(env) ? env : null;
        }

        private static string? FindPython()
        {
            foreach (var name in new[] { "python", "python3", "py" })
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = name,
                        Arguments = name.Equals("py", StringComparison.OrdinalIgnoreCase) ? "-3 -V" : "-V",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } continue; }
                    if (p.ExitCode == 0) return name;
                }
                catch { /* try next */ }
            }
            return null;
        }

        private static Dictionary<string, string> ReadDotEnv(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                var idx = line.IndexOf('=');
                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
                if (key.Length > 0) result[key] = value;
            }
            return result;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static string? Get(Dictionary<string, string> map, string key)
            => map.TryGetValue(key, out var v) ? v : null;

        private static string Quote(string path)
            => path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
