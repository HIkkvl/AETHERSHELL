using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace AetherShell.Client
{
    public static class SystemUtils
    {
        // Основной путь для политик системы
        private const string SYSTEM_POLICIES = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        // Путь для политик проводника (чтобы убрать Logoff/Выйти)
        private const string EXPLORER_POLICIES = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        /// <summary>
        /// Chromium DownloadRestrictions=3 — блокировать любые загрузки.
        /// https://chromeenterprise.google/policies/#DownloadRestrictions
        /// </summary>
        private static readonly string[] ChromiumPolicyRoots =
        {
            @"Software\Policies\Google\Chrome",
            @"Software\Policies\Google\Chrome\Recommended",
            @"Software\Policies\Microsoft\Edge",
            @"Software\Policies\Microsoft\Edge\Recommended",
            @"Software\Policies\BraveSoftware\Brave",
            @"Software\Policies\YandexBrowser",
            @"Software\Policies\Yandex\YandexBrowser",
            @"Software\Policies\Opera Software\Opera",
            @"Software\Policies\Opera Software\Opera GX",
            @"Software\Policies\Vivaldi",
            @"Software\Policies\Chromium",
        };

        public static void ApplyRestrictions()
        {
            try
            {
                // 1. Убираем Диспетчер задач, Смену пароля и Блокировку экрана
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SYSTEM_POLICIES))
                {
                    if (key != null)
                    {
                        key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);           // Убирает Диспетчер задач
                        key.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);    // Убирает "Сменить пароль"
                        key.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);   // Убирает "Заблокировать"
                    }
                }

                // 2. Убираем кнопку "Выйти" (Sign Out)
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(EXPLORER_POLICIES))
                {
                    if (key != null)
                    {
                        key.SetValue("NoLogoff", 1, RegistryValueKind.DWord); // Убирает "Выйти"
                        key.SetValue("NoClose", 1, RegistryValueKind.DWord);  // Убирает "Завершение работы" из меню Пуск
                    }
                }

                // 3. Запрет скачивания во всех распространённых браузерах
                ApplyBrowserDownloadBlock();
                DownloadFolderGuard.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Ошибка применения ограничений: " + ex.Message);
            }
        }

        public static void RemoveRestrictions()
        {
            try
            {
                DownloadFolderGuard.Stop();

                // Удаляем ограничения из System
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SYSTEM_POLICIES, true))
                {
                    if (key != null)
                    {
                        TryDelete(key, "DisableTaskMgr");
                        TryDelete(key, "DisableChangePassword");
                        TryDelete(key, "DisableLockWorkstation");
                    }
                }

                // Удаляем ограничения из Explorer
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(EXPLORER_POLICIES, true))
                {
                    if (key != null)
                    {
                        TryDelete(key, "NoLogoff");
                        TryDelete(key, "NoClose");
                    }
                }

                RemoveBrowserDownloadBlock();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Ошибка снятия ограничений: " + ex.Message);
            }
        }

        /// <summary>Блокирует загрузки через политики браузеров (HKCU). Уже открытые окна лучше перезапустить.</summary>
        private static void ApplyBrowserDownloadBlock()
        {
            foreach (var root in ChromiumPolicyRoots)
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(root))
                    {
                        if (key == null) continue;
                        // 3 = Block all downloads
                        key.SetValue("DownloadRestrictions", 3, RegistryValueKind.DWord);
                        key.SetValue("PromptForDownloadLocation", 0, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Browser policy " + root + ": " + ex.Message);
                }
            }

            // Firefox: enterprise policies (JSON в реестре Policies)
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Mozilla\Firefox"))
                {
                    if (key != null)
                    {
                        // Запрет сохранения страницы / «Сохранить как» через политики не полный,
                        // но Preferences режет типичные загрузки.
                        key.SetValue("DisableSafeMode", 1, RegistryValueKind.DWord);
                    }
                }

                using (var prefs = Registry.CurrentUser.CreateSubKey(@"Software\Policies\Mozilla\Firefox\Preferences"))
                {
                    if (prefs != null)
                    {
                        // false как строка — формат enterprise Preferences для Firefox
                        prefs.SetValue("browser.download.useDownloadDir", "false", RegistryValueKind.String);
                        prefs.SetValue("browser.download.folderList", "2", RegistryValueKind.String);
                        // Несуществующий путь — загрузка некуда сохранить
                        prefs.SetValue("browser.download.dir", @"C:\AetherShell\NoDownloads", RegistryValueKind.String);
                        prefs.SetValue("browser.download.manager.showWhenStarting", "false", RegistryValueKind.String);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Firefox download policy: " + ex.Message);
            }
        }

        private static void RemoveBrowserDownloadBlock()
        {
            foreach (var root in ChromiumPolicyRoots)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(root, true))
                    {
                        if (key == null) continue;
                        TryDelete(key, "DownloadRestrictions");
                        TryDelete(key, "PromptForDownloadLocation");
                    }
                }
                catch { /* ignore */ }
            }

            try
            {
                using (var prefs = Registry.CurrentUser.OpenSubKey(@"Software\Policies\Mozilla\Firefox\Preferences", true))
                {
                    if (prefs != null)
                    {
                        TryDelete(prefs, "browser.download.useDownloadDir");
                        TryDelete(prefs, "browser.download.folderList");
                        TryDelete(prefs, "browser.download.dir");
                        TryDelete(prefs, "browser.download.manager.showWhenStarting");
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static void TryDelete(RegistryKey key, string name)
        {
            try { key.DeleteValue(name); } catch { }
        }
    }
}
