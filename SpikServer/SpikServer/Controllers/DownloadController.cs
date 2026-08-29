using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Контроллер для раздачи файлов клиента (Installer скачивает с сервера)
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class DownloadController : ControllerBase
    {
        private readonly ILogger<DownloadController> _logger;
        private readonly string _clientFilesPath;
        private readonly string _watchdogFilesPath;

        public DownloadController(ILogger<DownloadController> logger)
        {
            _logger = logger;
            
            var baseDir = AppContext.BaseDirectory;
            _clientFilesPath = Path.Combine(baseDir, "ClientFiles");
            _watchdogFilesPath = Path.Combine(baseDir, "WatchdogFiles");
        }

        /// <summary>
        /// Скачать архив с клиентом (AetherShell.Client)
        /// URL: GET /api/download/client
        /// </summary>
        [HttpGet("client")]
        public IActionResult DownloadClient()
        {
            try
            {
                // Проверяем наличие готового архива
                var clientZipPath = Path.Combine(AppContext.BaseDirectory, "AetherShell.Client.zip");
                if (!System.IO.File.Exists(clientZipPath))
                    clientZipPath = Path.Combine(AppContext.BaseDirectory, "SpikClient.zip");
                if (System.IO.File.Exists(clientZipPath))
                {
                    _logger.LogInformation("[Download] Отдаём готовый архив клиента: {Path}", clientZipPath);
                    var stream = new FileStream(clientZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return File(stream, "application/zip", Path.GetFileName(clientZipPath));
                }

                // Если готового архива нет, создаём из папки ClientFiles
                if (!Directory.Exists(_clientFilesPath))
                {
                    _logger.LogWarning("[Download] Папка ClientFiles не найдена: {Path}", _clientFilesPath);
                    return NotFound(new { error = "Файлы клиента не найдены на сервере" });
                }

                var clientExe = Path.Combine(_clientFilesPath, "AetherShell.Client.exe");
                var legacyExe1 = Path.Combine(_clientFilesPath, "SpikClient.exe");
                var legacyExe2 = Path.Combine(_clientFilesPath, "clubApp.exe");
                if (!System.IO.File.Exists(clientExe) && !System.IO.File.Exists(legacyExe1) && !System.IO.File.Exists(legacyExe2))
                {
                    _logger.LogWarning("[Download] Исполняемый файл клиента не найден в {Path}", _clientFilesPath);
                    return NotFound(new { error = "Исполняемый файл клиента не найден" });
                }

                // Создаём архив в памяти
                var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    AddDirectoryToArchive(archive, _clientFilesPath, "");
                }
                
                memoryStream.Position = 0;
                _logger.LogInformation("[Download] Создан и отдан архив клиента из папки ClientFiles");
                
                return File(memoryStream, "application/zip", "AetherShell.Client.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download] Ошибка при скачивании клиента");
                return StatusCode(500, new { error = "Ошибка сервера при подготовке архива" });
            }
        }

        /// <summary>
        /// Скачать архив с Watchdog службой
        /// URL: GET /api/download/watchdog
        /// </summary>
        [HttpGet("watchdog")]
        public IActionResult DownloadWatchdog()
        {
            try
            {
                // Проверяем наличие готового архива
                var watchdogZipPath = Path.Combine(AppContext.BaseDirectory, "AetherShell.Watchdog.zip");
                if (!System.IO.File.Exists(watchdogZipPath))
                    watchdogZipPath = Path.Combine(AppContext.BaseDirectory, "SpikWatchdog.zip");
                if (System.IO.File.Exists(watchdogZipPath))
                {
                    _logger.LogInformation("[Download] Отдаём готовый архив watchdog: {Path}", watchdogZipPath);
                    var stream = new FileStream(watchdogZipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return File(stream, "application/zip", Path.GetFileName(watchdogZipPath));
                }

                // Если готового архива нет, создаём из папки WatchdogFiles
                if (!Directory.Exists(_watchdogFilesPath))
                {
                    _logger.LogWarning("[Download] Папка WatchdogFiles не найдена: {Path}", _watchdogFilesPath);
                    return NotFound(new { error = "Файлы Watchdog не найдены на сервере" });
                }

                var watchdogExe = Path.Combine(_watchdogFilesPath, "AetherShell.Watchdog.exe");
                if (!System.IO.File.Exists(watchdogExe))
                    watchdogExe = Path.Combine(_watchdogFilesPath, "SpikWatchdog.exe");
                if (!System.IO.File.Exists(watchdogExe))
                {
                    _logger.LogWarning("[Download] AetherShell.Watchdog.exe / SpikWatchdog.exe не найден в {Path}", _watchdogFilesPath);
                    return NotFound(new { error = "SpikWatchdog.exe не найден" });
                }

                // Создаём архив в памяти
                var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    AddDirectoryToArchive(archive, _watchdogFilesPath, "");
                }
                
                memoryStream.Position = 0;
                _logger.LogInformation("[Download] Создан и отдан архив Watchdog из папки WatchdogFiles");
                
                return File(memoryStream, "application/zip", "AetherShell.Watchdog.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download] Ошибка при скачивании Watchdog");
                return StatusCode(500, new { error = "Ошибка сервера при подготовке архива" });
            }
        }

        /// <summary>
        /// Скачать Installer
        /// URL: GET /api/download/installer
        /// </summary>
        [HttpGet("installer")]
        public IActionResult DownloadInstaller()
        {
            try
            {
                var installerPath = Path.Combine(AppContext.BaseDirectory, "AetherShell.Installer.exe");
                if (!System.IO.File.Exists(installerPath))
                    installerPath = Path.Combine(AppContext.BaseDirectory, "SpikInstaller.exe");
                if (!System.IO.File.Exists(installerPath))
                    installerPath = Path.Combine(AppContext.BaseDirectory, "Installer", "AetherShell.Installer.exe");
                if (!System.IO.File.Exists(installerPath))
                    installerPath = Path.Combine(AppContext.BaseDirectory, "Installer", "SpikInstaller.exe");

                if (!System.IO.File.Exists(installerPath))
                {
                    _logger.LogWarning("[Download] Installer не найден");
                    return NotFound(new { error = "Installer не найден на сервере" });
                }

                _logger.LogInformation("[Download] Отдаём Installer: {Path}", installerPath);
                var stream = new FileStream(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return File(stream, "application/octet-stream", Path.GetFileName(installerPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Download] Ошибка при скачивании Installer");
                return StatusCode(500, new { error = "Ошибка сервера" });
            }
        }

        /// <summary>
        /// Проверить доступность файлов для скачивания
        /// URL: GET /api/download/status
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var clientZipExists = System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "AetherShell.Client.zip")) || System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "SpikClient.zip"));
            var clientFolderExists = Directory.Exists(_clientFilesPath) && 
                (System.IO.File.Exists(Path.Combine(_clientFilesPath, "AetherShell.Client.exe")) || 
                 System.IO.File.Exists(Path.Combine(_clientFilesPath, "SpikClient.exe")) || 
                 System.IO.File.Exists(Path.Combine(_clientFilesPath, "clubApp.exe")));

            var watchdogZipExists = System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "AetherShell.Watchdog.zip")) || System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "SpikWatchdog.zip"));
            var watchdogFolderExists = Directory.Exists(_watchdogFilesPath) && 
                (System.IO.File.Exists(Path.Combine(_watchdogFilesPath, "AetherShell.Watchdog.exe")) || System.IO.File.Exists(Path.Combine(_watchdogFilesPath, "SpikWatchdog.exe")));

            var installerExists = System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "AetherShell.Installer.exe")) ||
                System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "SpikInstaller.exe")) ||
                System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "Installer", "AetherShell.Installer.exe")) ||
                System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "Installer", "SpikInstaller.exe"));

            return Ok(new
            {
                clientAvailable = clientZipExists || clientFolderExists,
                watchdogAvailable = watchdogZipExists || watchdogFolderExists,
                installerAvailable = installerExists,
                details = new
                {
                    clientZip = clientZipExists,
                    clientFolder = clientFolderExists,
                    watchdogZip = watchdogZipExists,
                    watchdogFolder = watchdogFolderExists
                }
            });
        }

        /// <summary>
        /// Рекурсивно добавляет файлы из директории в архив
        /// </summary>
        private void AddDirectoryToArchive(ZipArchive archive, string sourceDir, string entryPrefix)
        {
            foreach (var filePath in Directory.GetFiles(sourceDir))
            {
                var entryName = string.IsNullOrEmpty(entryPrefix) 
                    ? Path.GetFileName(filePath) 
                    : Path.Combine(entryPrefix, Path.GetFileName(filePath));
                
                archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
            }

            foreach (var dirPath in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dirPath);
                var newPrefix = string.IsNullOrEmpty(entryPrefix) 
                    ? dirName 
                    : Path.Combine(entryPrefix, dirName);
                
                AddDirectoryToArchive(archive, dirPath, newPrefix);
            }
        }
    }
}
