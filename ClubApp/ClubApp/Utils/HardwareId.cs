using System;
using System.IO;

namespace AetherShell.Client.Utils
{
    /// <summary>
    /// Постоянный идентификатор ПК. Раньше им был MAC, но он меняется вместе
    /// с сетевой картой и совпадает у ПК из разных клубов, поэтому личность
    /// машины теперь хранится в файле рядом с конфигом.
    /// </summary>
    public static class HardwareId
    {
        private const string FileName = "hardware.id";

        public static string Current { get; } = LoadOrCreate();

        private static string LoadOrCreate()
        {
            var path = ResolvePath();

            try
            {
                if (File.Exists(path))
                {
                    var stored = File.ReadAllText(path).Trim();
                    Guid parsed;
                    if (Guid.TryParse(stored, out parsed))
                        return parsed.ToString("N");
                }

                var generated = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, generated);
                return generated;
            }
            catch
            {
                // Файл недоступен (нет прав, диск только на чтение): ПК подключится
                // как новый, но работать будет. Постоянство важнее, чем падение.
                return Guid.NewGuid().ToString("N");
            }
        }

        private static string ResolvePath()
        {
            var next = Path.Combine(AppContext.BaseDirectory, FileName);
            if (File.Exists(next)) return next;

            var installDir = Path.Combine(@"C:\AetherShell", FileName);
            if (File.Exists(installDir)) return installDir;

            return next;
        }
    }
}
