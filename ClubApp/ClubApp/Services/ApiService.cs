using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AetherShell.Client.Models;

namespace AetherShell.Client.Services
{

public class ApiService : IDisposable
{
	public class ClientMeDto
	{
		public int id { get; set; }

		public string username { get; set; }

		public string email { get; set; }

		public decimal balance { get; set; }

		public string avatarUrl { get; set; }

		public decimal totalSpent { get; set; }
	}

	private readonly HttpClient _httpClient;

	public ApiService()
	{
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(AppConstants.SERVER_URL)
		};
		if (!string.IsNullOrEmpty(AppConstants.CLUB_KEY))
		{
			_httpClient.DefaultRequestHeaders.Add("X-Club-Key", AppConstants.CLUB_KEY);
		}
	}

	public void SetAuthToken(string token)
	{
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	}

	public async Task<List<AppItem>> GetAppsAsync()
	{
		return await _httpClient.GetFromJsonAsync<List<AppItem>>("/api/Apps");
	}

	public async Task<List<TariffItem>> GetTariffsAsync()
	{
		return await _httpClient.GetFromJsonAsync<List<TariffItem>>("/api/Tariffs");
	}

	public async Task<List<ProductItem>> GetProductsAsync()
	{
		return await _httpClient.GetFromJsonAsync<List<ProductItem>>("/api/Products");
	}

	public async Task<LoginResponse> LoginAsync(string username, string password, string macAddress)
	{
		var value = new
		{
			Username = username,
			Password = password,
			MacAddress = macAddress
		};
		HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/Auth/Login", value);
		if (!response.IsSuccessStatusCode)
		{
			string text = await response.Content.ReadAsStringAsync();
			if (response.StatusCode == (HttpStatusCode)429)
			{
				throw new Exception("Слишком много попыток. Подождите минуту.");
			}
			if (response.StatusCode == HttpStatusCode.Unauthorized)
			{
				throw new Exception("Неверный логин или пароль");
			}
			throw new Exception(string.IsNullOrEmpty(text) ? $"Ошибка сервера ({(int)response.StatusCode})" : text);
		}
		return await response.Content.ReadFromJsonAsync<LoginResponse>();
	}

