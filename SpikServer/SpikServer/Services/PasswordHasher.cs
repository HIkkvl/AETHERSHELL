namespace AetherShell.Server.Services
{
    /// <summary>
    /// Единая точка хеширования паролей для платформенных аккаунтов и клубных пользователей.
    /// </summary>
    public static class PasswordHasher
    {
        public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

        public static bool Verify(string password, string? hash)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                // Повреждённый или несовместимый хеш — трактуем как неверный пароль.
                return false;
            }
        }

        /// <summary>Пароль, который показывается владельцу клуба один раз при создании аккаунта.</summary>
        public static string GenerateReadablePassword(int length = 14)
        {
            // Без похожих друг на друга символов, чтобы пароль можно было продиктовать.
            const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }

        /// <summary>Ключ клуба для server.config шелла.</summary>
        public static string GenerateEnrollmentKey()
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
            return "ck_" + Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
