using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AetherShell.Client
{
    public static class ProcessKiller
    {
        // Список процессов, которые нужно убивать при окончании времени
        // Имена процессов указываются БЕЗ .exe
        private static readonly List<string> _gamesAndApps = new List<string>
        {
            // --- ИГРЫ ---
            "cs2",              // Counter-Strike 2
            "dota2",            // Dota 2
            "valorant",         // Valorant (сам клиент)
            "Valorant-Win64-Shipping",
            "pubg",             // PUBG
            "TslGame",          // PUBG (процесс движка)
            "gta5",             // GTA V
            "fifa23",           // FIFA
            "r5apex",           // Apex Legends
            "Minecraft",        // Minecraft
            "javaw",            // Minecraft (Java)
            "FortniteClient-Win64-Shipping", // Fortnite

            // --- БРАУЗЕРЫ (чтобы не сидели в YouTube) ---
            "chrome",
            "opera",
            "firefox",
            "msedge",
            "yandex",

            // --- LAUNCHERS (опционально, если нужно закрывать и их) ---
             "steam",
             "EpicGamesLauncher"
        };

        public static void KillGames()
        {
            try
            {
                // Получаем все запущенные процессы
                Process[] runningProcesses = Process.GetProcesses();

                foreach (Process p in runningProcesses)
                {
                    try
                    {
                        // Проверяем, есть ли процесс в нашем черном списке (регистронезависимо)
                        if (_gamesAndApps.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                        {
                            p.Kill(); // Принудительно завершаем
                            Debug.WriteLine($"Убит процесс: {p.ProcessName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Некоторые процессы (античиты) могут не давать себя убить без прав SYSTEM
                        Debug.WriteLine($"Не удалось убить {p.ProcessName}: {ex.Message}");
                    }
                }
            }
            catch (Exception globalEx)
            {
                Debug.WriteLine($"Ошибка в ProcessKiller: {globalEx.Message}");
            }
        }
    }
}