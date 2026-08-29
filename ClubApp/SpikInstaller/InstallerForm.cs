using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AetherShell.Installer
{
    public partial class InstallerForm : Form
    {
        private Label titleLabel;
        private Label statusLabel;
        private ProgressBar progressBar;
        private TextBox serverUrlBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private CheckBox autoLoginCheck;
        private Button installButton;
        private Label serverLabel;
        private Label serverHintLabel;
        private Label userLabel;
        private Label passLabel;

        private readonly string _installPath = @"C:\AetherShell";
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>Ключ клуба из скачанного архива: определяет, к какому клубу привяжется этот ПК.</summary>
        private string _clubKey = "";
        private string _clubName = "";

        public InstallerForm()
        {
            InitializeComponent();
            TryLoadServerConfig();
        }

        /// <summary>
        /// Установщик приходит в архиве вместе с server.config, где уже прописаны
        /// адрес сервера и ключ клуба, поэтому вводить ничего не нужно.
        /// </summary>
        private void TryLoadServerConfig()
        {
            var configPath = ResolveBesideInstaller("server.config");
            if (configPath == null)
            {
                serverHintLabel.Text = "server.config не найден: распакуйте весь zip из кабинета (exe + server.config рядом)";
                serverHintLabel.ForeColor = Color.FromArgb(240, 140, 100);
                return;
            }

            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (value.Length == 0) continue;

                    switch (key.ToUpperInvariant())
                    {
                        case "SERVER_URL":
                            serverUrlBox.Text = value;
                            serverUrlBox.ReadOnly = true;
                            serverUrlBox.ForeColor = Color.FromArgb(100, 200, 100);
                            break;
                        case "CLUB_KEY":
                            _clubKey = value;
                            break;
                        case "CLUB_NAME":
                            _clubName = value;
                            break;
                    }
                }

                if (_clubName.Length > 0)
                {
                    serverHintLabel.Text = $"Клуб: {_clubName}";
                    serverHintLabel.ForeColor = Color.FromArgb(100, 200, 100);
                }
            }
            catch (Exception ex)
            {
                serverHintLabel.Text = "Не удалось прочитать server.config: " + ex.Message;
                serverHintLabel.ForeColor = Color.FromArgb(240, 140, 100);
            }
        }

        /// <summary>
        /// Каталог с запущенным exe. У single-file publish <see cref="AppContext.BaseDirectory"/>
        /// указывает во временную папку распаковки .NET — рядом с ней нет server.config из zip.
        /// </summary>
        private static string GetInstallerDirectory()
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var dir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(dir))
                        return dir;
                }
            }
            catch { /* fallback below */ }

            try
            {
                var main = System.Reflection.Assembly.GetEntryAssembly()?.Location
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(main))
                {
                    var dir = Path.GetDirectoryName(main);
                    if (!string.IsNullOrEmpty(dir) && !dir.Contains($"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch { /* fallback below */ }

            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(cwd))
                return cwd;

            return AppContext.BaseDirectory;
        }

        /// <summary>Ищет файл рядом с установщиком, затем в текущей папке и в BaseDirectory.</summary>
        private static string? ResolveBesideInstaller(string fileName)
        {
            foreach (var dir in new[]
                     {
                         GetInstallerDirectory(),
                         Directory.GetCurrentDirectory(),
                         AppContext.BaseDirectory
                     })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var path = Path.GetFullPath(Path.Combine(dir, fileName));
                    if (File.Exists(path))
                        return path;
                }
                catch { /* next */ }
            }
            return null;
        }

        private void InitializeComponent()
        {
            this.Text = "Spik Client - Установка";
            this.Size = new Size(500, 450);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);

            // Title
            titleLabel = new Label
            {
                Text = "🎮 Установка Spik Client",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(30, 20)
            };
            this.Controls.Add(titleLabel);

            // Адрес сервера — обычно уже заполнен из server.config рядом с установщиком
            serverLabel = new Label
            {
                Text = "Адрес сервера:",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(30, 80),
                AutoSize = true
            };
            this.Controls.Add(serverLabel);

            serverUrlBox = new TextBox
            {
                Text = "",
                Font = new Font("Segoe UI", 12),
                Location = new Point(30, 105),
                Size = new Size(420, 30),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(serverUrlBox);

            serverHintLabel = new Label
            {
                Text = "Заполняется автоматически из server.config",
                Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(30, 138),
                AutoSize = true
            };
            this.Controls.Add(serverHintLabel);

            // Auto login checkbox
            autoLoginCheck = new CheckBox
            {
                Text = "Настроить автоматический вход в Windows",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                Location = new Point(30, 150),
                AutoSize = true,
                Checked = true
            };
            autoLoginCheck.CheckedChanged += AutoLoginCheck_Changed;
            this.Controls.Add(autoLoginCheck);

            // Username
            userLabel = new Label
            {
                Text = "Имя пользователя Windows:",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(30, 185),
                AutoSize = true
            };
            this.Controls.Add(userLabel);

            usernameBox = new TextBox
            {
                Text = Environment.UserName,
                Font = new Font("Segoe UI", 12),
                Location = new Point(30, 210),
                Size = new Size(200, 30),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(usernameBox);

            // Password
            passLabel = new Label
            {
                Text = "Пароль Windows:",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(250, 185),
                AutoSize = true
            };
            this.Controls.Add(passLabel);

            passwordBox = new TextBox
            {
                Font = new Font("Segoe UI", 12),
                Location = new Point(250, 210),
                Size = new Size(200, 30),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = true
            };
            this.Controls.Add(passwordBox);

            // Progress bar
            progressBar = new ProgressBar
            {
                Location = new Point(30, 280),
                Size = new Size(420, 25),
                Style = ProgressBarStyle.Continuous
            };
            this.Controls.Add(progressBar);

            // Status
            statusLabel = new Label
            {
                Text = "Готов к установке",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(30, 310),
                Size = new Size(420, 20)
            };
            this.Controls.Add(statusLabel);

            // Install button
            installButton = new Button
            {
                Text = "🚀 Установить",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(30, 350),
                Size = new Size(420, 50),
                BackColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            installButton.FlatAppearance.BorderSize = 0;
            installButton.Click += InstallButton_Click;
            this.Controls.Add(installButton);
        }

        private void AutoLoginCheck_Changed(object sender, EventArgs e)
        {
            usernameBox.Enabled = autoLoginCheck.Checked;
            passwordBox.Enabled = autoLoginCheck.Checked;
            userLabel.ForeColor = autoLoginCheck.Checked ? Color.FromArgb(148, 163, 184) : Color.Gray;
            passLabel.ForeColor = autoLoginCheck.Checked ? Color.FromArgb(148, 163, 184) : Color.Gray;
        }

        private async void InstallButton_Click(object sender, EventArgs e)
        {
            installButton.Enabled = false;
            
            try
            {
                await InstallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка установки: {ex.Message}", "Ошибка", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                installButton.Enabled = true;
            }
        }

        private async Task InstallAsync()
        {
            var serverUrl = NormalizeServerUrl(serverUrlBox.Text);

            if (serverUrl == null)
            {
                MessageBox.Show("Укажите адрес сервера, например https://aether.example.com",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                installButton.Enabled = true;
                return;
            }

            if (string.IsNullOrEmpty(_clubKey))
            {
                var proceed = MessageBox.Show(
                    "В server.config нет ключа клуба (CLUB_KEY). Без него шелл не сможет подключиться.\n\n" +
                    "Скачайте установщик из кабинета клуба. Продолжить всё равно?",
                    "Нет ключа клуба", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (proceed != DialogResult.Yes)
                {
                    installButton.Enabled = true;
                    return;
                }
            }

            // Шаг 1: Создание папки
            UpdateStatus("Создание папки установки...", 10);
            if (!Directory.Exists(_installPath))
            {
                Directory.CreateDirectory(_installPath);
            }

            // Шаг 2: Скачивание/копирование файлов
            UpdateStatus("Получение файлов клиента...", 20);
            await CopyClientFilesAsync(serverUrl);

            // Шаг 3: Конфиг с адресом сервера и ключом клуба
            UpdateStatus("Настройка подключения к серверу...", 40);
            UpdateServerConfig(serverUrl);

            // Шаг 4: Установка Watchdog службы
            UpdateStatus("Установка службы мониторинга...", 50);
            InstallWatchdogService();

            // Шаг 5: Отключение Explorer и замена Shell
            UpdateStatus("Отключение стандартного Explorer...", 60);
            DisableExplorerShell();

            // Шаг 6: Настройка автозапуска
            UpdateStatus("Настройка автозапуска...", 70);
            SetupAutoStart();

            // Шаг 7: Настройка автологина
            if (autoLoginCheck.Checked)
            {
                UpdateStatus("Настройка автоматического входа...", 85);
                SetupAutoLogin(usernameBox.Text, passwordBox.Text);
            }

            UpdateStatus("Установка завершена!", 100);
            
            // Проверяем что установлено
            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
            {
                clientExe = Path.Combine(_installPath, "clubApp.exe"); // legacy name
            }
            
            var filesInstalled = File.Exists(clientExe);
            var fileCount = filesInstalled ? Directory.GetFiles(_installPath, "*.*", SearchOption.AllDirectories).Length : 0;
            
            // Проверяем shell в реестре
            string? shellValue = null;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                shellValue = key?.GetValue("Shell")?.ToString();
            }
            catch { }
            
            var shellReplaced = shellValue != null && (shellValue.Contains("clubApp") || shellValue.Contains("SpikClient") || shellValue.Contains("AetherShell.Client"));
            
            var summary = $"Установка завершена!\n\n" +
                $"📁 Файлов установлено: {fileCount}\n" +
                $"📍 Путь: {_installPath}\n" +
                $"🖥️ Shell заменён: {(shellReplaced ? "Да" : "Нет")}\n" +
                $"🔧 Shell в реестре: {shellValue ?? "не найден"}\n\n" +
                $"Перезагрузить компьютер сейчас?";
            
            var result = MessageBox.Show(summary, "Установка завершена", 
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                Process.Start("shutdown", "/r /t 5 /c \"Перезагрузка для завершения установки Spik Client\"");
                Application.Exit();
            }
        }

        /// <summary>
        /// Приводит ввод к абсолютному URL. Голый хост считаем https:
        /// сервер теперь публичный, а не машина в локальной сети клуба.
        /// </summary>
        private static string? NormalizeServerUrl(string raw)
        {
            var value = (raw ?? "").Trim().TrimEnd('/');
            if (value.Length == 0) return null;

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out _) ? value : null;
        }

        private async Task CopyClientFilesAsync(string serverUrl)
        {
            var installerDir = GetInstallerDirectory();
            
            // Сначала проверяем локальные файлы (они приоритетнее)
            var possibleSources = new[]
            {
                Path.Combine(installerDir, "ClientFiles"),
                Path.Combine(installerDir, "..", "ClientFiles"),
                Path.Combine(installerDir, "..", "ClubApp", "bin", "Release"),
                Path.Combine(installerDir, "..", "ClubApp", "bin", "Debug"),
                Path.Combine(installerDir, "..", "..", "ClubApp", "ClubApp", "bin", "Release"),
                Path.Combine(installerDir, "..", "..", "ClubApp", "ClubApp", "bin", "Debug")
            };

            string? sourceDir = null;
            foreach (var src in possibleSources)
            {
                var fullPath = Path.GetFullPath(src);
                var hasClient = File.Exists(Path.Combine(fullPath, "AetherShell.Client.exe")) || File.Exists(Path.Combine(fullPath, "SpikClient.exe")) || File.Exists(Path.Combine(fullPath, "clubApp.exe"));
                
                if (Directory.Exists(fullPath) && hasClient)
                {
                    sourceDir = fullPath;
                    UpdateStatus($"Найдены локальные файлы: {fullPath}", 22);
                    break;
                }
            }

            // Если локальные файлы найдены - копируем их
            if (sourceDir != null)
            {
                UpdateStatus("Копирование файлов клиента...", 25);
                int fileCount = 0;
                
                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var destPath = Path.Combine(_installPath, relativePath);
                    
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    File.Copy(file, destPath, true);
                    fileCount++;
                }
                
                UpdateStatus($"Скопировано {fileCount} файлов", 35);
                
                // Также копируем Watchdog если есть
                await CopyWatchdogFilesAsync(installerDir);
                return;
            }

            // Если локальных файлов нет - пробуем скачать с сервера
            UpdateStatus("Локальные файлы не найдены, пробуем скачать с сервера...", 22);
            var downloaded = await TryDownloadFromServerAsync(serverUrl);
            
            if (downloaded)
            {
                return;
            }

            // Последняя надежда - диалог выбора папки
            UpdateStatus("Выберите папку с файлами клиента...", 25);
            using var dialog = new FolderBrowserDialog
            {
                Description = "Выберите папку с файлами AetherShell Client"
            };
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                sourceDir = dialog.SelectedPath;
                
                int fileCount = 0;
                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var destPath = Path.Combine(_installPath, relativePath);
                    
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    File.Copy(file, destPath, true);
                    fileCount++;
                }
                
                UpdateStatus($"Скопировано {fileCount} файлов", 35);
                await CopyWatchdogFilesAsync(installerDir);
            }
            else
            {
                throw new Exception("Файлы клиента не найдены и не выбраны");
            }
        }

        private async Task<bool> TryDownloadFromServerAsync(string serverUrl)
        {
            var clientZipUrl = serverUrl + "/api/download/client";
            var watchdogZipUrl = serverUrl + "/api/download/watchdog";
            
            var clientZipPath = Path.Combine(Path.GetTempPath(), "AetherShell.Client.zip");
            var watchdogZipPath = Path.Combine(Path.GetTempPath(), "AetherShell.Watchdog.zip");

            try
            {
                // Скачиваем клиент
                UpdateStatus("Скачивание клиента с сервера...", 20);
                
                using (var response = await _httpClient.GetAsync(clientZipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        UpdateStatus($"Сервер не вернул клиент (код {(int)response.StatusCode})", 22);
                        return false;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(clientZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    
                    var buffer = new byte[8192];
                    long downloadedBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)(20 + (downloadedBytes * 10 / totalBytes));
                            UpdateStatus($"Скачивание клиента: {downloadedBytes / 1024} / {totalBytes / 1024} KB", progress);
                        }
                    }
                }

                // Распаковываем клиент
                UpdateStatus("Распаковка клиента...", 32);
                if (Directory.Exists(_installPath))
                {
                    // Удаляем старые файлы, кроме конфигов
                    foreach (var file in Directory.GetFiles(_installPath))
                    {
                        if (!file.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                ZipFile.ExtractToDirectory(clientZipPath, _installPath, true);

                // Пробуем скачать watchdog
                UpdateStatus("Скачивание службы мониторинга...", 35);
                try
                {
                    using var watchdogResponse = await _httpClient.GetAsync(watchdogZipUrl);
                    if (watchdogResponse.IsSuccessStatusCode)
                    {
                        using var watchdogStream = await watchdogResponse.Content.ReadAsStreamAsync();
                        using var watchdogFileStream = new FileStream(watchdogZipPath, FileMode.Create);
                        await watchdogStream.CopyToAsync(watchdogFileStream);
                        watchdogFileStream.Close();
                        
                        UpdateStatus("Распаковка службы мониторинга...", 38);
                        ZipFile.ExtractToDirectory(watchdogZipPath, _installPath, true);
                    }
                }
                catch
                {
                    // Watchdog опционален
                    UpdateStatus("Watchdog недоступен на сервере, пропускаем...", 38);
                }

                // Очистка
                try { File.Delete(clientZipPath); } catch { }
                try { File.Delete(watchdogZipPath); } catch { }

                return true;
            }
            catch (HttpRequestException ex)
            {
                UpdateStatus($"Ошибка подключения к серверу: {ex.Message}", 22);
                return false;
            }
            catch (TaskCanceledException)
            {
                UpdateStatus("Таймаут скачивания", 22);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ошибка скачивания: {ex.Message}", 22);
                return false;
            }
        }

        private async Task CopyWatchdogFilesAsync(string installerDir)
        {
            var watchdogSources = new[]
            {
                Path.Combine(installerDir, "WatchdogFiles"),
                Path.Combine(installerDir, "..", "SpikWatchdog", "bin", "Release", "net8.0-windows"),
                Path.Combine(installerDir, "..", "SpikWatchdog", "bin", "Debug", "net8.0-windows") // folder name unchanged
            };

            foreach (var src in watchdogSources)
            {
                var fullPath = Path.GetFullPath(src);
                if (Directory.Exists(fullPath) && (File.Exists(Path.Combine(fullPath, "AetherShell.Watchdog.exe")) || File.Exists(Path.Combine(fullPath, "SpikWatchdog.exe"))))
                {
                    foreach (var file in Directory.GetFiles(fullPath))
                    {
                        File.Copy(file, Path.Combine(_installPath, Path.GetFileName(file)), true);
                    }
                    break;
                }
            }

            await Task.Delay(100);
        }

        private void UpdateServerConfig(string serverUrl)
        {
            var lines = new System.Collections.Generic.List<string> { "SERVER_URL=" + serverUrl };

            if (_clubKey.Length > 0) lines.Add("CLUB_KEY=" + _clubKey);
            if (_clubName.Length > 0) lines.Add("CLUB_NAME=" + _clubName);

            // Пароль выхода из шелла задаётся клубом вручную, поэтому не затираем его.
            var configPath = Path.Combine(_installPath, "server.config");
            var existingExitPassword = ReadExitPassword(configPath);
            if (existingExitPassword != null) lines.Add("EXIT_PASSWORD=" + existingExitPassword);

            File.WriteAllText(configPath, string.Join(Environment.NewLine, lines));
        }

        private static string? ReadExitPassword(string configPath)
        {
            if (!File.Exists(configPath)) return null;

            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("EXIT_PASSWORD=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("EXIT_PASSWORD=".Length).Trim();
                        return value.Length > 0 ? value : null;
                    }
                }
            }
            catch { }

            return null;
        }

        private void InstallWatchdogService()
        {
            var watchdogExe = Path.Combine(_installPath, "AetherShell.Watchdog.exe");
            if (!File.Exists(watchdogExe))
            {
                return; // Watchdog не найден, пропускаем
            }

            var serviceName = "AetherShell.Watchdog";

            // Останавливаем и удаляем если существует
            try
            {
                var stopInfo = new ProcessStartInfo("sc.exe", $"stop {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(stopInfo)?.WaitForExit();

                var deleteInfo = new ProcessStartInfo("sc.exe", $"delete {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(deleteInfo)?.WaitForExit();
                
                System.Threading.Thread.Sleep(2000);
            }
            catch { }

            // Создаём службу
            var createInfo = new ProcessStartInfo("sc.exe", 
                $"create {serviceName} binPath= \"{watchdogExe}\" start= auto DisplayName= \"Spik Client Watchdog\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(createInfo)?.WaitForExit();

            // Настраиваем восстановление при сбое
            var failureInfo = new ProcessStartInfo("sc.exe",
                $"failure {serviceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(failureInfo)?.WaitForExit();

            // Запускаем службу
            var startInfo = new ProcessStartInfo("sc.exe", $"start {serviceName}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo)?.WaitForExit();
        }

        private void DisableExplorerShell()
        {
            // Определяем путь к клиенту
            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
            {
                clientExe = Path.Combine(_installPath, "clubApp.exe"); // legacy name
            }

            if (!File.Exists(clientExe))
            {
                MessageBox.Show($"Клиент не найден в {_installPath}!\nПроверьте, что файлы скопированы.", 
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UpdateStatus($"Заменяем shell на: {clientExe}", 61);
            
            int successCount = 0;

            // 1. Заменяем Shell для ВСЕХ пользователей (HKLM)
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (key != null)
                    {
                        var originalShell = key.GetValue("Shell")?.ToString();
                        if (!string.IsNullOrEmpty(originalShell) && originalShell != clientExe)
                        {
                            key.SetValue("Shell_Backup", originalShell, RegistryValueKind.String);
                        }
                        key.SetValue("Shell", clientExe, RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("Shell заменён в HKLM", 62);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKLM Shell: {ex.Message}", 62);
            }

            // 2. Также устанавливаем для текущего пользователя (HKCU)
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true) 
                    ?? Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                {
                    if (key != null)
                    {
                        key.SetValue("Shell", clientExe, RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("Shell заменён в HKCU", 63);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKCU Shell: {ex.Message}", 63);
            }

            // 3. Убираем explorer из автозапуска Run
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        try { key.DeleteValue("Explorer", false); } catch { }
                        try { key.DeleteValue("explorer", false); } catch { }
                    }
                }
            }
            catch { }

            if (successCount > 0)
            {
                UpdateStatus($"Explorer заменён на AetherShell Client ({successCount} ключей)", 65);
            }
            else
            {
                MessageBox.Show("Не удалось заменить shell!\nЗапустите установщик от имени Администратора.", 
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupAutoStart()
        {
            // Поддерживаем оба имени файла
            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
            {
                clientExe = Path.Combine(_installPath, "clubApp.exe"); // legacy name
            }

            if (!File.Exists(clientExe))
            {
                UpdateStatus("Клиент не найден для автозапуска!", 72);
                return;
            }

            int successCount = 0;

            // 1. Добавляем в реестр Run для текущего пользователя
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AetherShell.Client", $"\"{clientExe}\"", RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("Автозапуск: HKCU Run ✓", 72);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKCU Run: {ex.Message}", 72);
            }

            // 2. Добавляем в реестр Run для всех пользователей
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AetherShell.Client", $"\"{clientExe}\"", RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("Автозапуск: HKLM Run ✓", 74);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKLM Run: {ex.Message}", 74);
            }

            // 3. Создаём задачу планировщика
            try
            {
                var taskName = "AetherShell.ClientAutoStart";

                var deleteInfo = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{taskName}\" /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(deleteInfo)?.WaitForExit();

                var createInfo = new ProcessStartInfo("schtasks.exe",
                    $"/Create /TN \"{taskName}\" /TR \"\\\"{clientExe}\\\"\" /SC ONLOGON /RL HIGHEST /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var proc = Process.Start(createInfo);
                proc?.WaitForExit();
                
                if (proc?.ExitCode == 0)
                {
                    successCount++;
                    UpdateStatus("Автозапуск: Задача планировщика ✓", 76);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Задача: {ex.Message}", 76);
            }

            // 4. Добавляем в папку автозагрузки
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                var shortcutPath = Path.Combine(startupFolder, "AetherShell.Client.lnk");
                
                CreateShortcut(shortcutPath, clientExe);
                
                if (File.Exists(shortcutPath))
                {
                    successCount++;
                    UpdateStatus("Автозапуск: Ярлык в автозагрузке ✓", 78);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Ярлык: {ex.Message}", 78);
            }

            UpdateStatus($"Настроено {successCount} методов автозапуска", 80);
        }

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            try
            {
                // Используем VBScript для создания ярлыка (работает везде)
                var vbsPath = Path.Combine(Path.GetTempPath(), "create_shortcut.vbs");
                var vbsContent = $@"
Set WshShell = WScript.CreateObject(""WScript.Shell"")
Set shortcut = WshShell.CreateShortcut(""{shortcutPath}"")
shortcut.TargetPath = ""{targetPath}""
shortcut.WorkingDirectory = ""{Path.GetDirectoryName(targetPath)}""
shortcut.Save
";
                File.WriteAllText(vbsPath, vbsContent);
                
                var psi = new ProcessStartInfo("cscript.exe", $"//nologo \"{vbsPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();
                
                try { File.Delete(vbsPath); } catch { }
            }
            catch { }
        }

        private void SetupAutoLogin(string username, string password)
        {
            if (string.IsNullOrEmpty(username)) return;

            var regPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            using var key = Registry.LocalMachine.OpenSubKey(regPath, true);
            
            if (key != null)
            {
                key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
                key.SetValue("DefaultUserName", username, RegistryValueKind.String);
                key.SetValue("DefaultPassword", password ?? "", RegistryValueKind.String);
                key.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
            }
        }

        private void UpdateStatus(string message, int progress)
        {
            statusLabel.Text = message;
            progressBar.Value = progress;
            Application.DoEvents();
        }
    }
}
