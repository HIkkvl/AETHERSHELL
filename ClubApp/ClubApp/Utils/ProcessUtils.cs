using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AetherShell.Client.Utils
{
    /// <summary>Приложение, которое сейчас в фокусе у клиента.</summary>
    public class ForegroundApp
    {
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
    }

    public static class ProcessUtils
    {
        public static Process StartGame(string exePath, Action onExited = null)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new Exception("Путь к игре не задан");

            string raw = exePath.Trim().Trim('"');
            raw = Environment.ExpandEnvironmentVariables(raw);

            // steam://, epic://, URL и т.п.
            if (IsProtocolOrUrl(raw))
                return StartWithShell(raw, null, null, onExited);

            ParseCommandLine(raw, out string file, out string args);
            file = file.Trim().Trim('"');
            file = Environment.ExpandEnvironmentVariables(file);

            string resolved = ResolveExistingPath(file);
            if (resolved == null)
            {
                throw new Exception(
                    "Файл не найден:\n" + file +
                    "\n\nПроверьте ExePath в админке: полный путь к .exe / .lnk или ссылку steam://...");
            }

            string workDir = Path.GetDirectoryName(resolved);
            if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
                workDir = null;

            try
            {
                return StartWithShell(resolved, args, workDir, onExited);
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка запуска игры: " + ex.Message + "\n" + resolved, ex);
            }
        }

        private static Process StartWithShell(string fileName, string arguments, string workingDirectory, Action onExited)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            };
            if (!string.IsNullOrWhiteSpace(arguments))
                processInfo.Arguments = arguments;
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                processInfo.WorkingDirectory = workingDirectory;

            var process = new Process { StartInfo = processInfo };
            process.EnableRaisingEvents = true;
            if (onExited != null)
                process.Exited += (s, e) => onExited();

            process.Start();
            return process;
        }

        private static bool IsProtocolOrUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith("\\\\", StringComparison.Ordinal)) return false; // UNC path
            int scheme = value.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0) return true;
            // steam:4000 / shell:AppsFolder без //
            if (value.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Разбор `"C:\Path\game.exe" -arg` или `C:\Path\game.exe -arg`.</summary>
        private static void ParseCommandLine(string raw, out string file, out string args)
        {
            file = raw;
            args = null;
            if (string.IsNullOrWhiteSpace(raw)) return;

            raw = raw.Trim();
            if (raw.StartsWith("\""))
            {
                int end = raw.IndexOf('"', 1);
                if (end > 1)
                {
                    file = raw.Substring(1, end - 1);
                    args = raw.Substring(end + 1).Trim();
                    if (args.Length == 0) args = null;
                    return;
                }
            }

            // Без кавычек: первый токен — файл, если он существует; иначе вся строка — путь
            // (пути с пробелами без кавычек тоже бывают в админке).
            int space = raw.IndexOf(' ');
            if (space <= 0) return;

            string candidate = raw.Substring(0, space);
            string rest = raw.Substring(space + 1).Trim();
            if (File.Exists(candidate) || File.Exists(Environment.ExpandEnvironmentVariables(candidate)))
            {
                file = candidate;
                args = string.IsNullOrEmpty(rest) ? null : rest;
            }
        }

        private static string ResolveExistingPath(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;

            try
            {
                if (File.Exists(file)) return Path.GetFullPath(file);
            }
            catch { }

            // Иногда в базе путь без расширения
            try
            {
                if (!Path.HasExtension(file))
                {
                    foreach (var ext in new[] { ".exe", ".lnk", ".bat", ".cmd" })
                    {
                        string withExt = file + ext;
                        if (File.Exists(withExt)) return Path.GetFullPath(withExt);
                    }
                }
            }
            catch { }

            // Относительный путь от корня дисков / Program Files — не угадываем;
            // но пробуем CurrentDirectory на случай локальных ярлыков.
            try
            {
                string combined = Path.GetFullPath(file);
                if (File.Exists(combined)) return combined;
            }
            catch { }

            return null;
        }

        public static void Shutdown()
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        public static void Reboot()
        {
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        /// <summary>Диспетчер задач для сотрудника. Требует снятых политик, иначе Windows откажет.</summary>
        public static bool OpenTaskManager() => TryStart("taskmgr.exe");

        public static bool OpenExplorer() => TryStart("explorer.exe");

        private static bool TryStart(string fileName)
        {
            try
            {
                Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Активное окно и его процесс. Нужно для отображения в панели администратора:
        /// «в игре» без названия игры ничего не говорит смене.
        /// Возвращает null, если окно определить не удалось.
        /// </summary>
        public static ForegroundApp GetForegroundApp()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return null;

                GetWindowThreadProcessId(handle, out var processId);
                if (processId == 0) return null;

                using (var process = Process.GetProcessById((int)processId))
                {
                    var title = new StringBuilder(512);
                    GetWindowText(handle, title, title.Capacity);

                    return new ForegroundApp
                    {
                        ProcessName = process.ProcessName,
                        WindowTitle = title.ToString()
                    };
                }
            }
            catch
            {
                // Процесс мог закрыться между вызовами — для трекинга это не ошибка.
                return null;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }
}
