using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AetherShell.Client
{
    /// <summary>
    /// Страховка к политикам браузеров: удаляет новые файлы в «Загрузки» / Desktop
    /// и типичные .crdownload/.tmp, если браузер всё же успел что-то сохранить.
    /// </summary>
    public static class DownloadFolderGuard
    {
        private static readonly object Sync = new object();
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();
        private static int _active;

        public static void Start()
        {
            if (Interlocked.Exchange(ref _active, 1) == 1) return;

            lock (Sync)
            {
                StopWatchers_NoLock();

                foreach (var dir in GetWatchFolders())
                {
                    try
                    {
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        var watcher = new FileSystemWatcher(dir)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                            EnableRaisingEvents = true
                        };

                        watcher.Created += OnFileEvent;
                        watcher.Changed += OnFileEvent;
                        watcher.Renamed += OnRenamed;
                        Watchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("DownloadFolderGuard watch " + dir + ": " + ex.Message);
                    }
                }
            }
        }

        public static void Stop()
        {
            Interlocked.Exchange(ref _active, 0);
            lock (Sync)
            {
                StopWatchers_NoLock();
            }
        }

        private static void StopWatchers_NoLock()
        {
            foreach (var w in Watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Created -= OnFileEvent;
                    w.Changed -= OnFileEvent;
                    w.Renamed -= OnRenamed;
                    w.Dispose();
                }
                catch { /* ignore */ }
            }
            Watchers.Clear();
        }

        private static IEnumerable<string> GetWatchFolders()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(userProfile, "Downloads");
            // Локализованное имя «Загрузки» на русской Windows
            string downloadsRu = Path.Combine(userProfile, "Загрузки");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string knownDownloads = GetKnownDownloadsFolder() ?? downloads;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                downloads,
                downloadsRu,
                knownDownloads,
                desktop
            };

            foreach (var path in set)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    yield return path;
            }
        }

        private static string GetKnownDownloadsFolder()
        {
            try
            {
                // FOLDERID_Downloads
                Guid downloadsGuid = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                IntPtr pathPtr;
                int hr = SHGetKnownFolderPath(ref downloadsGuid, 0, IntPtr.Zero, out pathPtr);
                if (hr != 0 || pathPtr == IntPtr.Zero) return null;
                try
                {
                    return System.Runtime.InteropServices.Marshal.PtrToStringUni(pathPtr);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pathPtr);
                }
            }
            catch
            {
                return null;
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        private static void OnRenamed(object sender, RenamedEventArgs e)
        {
            TryDeleteFile(e.FullPath);
        }

        private static void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted) return;
            TryDeleteFile(e.FullPath);
        }

        private static void TryDeleteFile(string path)
        {
            if (_active == 0) return;
            if (string.IsNullOrWhiteSpace(path)) return;

            // Только файлы — папки не трогаем
            try
            {
                if (Directory.Exists(path)) return;
            }
            catch { return; }

            // Несколько попыток: браузер может держать файл открытым
            ThreadPool.QueueUserWorkItem(_ =>
            {
                for (int i = 0; i < 8; i++)
                {
                    if (_active == 0) return;
                    try
                    {
                        if (!File.Exists(path)) return;

                        var attrs = File.GetAttributes(path);
                        if ((attrs & FileAttributes.Directory) != 0) return;

                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        Debug.WriteLine("DownloadFolderGuard removed: " + path);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(250 + i * 150);
                    }
                }
            });
        }
    }
}
