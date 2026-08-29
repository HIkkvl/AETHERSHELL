using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AetherShell.Client.Services
{
    /// <summary>Локальные правки картинки/пути приложений (только на этом ПК).</summary>
    public static class LocalAppOverrides
    {
        public class OverrideEntry
        {
            public string ImageUrl { get; set; }
            public string ExePath { get; set; }
        }

        private static Dictionary<string, OverrideEntry> _cache;
        private static readonly object _lock = new object();

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AetherShell");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "app-overrides.json");
            }
        }

        public static void ApplyTo(IEnumerable<Models.AppItem> apps)
        {
            if (apps == null) return;
            var map = Load();
            foreach (var app in apps)
            {
                if (app == null) continue;
                if (!map.TryGetValue(app.Id.ToString(), out var entry) || entry == null) continue;
                if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
                    app.ImageUrl = entry.ImageUrl;
                if (!string.IsNullOrWhiteSpace(entry.ExePath))
                    app.ExePath = entry.ExePath;
            }
        }

        public static void SetImage(int appId, string imageUrl)
        {
            var map = Load();
            string key = appId.ToString();
            if (!map.TryGetValue(key, out var entry) || entry == null)
                entry = new OverrideEntry();
            entry.ImageUrl = imageUrl;
            map[key] = entry;
            Save(map);
        }

        public static void SetExePath(int appId, string exePath)
        {
            var map = Load();
            string key = appId.ToString();
            if (!map.TryGetValue(key, out var entry) || entry == null)
                entry = new OverrideEntry();
            entry.ExePath = exePath;
            map[key] = entry;
            Save(map);
        }

        private static Dictionary<string, OverrideEntry> Load()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                _cache = new Dictionary<string, OverrideEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (!File.Exists(FilePath)) return _cache;
                    string json = File.ReadAllText(FilePath);
                    if (string.IsNullOrWhiteSpace(json)) return _cache;
                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, OverrideEntry>>(json);
                    if (parsed != null) _cache = parsed;
                }
                catch { }
                return _cache;
            }
        }

        private static void Save(Dictionary<string, OverrideEntry> map)
        {
            lock (_lock)
            {
                _cache = map ?? new Dictionary<string, OverrideEntry>();
                try
                {
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(_cache, Formatting.Indented));
                }
                catch { }
            }
        }
    }
}
