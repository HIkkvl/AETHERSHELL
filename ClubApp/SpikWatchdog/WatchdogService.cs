using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AetherShell.Watchdog;

public class WatchdogService : BackgroundService
{
    private readonly ILogger<WatchdogService> _logger;
    private readonly string _clientPath;
    private readonly string _clientProcessName = "AetherShell.Client";
    private Process? _clientProcess;
    private int _restartCount = 0;
    private DateTime _lastRestartTime = DateTime.MinValue;

    #region Native API для запуска в сессии пользователя

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(IntPtr hServer, int Reserved, int Version,
        ref IntPtr ppSessionInfo, ref int pCount);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int ImpersonationLevel,
        int TokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    const uint MAXIMUM_ALLOWED = 0x2000000;
    const int SecurityImpersonation = 2;
    const int TokenPrimary = 1;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint CREATE_NEW_CONSOLE = 0x00000010;
    const int STARTF_USESHOWWINDOW = 0x00000001;
    const short SW_SHOW = 5;

    #endregion

    public WatchdogService(ILogger<WatchdogService> logger)
    {
        _logger = logger;
        
        // Путь к клиенту (рядом с watchdog или в конфиге)
        var baseDir = AppContext.BaseDirectory;
        _clientPath = Path.Combine(baseDir, "AetherShell.Client.exe");
        
        // Альтернативные пути
        if (!File.Exists(_clientPath))
        {
            _clientPath = Path.Combine(baseDir, "..", "ClubApp", "ClubApp", "bin", "Release", "AetherShell.Client.exe");
        }
        if (!File.Exists(_clientPath))
        {
            _clientPath = Path.Combine(baseDir, "..", "ClubApp", "ClubApp", "bin", "Debug", "AetherShell.Client.exe");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Watchdog] Сервис запущен. Путь клиента: {Path}", _clientPath);

        if (!File.Exists(_clientPath))
        {
            _logger.LogError("[Watchdog] Клиент не найден: {Path}", _clientPath);
            // Продолжаем работу, может появится позже
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorClientAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Watchdog] Ошибка мониторинга");
            }

            await Task.Delay(3000, stoppingToken); // Проверка каждые 3 секунды
        }
    }

    private async Task MonitorClientAsync(CancellationToken stoppingToken)
    {
        // Проверяем есть ли активная сессия пользователя
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            _logger.LogDebug("[Watchdog] Нет активной сессии пользователя");
            return;
        }

        // Ищем процесс клиента
        var clientProcesses = Process.GetProcessesByName(_clientProcessName);
        
        if (clientProcesses.Length == 0)
        {
            _logger.LogWarning("[Watchdog] Клиент не запущен, запускаем...");
            await StartClientAsync();
        }
        else
        {
            // Проверяем что процесс отвечает
            foreach (var proc in clientProcesses)
            {
                try
                {
                    if (proc.HasExited)
                    {
                        _logger.LogWarning("[Watchdog] Клиент завершился (код: {Code}), перезапускаем...", 
                            proc.ExitCode);
                        await StartClientAsync();
                    }
                    else if (!proc.Responding)
                    {
                        _logger.LogWarning("[Watchdog] Клиент не отвечает, принудительный перезапуск...");
                        proc.Kill();
                        await Task.Delay(1000, stoppingToken);
                        await StartClientAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Watchdog] Ошибка проверки процесса");
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
    }

    private async Task StartClientAsync()
    {
        if (!File.Exists(_clientPath))
        {
            _logger.LogError("[Watchdog] Файл клиента не найден: {Path}", _clientPath);
            return;
        }

        // Защита от слишком частых перезапусков
        var timeSinceLastRestart = DateTime.Now - _lastRestartTime;
        if (timeSinceLastRestart.TotalSeconds < 10)
        {
            _restartCount++;
            if (_restartCount > 5)
            {
                _logger.LogError("[Watchdog] Слишком много перезапусков за короткое время. Пауза 60 сек.");
                await Task.Delay(60000);
                _restartCount = 0;
            }
        }
        else
        {
            _restartCount = 0;
        }

        try
        {
            var pid = StartProcessInUserSession(_clientPath);
            _lastRestartTime = DateTime.Now;

            if (pid > 0)
            {
                _logger.LogInformation("[Watchdog] Клиент запущен в сессии пользователя (PID: {Pid})", pid);
            }
            else
            {
                _logger.LogWarning("[Watchdog] Не удалось запустить клиент в сессии пользователя, пробуем обычный запуск...");
                // Fallback: обычный запуск (работает если служба запущена от имени пользователя)
                var startInfo = new ProcessStartInfo
                {
                    FileName = _clientPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_clientPath)
                };
                _clientProcess = Process.Start(startInfo);
                _logger.LogInformation("[Watchdog] Клиент запущен обычным способом (PID: {Pid})", _clientProcess?.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] Ошибка запуска клиента");
        }
    }

    /// <summary>
    /// Запускает процесс в интерактивной сессии пользователя (из службы Windows)
    /// </summary>
    private int StartProcessInUserSession(string applicationPath)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr duplicatedToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            // Получаем ID активной консольной сессии
            var sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == 0xFFFFFFFF)
            {
                _logger.LogWarning("[Watchdog] Нет активной консольной сессии");
                return -1;
            }

            _logger.LogDebug("[Watchdog] Активная сессия: {SessionId}", sessionId);

            // Получаем токен пользователя для этой сессии
            if (!WTSQueryUserToken(sessionId, out userToken))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("[Watchdog] WTSQueryUserToken failed: {Error}", error);
                return -1;
            }

            // Дублируем токен для CreateProcessAsUser
            if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                SecurityImpersonation, TokenPrimary, out duplicatedToken))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("[Watchdog] DuplicateTokenEx failed: {Error}", error);
                return -1;
            }

            // Создаём environment block для пользователя
            if (!CreateEnvironmentBlock(out environment, duplicatedToken, false))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogWarning("[Watchdog] CreateEnvironmentBlock failed: {Error}", error);
                // Продолжаем без environment block
            }

            // Настраиваем STARTUPINFO
            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default", // Интерактивный рабочий стол
                dwFlags = STARTF_USESHOWWINDOW,
                wShowWindow = SW_SHOW
            };

            var creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE;
            var workingDirectory = Path.GetDirectoryName(applicationPath);

            // Запускаем процесс
            if (!CreateProcessAsUser(
                duplicatedToken,
                applicationPath,
                null,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                environment,
                workingDirectory,
                ref startupInfo,
                out var processInfo))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("[Watchdog] CreateProcessAsUser failed: {Error}", error);
                return -1;
            }

            // Закрываем хэндлы процесса (мы следим за ним по имени)
            CloseHandle(processInfo.hProcess);
            CloseHandle(processInfo.hThread);

            return processInfo.dwProcessId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Watchdog] Ошибка запуска в сессии пользователя");
            return -1;
        }
        finally
        {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);
            if (duplicatedToken != IntPtr.Zero)
                CloseHandle(duplicatedToken);
            if (userToken != IntPtr.Zero)
                CloseHandle(userToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Watchdog] Остановка сервиса...");
        await base.StopAsync(cancellationToken);
    }
}
