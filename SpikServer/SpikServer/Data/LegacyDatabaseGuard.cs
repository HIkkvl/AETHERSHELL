using Npgsql;

namespace AetherShell.Server.Data
{
    /// <summary>
    /// До перехода на базу-на-клуб все таблицы жили в одной базе. Такая база не
    /// мигрируется на новую схему: первая же миграция спотыкается на уже
    /// существующем <c>Accounts</c>, и сервер падает с непонятной ошибкой EF.
    ///
    /// Здесь это распознаётся до миграций: либо понятное объяснение с выходом,
    /// либо, по явному разрешению, полный сброс.
    /// </summary>
    public static class LegacyDatabaseGuard
    {
        /// <summary>
        /// Признак старой схемы: <c>Computers</c> в платформенной базе. В новой
        /// архитектуре эта таблица бывает только в базе клуба.
        /// </summary>
        private const string LegacyMarkerTable = "Computers";

        public static void EnsureNotLegacy(string connectionString)
        {
            using var connection = new NpgsqlConnection(connectionString);

            try
            {
                connection.Open();
            }
            catch (NpgsqlException)
            {
                // Базы ещё нет — EnsureCreated/Migrate создадут её сами.
                return;
            }

            if (!TableExists(connection, LegacyMarkerTable)) return;

            if (!ResetAllowed())
            {
                Console.WriteLine("[System] ОШИБКА: база данных осталась от старой схемы (все клубы в одной базе).");
                Console.WriteLine("[System] Теперь у каждого клуба своя база, поэтому старая не обновляется — её нужно пересоздать.");
                Console.WriteLine("[System] Docker: docker compose down -v && docker compose up -d");
                Console.WriteLine("[System] Локально: удалите базу вручную или запустите сервер с SPIK_RESET_LEGACY_DB=true.");
                Console.WriteLine("[System] Клубы и компьютеры придётся подключить заново: данные не переносятся.");
                Environment.Exit(1);
            }

            Console.WriteLine("[System] SPIK_RESET_LEGACY_DB=true: сбрасываю старую базу.");

            DropClubDatabases(connection);

            using var reset = new NpgsqlCommand("DROP SCHEMA public CASCADE; CREATE SCHEMA public;", connection);
            reset.ExecuteNonQuery();

            Console.WriteLine("[System] Старая схема удалена, миграции создадут базу заново.");
        }

        private static bool ResetAllowed()
        {
            var value = Environment.GetEnvironmentVariable("SPIK_RESET_LEGACY_DB")?.Trim();
            return value is "1" or "true" or "True" or "yes";
        }

        private static bool TableExists(NpgsqlConnection connection, string table)
        {
            using var command = new NpgsqlCommand(
                "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name",
                connection);
            command.Parameters.AddWithValue("name", table);
            return command.ExecuteScalar() != null;
        }

        /// <summary>
        /// Базы клубов от прерванного запуска. Нумерация клубов начнётся заново,
        /// поэтому осиротевшая aether_club_1 иначе досталась бы новому клубу.
        /// </summary>
        private static void DropClubDatabases(NpgsqlConnection connection)
        {
            var names = new List<string>();

            using (var list = new NpgsqlCommand(
                "SELECT datname FROM pg_database WHERE datname LIKE 'aether\\_club\\_%'", connection))
            using (var reader = list.ExecuteReader())
            {
                while (reader.Read()) names.Add(reader.GetString(0));
            }

            foreach (var name in names)
            {
                // Имя пришло из pg_database, а параметры в DROP DATABASE не поддерживаются.
                using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
                drop.ExecuteNonQuery();
                Console.WriteLine($"[System] Удалена осиротевшая база клуба: {name}");
            }
        }
    }
}