	public async Task RegisterAsync(string username, string email, string password)
	{
		var value = new
		{
			Username = username,
			Email = email,
			Password = password
		};
		HttpResponseMessage httpResponseMessage = await _httpClient.PostAsJsonAsync("/api/Auth/Register", value);
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			string text = await httpResponseMessage.Content.ReadAsStringAsync();
			throw new Exception(string.IsNullOrEmpty(text) ? "Ошибка регистрации" : text);
		}
	}

	public async Task ForgotPasswordAsync(string email)
	{
		HttpResponseMessage httpResponseMessage = await _httpClient.PostAsJsonAsync("api/Auth/forgot-password", new
		{
			Email = email
		});
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			throw new Exception(await httpResponseMessage.Content.ReadAsStringAsync());
		}
	}

	public async Task ResetPasswordAsync(string email, string code, string newPassword)
	{
		var value = new
		{
			Email = email,
			Code = code,
			NewPassword = newPassword
		};
		HttpResponseMessage httpResponseMessage = await _httpClient.PostAsJsonAsync("api/Auth/reset-password", value);
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			throw new Exception(await httpResponseMessage.Content.ReadAsStringAsync());
		}
	}

	public async Task<SessionStatusDto> GetSessionStatusAsync(string macAddress)
	{
		HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync("/api/Auth/status?mac=" + macAddress);
		if (httpResponseMessage.IsSuccessStatusCode)
		{
			return await httpResponseMessage.Content.ReadFromJsonAsync<SessionStatusDto>();
		}
		return null;
	}

	public async Task<OrderResponse> BuyTariffAsync(BuyTariffRequest request)
	{
		HttpResponseMessage obj = await _httpClient.PostAsJsonAsync("/api/Auth/buy", request);
		obj.EnsureSuccessStatusCode();
		return await obj.Content.ReadFromJsonAsync<OrderResponse>();
	}

	public async Task<OrderResponse> CreateOrderAsync(CreateOrderDto orderDto)
	{
		HttpResponseMessage obj = await _httpClient.PostAsJsonAsync("/api/Orders", orderDto);
		obj.EnsureSuccessStatusCode();
		return await obj.Content.ReadFromJsonAsync<OrderResponse>();
	}

	public async Task<PaymentLinkResponse> CreatePaymentLinkAsync(PaymentRequest request)
	{
		HttpResponseMessage obj = await _httpClient.PostAsJsonAsync("/api/payment/create-link", request);
		obj.EnsureSuccessStatusCode();
		return await obj.Content.ReadFromJsonAsync<PaymentLinkResponse>();
	}

	public async Task StopSessionAsync(string pcId)
	{
		await _httpClient.PostAsync("/api/Auth/logout?pcId=" + Uri.EscapeDataString(pcId), null);
	}

	public async Task<List<Banner>> GetBannersAsync()
	{
		_ = 1;
		try
		{
			HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync("/api/Banners?activeOnly=true");
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				return (await httpResponseMessage.Content.ReadFromJsonAsync<List<Banner>>()) ?? new List<Banner>();
			}
			return new List<Banner>();
		}
		catch
		{
			return new List<Banner>();
		}
	}

	public async Task<List<UserOrder>> GetMyOrdersAsync()
	{
		_ = 1;
		try
		{
			HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync("/api/Orders/my");
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				return (await httpResponseMessage.Content.ReadFromJsonAsync<List<UserOrder>>()) ?? new List<UserOrder>();
			}
			return new List<UserOrder>();
		}
		catch
		{
			return new List<UserOrder>();
		}
	}

	public async Task<ClientMeDto> GetMyProfileAsync()
	{
		HttpResponseMessage httpResponseMessage = await _httpClient.GetAsync("api/Auth/me");
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			throw new Exception(await httpResponseMessage.Content.ReadAsStringAsync());
		}
		return await httpResponseMessage.Content.ReadFromJsonAsync<ClientMeDto>();
	}

	public async Task ChangeClientPasswordAsync(string currentPassword, string newPassword)
	{
		HttpResponseMessage httpResponseMessage = await _httpClient.PostAsJsonAsync("api/Auth/client-change-password", new
		{
			CurrentPassword = currentPassword,
			NewPassword = newPassword
		});
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			throw new Exception(await httpResponseMessage.Content.ReadAsStringAsync());
		}
	}

	public async Task<string> UploadAvatarAsync(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		using (var content = new MultipartFormDataContent())
		{
			var fileContent = new ByteArrayContent(bytes);
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			string mime = (ext == ".png") ? "image/png" : ((ext == ".webp") ? "image/webp" : "image/jpeg");
			fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
			content.Add(fileContent, "file", Path.GetFileName(filePath));
			HttpResponseMessage response = await _httpClient.PostAsync("/api/Auth/avatar", content);
			string body = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode)
				throw new Exception(string.IsNullOrWhiteSpace(body) ? ("HTTP " + (int)response.StatusCode) : body);

			string url = null;
			try
			{
				var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
				if (json != null) json.TryGetValue("url", out url);
			}
			catch { }

			if (string.IsNullOrEmpty(url))
			{
				int i = body.IndexOf("\"url\"", StringComparison.OrdinalIgnoreCase);
				if (i >= 0)
				{
					int colon = body.IndexOf(':', i);
					int q1 = body.IndexOf('"', colon + 1);
					int q2 = body.IndexOf('"', q1 + 1);
					if (q1 >= 0 && q2 > q1) url = body.Substring(q1 + 1, q2 - q1 - 1);
				}
			}
			if (string.IsNullOrEmpty(url))
				throw new Exception("Сервер не вернул URL аватара");
			return url;
		}
	}


	public void Dispose()
	{
		_httpClient?.Dispose();
	}
}
}
