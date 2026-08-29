using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AetherShell.Server.Data
{
    /// <summary>
    /// Строит строки подключения к базам отдельных клубов на основе базовой строки
    /// платформенной базы: меняется только имя базы данных, хост и логин те же.
    /// </summary>
    public interface IClubDbConnectionFactory
    {
        /// <summary>Имя базы данных клуба.</summary>
        string DatabaseName(int clubId);

        /// <summary>Строка подключения к базе конкретного клуба.</summary>
        string ConnectionStringFor(int clubId);

        /// <summary>
        /// Строка подключения для платформенных запросов, где клуб не определён.
        /// Соединение никогда не открывается: клубные контроллеры закрыты
        /// <see cref="AetherShell.Server.Filters.RequireClubAttribute"/>, который
        /// отклоняет запрос раньше первого обращения к базе.
        /// </summary>
        string PlatformConnectionString { get; }
    }

    public class ClubDbConnectionFactory : IClubDbConnectionFactory
    {
        private const string DatabasePrefix = "aether_club_";

        private readonly string _baseConnectionString;

        public ClubDbConnectionFactory(string baseConnectionString)
        {
            _baseConnectionString = baseConnectionString;
        }

        public string PlatformConnectionString => _baseConnectionString;

        public string DatabaseName(int clubId) => DatabasePrefix + clubId;

        public string ConnectionStringFor(int clubId)
        {
            var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
            {
                Database = DatabaseName(clubId)
            };
            return builder.ConnectionString;
        }
    }

    /// <summary>
    /// Контекст базы произвольного клуба вне HTTP-запроса: нужен фоновым сервисам,
    /// SignalR-хабу и провижинингу, где клуб задаётся явно, а не заголовком.
    /// </summary>
    public interface IClubDbContextFactory
    {
        ClubDbContext Create(int clubId);
    }

    public class ClubDbContextFactory : IClubDbContextFactory
    {
        private readonly IClubDbConnectionFactory _connections;

        public ClubDbContextFactory(IClubDbConnectionFactory connections)
        {
            _connections = connections;
        }

        public ClubDbContext Create(int clubId)
        {
            var options = new DbContextOptionsBuilder<ClubDbContext>()
                .UseNpgsql(_connections.ConnectionStringFor(clubId))
                .Options;

            return new ClubDbContext(options);
        }
    }
}
