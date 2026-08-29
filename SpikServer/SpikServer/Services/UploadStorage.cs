namespace AetherShell.Server.Services
{
    /// <summary>
    /// Где лежат загруженные картинки. В докере это том <c>server-data:/data</c>,
    /// при локальном запуске — папка рядом с бинарником, чтобы не требовать root.
    /// </summary>
    public class UploadStorage
    {
        public string RootPath { get; }

        /// <summary>Префикс, по которому эти же файлы отдаются наружу.</summary>
        public const string PublicPrefix = "/uploads";

        public UploadStorage(IConfiguration configuration)
        {
            var configured = configuration["SPIK_DATA_DIR"];

            var dataDir = !string.IsNullOrWhiteSpace(configured)
                ? configured!
                : Directory.Exists("/data")
                    ? "/data"
                    : Path.Combine(AppContext.BaseDirectory, "data");

            RootPath = Path.Combine(dataDir, "uploads");
            Directory.CreateDirectory(RootPath);
        }

        public string ClubPath(int clubId)
        {
            var path = Path.Combine(RootPath, $"club-{clubId}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
