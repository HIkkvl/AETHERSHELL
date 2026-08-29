namespace AetherShell.Server.Constants
{
    public static class SignalRMethods
    {
        public const string ReceiveUnlock = "ReceiveUnlock";
        public const string ReceiveLock = "ReceiveLock";
        public const string ComputerUpdated = "ComputerUpdated";
        public const string BannersUpdated = "BannersUpdated";

        /// <summary>Каталог игр/ПО изменился — шелл и панель перезагружают список.</summary>
        public const string AppsUpdated = "AppsUpdated";

        /// <summary>Меню бара/кухни изменилось.</summary>
        public const string ProductsUpdated = "ProductsUpdated";

        /// <summary>Тарифы изменились.</summary>
        public const string TariffsUpdated = "TariffsUpdated";

        /// <summary>Клиенты сети: создание, удаление, баланс.</summary>
        public const string ClientsUpdated = "ClientsUpdated";

        /// <summary>Список/карточки ПК (имя, группа, карта).</summary>
        public const string ComputersUpdated = "ComputersUpdated";

        /// <summary>Настройки лояльности клуба.</summary>
        public const string LoyaltyUpdated = "LoyaltyUpdated";
    }
}
