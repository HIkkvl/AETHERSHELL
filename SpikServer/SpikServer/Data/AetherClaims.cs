namespace AetherShell.Server.Data
{
    /// <summary>Имена claim-ов, которыми различаются платформенные и клубные токены.</summary>
    public static class AetherClaims
    {
        /// <summary>"account" — токен владельца/платформы, "club" — токен персонала или посетителя клуба.</summary>
        public const string TokenType = "token_type";

        public const string TokenTypeAccount = "account";
        public const string TokenTypeClub = "club";

        /// <summary>Id платформенного аккаунта (для токенов типа account).</summary>
        public const string AccountId = "account_id";

        /// <summary>Id клуба, к которому жёстко привязан токен (для токенов типа club).</summary>
        public const string ClubId = "club_id";
    }
}
