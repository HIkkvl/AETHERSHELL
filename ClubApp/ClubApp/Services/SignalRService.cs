using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AetherShell.Client;

namespace AetherShell.Client.Services
{
    public class SignalRService : IDisposable
    {
        private HubConnection _hubConnection;
        private readonly string _pcId;
        private string _authToken;

        public SignalRService(string pcId)
        {
            _pcId = pcId;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token;
        }

        public event Action<DateTime> OnUnlock;
        public event Action OnLock;
        public event Action<string, string> OnChatMessage;
        public event Action<decimal> OnPaymentSuccess;
        public event Action<decimal> OnBalanceUpdated;  // Обновление баланса (например при возврате)
        public event Action OnShutdown;
        public event Action OnReboot;
        public event Action OnBannersUpdated;
        public event Action OnAppsUpdated;
        public event Action OnProductsUpdated;
        public event Action OnTariffsUpdated;
        public event Action OnLoyaltyUpdated;
        public event Func<string, Task> OnReconnected;
        public event Action<int, string> OnOrderStatusUpdated; // orderId, status
        
        // Новые события для подтверждения ПК и offline-режима
        public event Action OnPendingApproval;      // ПК ожидает подтверждения
        public event Action OnApproved;             // ПК подтверждён
        public event Action OnConnected;            // Успешное подключение
        public event Action OnDisconnected;         // Полная потеря связи (Closed)
        public event Action OnReconnecting;         // Попытка переподключения (временно offline)
        
        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
        private bool _wasConnected = false;         // Было ли успешное подключение
        private bool _isStopping = false;           // Флаг штатной остановки

        public async Task InitializeAsync()
        {
            string connectionUrl = $"{AppConstants.SERVER_URL}{AppConstants.HUB_NAME}?pc_id={Uri.EscapeDataString(_pcId)}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(connectionUrl, options =>
                {
                    // Ключ клуба и токен идут заголовками: в query-строке они
                    // оседали бы в логах прокси и в истории браузера.
                    if (!string.IsNullOrEmpty(AppConstants.CLUB_KEY))
                        options.Headers[AppConstants.CLUB_KEY_HEADER] = AppConstants.CLUB_KEY;

                    if (!string.IsNullOrEmpty(_authToken))
                        options.Headers["Authorization"] = "Bearer " + _authToken;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers();

            _hubConnection.Reconnected += async (connectionId) =>
            {
                _wasConnected = true;
                if (OnReconnected != null)
                {
                    await OnReconnected(connectionId);
                }
            };

            _hubConnection.Reconnecting += (exception) =>
            {
                // Показываем offline только если соединение было установлено ранее
                if (_wasConnected)
                {
                    OnReconnecting?.Invoke();
                }
                return Task.CompletedTask;
            };

            _hubConnection.Closed += (exception) =>
            {
                // Вызываем только если было успешное подключение и это НЕ штатная остановка
                if (_wasConnected && !_isStopping)
                {
                    OnDisconnected?.Invoke();
                }
                return Task.CompletedTask;
            };

            try
            {
                await _hubConnection.StartAsync();
                
                // Успешное первое подключение
                _wasConnected = true;
                OnConnected?.Invoke();
                Console.WriteLine("[SignalR] Подключение установлено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Ошибка подключения: {ex.Message}");
                // Не вызываем OnDisconnected - это первое подключение, оно просто не удалось
                throw; // Пробрасываем для обработки выше
            }
        }

        private void RegisterHandlers()
        {
            _hubConnection.On<DateTime>("ReceiveUnlock", (endTimeUtc) =>
            {
                OnUnlock?.Invoke(endTimeUtc);
            });

            _hubConnection.On("ReceiveLock", () =>
            {
                OnLock?.Invoke();
            });

            _hubConnection.On<string, string>("ReceiveChatMessage", (sender, message) =>
            {
                OnChatMessage?.Invoke(sender, message);
            });

            _hubConnection.On<decimal>("PaymentSuccess", (newBalance) =>
            {
                OnPaymentSuccess?.Invoke(newBalance);
            });

            _hubConnection.On<decimal>("BalanceUpdated", (newBalance) =>
            {
                OnBalanceUpdated?.Invoke(newBalance);
            });

            _hubConnection.On("ReceiveShutdown", () =>
            {
                OnShutdown?.Invoke();
            });

            _hubConnection.On("ReceiveReboot", () =>
            {
                OnReboot?.Invoke();
            });

            _hubConnection.On("BannersUpdated", () =>
            {
                OnBannersUpdated?.Invoke();
            });

            _hubConnection.On("AppsUpdated", () => OnAppsUpdated?.Invoke());
            _hubConnection.On("ProductsUpdated", () => OnProductsUpdated?.Invoke());
            _hubConnection.On("TariffsUpdated", () => OnTariffsUpdated?.Invoke());
            _hubConnection.On("LoyaltyUpdated", () => OnLoyaltyUpdated?.Invoke());

            _hubConnection.On<int, string>("OrderStatusUpdated", (orderId, status) =>
            {
                OnOrderStatusUpdated?.Invoke(orderId, status);
            });

            // Обработчики для подтверждения ПК
            _hubConnection.On("PendingApproval", () =>
            {
                OnPendingApproval?.Invoke();
            });

            _hubConnection.On("Approved", () =>
            {
                OnApproved?.Invoke();
            });
        }

        /// <summary>Сообщает, что сейчас в фокусе на этом ПК. null означает «ничего, кроме шелла».</summary>


        /// <summary>Сообщает, что сейчас в фокусе на этом ПК. null означает «ничего, кроме шелла».</summary>
        public async Task SendCurrentAppAsync(string processName, string windowTitle)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("UpdateCurrentApp", processName, windowTitle);
            }
        }

        public async Task SendToAdminAsync(string message)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("SendToAdmin", message);
            }
        }

        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {
                _isStopping = true;  // Помечаем что это штатная остановка
                await _hubConnection.StopAsync();
            }
        }

        // Отправка системной информации на сервер
        public async Task SendSystemInfoAsync(string ipAddress, string cpuName, int ramTotalMb, int ramUsedMb,
                                               string gpuName, string diskInfo, string osVersion, string macAddress)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try
                {
                    await _hubConnection.InvokeAsync("UpdateSystemInfo", 
                        ipAddress, cpuName, ramTotalMb, ramUsedMb, gpuName, diskInfo, osVersion, macAddress);
                    Console.WriteLine("[SignalR] Системная информация отправлена");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignalR] Ошибка отправки системной информации: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _hubConnection?.DisposeAsync();
        }
    }
}
