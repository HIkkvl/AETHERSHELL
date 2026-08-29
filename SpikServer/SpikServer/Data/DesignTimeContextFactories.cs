using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AetherShell.Server.Data
{
    /// <summary>
    /// Контексты для команд `dotnet ef`. Настоящая строка подключения им не нужна:
    /// миграции генерируются по модели, а без этих фабрик инструмент попытался бы
    /// поднять приложение целиком и упал на проверке секретов в Program.cs.
    /// </summary>
    public class PlatformDbContextDesignFactory : IDesignTimeDbContextFactory<PlatformDbContext>
    {
        private const string DesignTimeConnection = "Host=localhost;Database=aethershell;Username=postgres;Password=postgres";

        public PlatformDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(DesignTimeConnection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"))
                .Options;

            return new PlatformDbContext(options);
        }
    }

    public class ClubDbContextDesignFactory : IDesignTimeDbContextFactory<ClubDbContext>
    {
        private const string DesignTimeConnection = "Host=localhost;Database=aether_club_0;Username=postgres;Password=postgres";

        public ClubDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<ClubDbContext>()
                .UseNpgsql(DesignTimeConnection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory"))
                .Options;

            return new ClubDbContext(options);
        }
    }
}
