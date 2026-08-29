namespace AetherShell.Server.Data
{
    /// <summary>
    /// Клуб, в контексте которого выполняется текущий запрос. Заполняется
    /// <see cref="AetherShell.Server.Middleware.ClubScopeMiddleware"/> из токена и заголовка X-Club-Id,
    /// а <see cref="ClubDbContext"/> по нему выбирает базу данных клуба.
    /// </summary>
    public interface ICurrentClub
    {
        /// <summary>null означает «без скоупа»: платформенные запросы и фоновые сервисы.</summary>
        int? ClubId { get; }

        /// <summary>
        /// Сеть, к которой относится клуб текущего запроса, — это владелец клуба.
        /// По ней выбираются посетители: баланс у них общий на все филиалы сети.
        /// </summary>
        int? NetworkId { get; }

        bool IsPlatformAdmin { get; }

        /// <summary>Id платформенного аккаунта, если запрос пришёл с account-токеном.</summary>
        int? AccountId { get; }

        void Set(int? clubId, bool isPlatformAdmin, int? accountId, int? networkId = null);
    }

    public class CurrentClub : ICurrentClub
    {
        public int? ClubId { get; private set; }
        public int? NetworkId { get; private set; }
        public bool IsPlatformAdmin { get; private set; }
        public int? AccountId { get; private set; }

        public void Set(int? clubId, bool isPlatformAdmin, int? accountId, int? networkId = null)
        {
            ClubId = clubId;
            IsPlatformAdmin = isPlatformAdmin;
            AccountId = accountId;
            NetworkId = networkId;
        }
    }
}
