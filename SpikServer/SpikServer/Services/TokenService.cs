using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using Microsoft.IdentityModel.Tokens;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Единая точка выпуска токенов. Различает два вида:
    ///  - account: владелец клуба или платформенный админ, работает в кабинете и панелях;
    ///  - club: персонал и посетители конкретного клуба, жёстко привязан к своему ClubId.
    /// </summary>
    public class TokenService
    {
        private readonly SigningCredentials _credentials;

        public TokenService(IConfiguration configuration, ServerSettings serverSettings)
        {
            var secret = ResolveSecret(configuration, serverSettings);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        public static string ResolveSecret(IConfiguration configuration, ServerSettings? serverSettings)
        {
            var envKey = Environment.GetEnvironmentVariable("SPIK_JWT_SECRET");
            if (!string.IsNullOrEmpty(envKey)) return envKey;
            if (!string.IsNullOrEmpty(serverSettings?.JwtSecretKey)) return serverSettings.JwtSecretKey;
            var configKey = configuration["Jwt:SecretKey"];
            if (!string.IsNullOrEmpty(configKey)) return configKey;
            throw new InvalidOperationException("JWT secret not configured. Задайте SPIK_JWT_SECRET.");
        }

        /// <summary>
        /// Токен сотрудника зала. Клуб передаётся отдельно: сама запись User лежит
        /// в базе своего клуба и номера клуба уже не хранит.
        /// </summary>
        public string IssueClubToken(User user, int clubId, TimeSpan? lifetime = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, string.IsNullOrEmpty(user.Role) ? "Admin" : user.Role),
                new(AetherClaims.TokenType, AetherClaims.TokenTypeClub),
                new(AetherClaims.ClubId, clubId.ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString())
            };

            return Write(claims, lifetime ?? TimeSpan.FromDays(1));
        }

        /// <summary>
        /// Токен посетителя. Сам клиент принадлежит сети, но токен привязан к клубу,
        /// в котором он вошёл: сессии, заказы и логи остаются данными конкретного зала.
        /// </summary>
        public string IssueClientToken(Client client, int clubId, TimeSpan? lifetime = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, client.Username),
                new(ClaimTypes.Role, "User"),
                new(AetherClaims.TokenType, AetherClaims.TokenTypeClub),
                new(AetherClaims.ClubId, clubId.ToString()),
                new(ClaimTypes.NameIdentifier, client.Id.ToString())
            };

            return Write(claims, lifetime ?? TimeSpan.FromDays(1));
        }

        /// <summary>Токен платформенного аккаунта: владелец клуба или админ платформы.</summary>
        public string IssueAccountToken(Account account, TimeSpan? lifetime = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, account.Email),
                new(ClaimTypes.Role, account.Role),
                new(AetherClaims.TokenType, AetherClaims.TokenTypeAccount),
                new(AetherClaims.AccountId, account.Id.ToString()),
                new(ClaimTypes.NameIdentifier, account.Id.ToString())
            };

            return Write(claims, lifetime ?? TimeSpan.FromDays(7));
        }

        private string Write(IEnumerable<Claim> claims, TimeSpan lifetime)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.Add(lifetime),
                SigningCredentials = _credentials
            });
            return handler.WriteToken(token);
        }
    }
}
