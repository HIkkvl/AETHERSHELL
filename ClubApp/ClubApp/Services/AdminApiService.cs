using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace AetherShell.Client.Services
{
    /// <summary>Кто вошёл в режим администратора.</summary>
    public class AdminSession
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public string Token { get; set; }
    }

    /// <summary>
    /// Действия сотрудника зала прямо с рабочего ПК.
    ///
    /// Живёт отдельно от <see cref="ApiService"/> с собственным HttpClient: у того
    /// в заголовках токен посетителя, и подмена его админским выкинула бы клиента
    /// из своей сессии.
    /// </summary>
    public class AdminApiService : IDisposable
    {
        private readonly HttpClient _httpClient;

        public AdminApiService()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(AppConstants.SERVER_URL)
            };

            if (!string.IsNullOrEmpty(AppConstants.CLUB_KEY))
            {
                _httpClient.DefaultRequestHeaders.Add(AppConstants.CLUB_KEY_HEADER, AppConstants.CLUB_KEY);
            }
        }

        /// <summary>
        /// Вход сотрудника. MacAddress намеренно не передаётся: иначе сервер
        /// привязал бы ПК к учётке администратора и сбросил сессию посетителя.
        /// </summary>
        public async Task<AdminSession> LoginAsync(string username, string password)
        {
            var response = await _httpClient.PostAsJsonAsync(AppConstants.API_AUTH_LOGIN,
                new { Username = username, Password = password, MacAddress = "" });

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 429)
                    throw new Exception("Слишком много попыток. Подождите минуту.");
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new Exception("Неверный логин или пароль");

                var error = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrEmpty(error) ? $"Ошибка сервера ({(int)response.StatusCode})" : error);
            }

            var result = await response.Content.ReadFromJsonAsync<Models.LoginResponse>();
            var role = result?.role ?? "";

            if (role != "Admin" && role != "Senior" && role != "Super")
                throw new Exception("У этой учётной записи нет прав администратора зала.");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.token);

            return new AdminSession { Username = result.username, Role = role, Token = result.token };
        }

        public Task StartSessionAsync(string pcId, int minutes)
            => PostAsync($"/api/Admin/start?pcId={Uri.EscapeDataString(pcId)}&minutes={minutes}");

        public Task StopSessionAsync(string pcId)
            => PostAsync($"/api/Admin/stop?pcId={Uri.EscapeDataString(pcId)}");

        public Task RebootAsync(string pcId)
            => PostAsync($"/api/Admin/reboot?pcId={Uri.EscapeDataString(pcId)}");

        public Task ShutdownAsync(string pcId)
            => PostAsync($"/api/Admin/shutdown?pcId={Uri.EscapeDataString(pcId)}");

        /// <summary>
        /// Баланс посетителя общий на всю сеть филиалов, поэтому пополнение идёт
        /// через клиентов сети, а не через пользователей зала.
        /// </summary>
        public async Task TopUpAsync(string username, decimal amount)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/Clients/{Uri.EscapeDataString(username)}/topup", amount);
            await EnsureOkAsync(response);
        }

        private async Task PostAsync(string url)
        {
            var response = await _httpClient.PostAsync(url, null);
            await EnsureOkAsync(response);
        }

        private static async Task EnsureOkAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrEmpty(body)
                ? $"Ошибка сервера ({(int)response.StatusCode})"
                : body);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
