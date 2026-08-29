using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AetherShell.Client.Models;
using AetherShell.Client.Pages;
using AetherShell.Client.Properties;
using AetherShell.Client.Services;
using AetherShell.Client.Utils;
using AetherShell.Client.Windows;
using Microsoft.Win32;

using System.ComponentModel;
using System.Diagnostics;
namespace AetherShell.Client
{

public partial class MainWindow : Window
{
	private string _cachedPcName;

	private string _authToken;

	public readonly ApiService ApiService;

	private SignalRService _signalRService;

	private bool _canClose;

	private bool _isSessionActive;

	private DispatcherTimer _uiTimer;

	private DispatcherTimer _authMessageClearTimer;

	private DateTime _sessionEndTime;

	private readonly Brush _warningColor = Brushes.Red;

	private readonly Brush _normalColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5ED7"));

	private bool _enableShop = true;

	private DispatcherTimer _idleTimer;

	private DateTime _lastInputTime;

	private const int IDLE_TIMEOUT_SECONDS = 60;

	private DispatcherTimer _bannerTimer;

	private List<Banner> _activeBanners = new List<Banner>();

	private int _currentBannerIndex;

	private bool _warning20MinPlayed;

	private bool _warning10MinPlayed;

	private bool _warning5MinPlayed;

	private bool _isOffline;

	private bool _isPendingApproval;

	private DateTime _lastServerSync;

	private Action _pendingMsgAction;

	private Action<string> _pendingInputCallback;

	private readonly AdminApiService _adminApi = new AdminApiService();

	private AdminSession _adminSession;

	private bool _adminPanelOpen;

	private DispatcherTimer _appWatchTimer;

	private string _lastReportedApp;

	private TextBlock _authMessageClearTarget;







































































	public static MainWindow Instance { get; private set; }

	public decimal CurrentUserTotalSpent { get; set; }

	public int? CurrentUserDiscountPercent { get; set; }

	public string MyPcId { get; private set; }

	public string CurrentUsername { get; private set; }

	public string CurrentAvatarUrl { get; private set; }

	public List<CartItem> Cart { get; private set; } = new List<CartItem>();

	public ObservableCollection<ChatMessageViewModel> ChatMessagesItems { get; set; } = new ObservableCollection<ChatMessageViewModel>();

	public ObservableCollection<NotificationItem> Notifications { get; } = new ObservableCollection<NotificationItem>();

	public ObservableCollection<UserOrder> ActiveOrders { get; set; } = new ObservableCollection<UserOrder>();

	public bool IsAdminDesktopUnlocked { get; private set; }

	public bool IsHighAccessMode { get; private set; }

	public event Action HighAccessChanged;

	public MainWindow()
	{
		InitializeComponent();
		Instance = this;
		base.DataContext = this;
		ApiService = new ApiService();
		MyPcId = HardwareId.Current;
		base.Title = "AetherShell | ID: " + (MyPcId ?? "Unknown");
		_uiTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_uiTimer.Tick += UiTimer_Tick;
		_uiTimer.Start();
		_lastInputTime = DateTime.Now;
		_idleTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		_idleTimer.Tick += IdleTimer_Tick;
		_idleTimer.Start();
		base.PreviewMouseMove += delegate
		{
			ResetIdleTimer();
		};
		base.PreviewKeyDown += delegate
		{
			ResetIdleTimer();
		};
		base.PreviewMouseDown += delegate
		{
			ResetIdleTimer();
		};
		_bannerTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_bannerTimer.Tick += BannerTimer_Tick;
		_appWatchTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_appWatchTimer.Tick += AppWatchTimer_Tick;
		_appWatchTimer.Start();
		base.Loaded += MainWindow_Loaded;
		base.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!_canClose)
			{
				e.Cancel = true;
			}
		};
	}

	private void ShowExitPasswordAndCloseIfOk()
	{
		TryEnterHighAccess();
	}

	private void TryEnterHighAccess()
	{
		if (IsHighAccessMode)
			return;

		ExitPasswordWindow exitPasswordWindow = new ExitPasswordWindow();
		if (exitPasswordWindow.ShowDialog() != true)
			return;

		if ((exitPasswordWindow.EnteredPassword ?? "") != AppConstants.EXIT_SHELL_PASSWORD)
		{
			new AetherShell.Client.Windows.MessageBox("Неверный пароль.", "Ошибка").ShowDialog();
			return;
		}

		// На экране входа — выход из шелла; после авторизации — высокий доступ.
		if (string.IsNullOrEmpty(_authToken))
		{
			CloseShellAfterExitPassword();
			return;
		}

		EnterHighAccessMode();
	}

	private void CloseShellAfterExitPassword()
	{
		try { ShellTaskbarWindow.HideForSession(); } catch { }
		try { KeyboardBlocker.Unblock(); } catch { }
		try { SystemUtils.RemoveRestrictions(); } catch { }
		try { TaskbarBlocker.Show(); } catch { }
		IsHighAccessMode = false;
		_canClose = true;
		Application.Current.Shutdown();
	}

	public void EnterHighAccessMode()
	{
		if (IsHighAccessMode) return;
		IsHighAccessMode = true;
		ThemeManager.ApplyHighAccessTheme();
		ApplyHighAccessChrome(true);
		ShellTaskbarWindow.ApplyHighAccessLook(true);
		if (HighAccessBadge != null)
			HighAccessBadge.Visibility = Visibility.Visible;
		HighAccessChanged?.Invoke();
	}

	public void LeaveHighAccessMode(bool forceReauth)
	{
		if (!IsHighAccessMode) return;
		IsHighAccessMode = false;
		ThemeManager.ApplyNormalTheme();
		ApplyHighAccessChrome(false);
		ShellTaskbarWindow.ApplyHighAccessLook(false);
		if (HighAccessBadge != null)
			HighAccessBadge.Visibility = Visibility.Collapsed;
		HighAccessChanged?.Invoke();

		// После высокого доступа всегда требуем повторный вход
		if (forceReauth)
			EndSession();
	}

	private void ApplyHighAccessChrome(bool enabled)
	{
		try
		{
			if (RootShellGrid != null)
			{
				if (enabled)
				{
					RootShellGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1C"));
				}
				else
				{
					// Restore Figma session gradient (do not leave a flat AppBackgroundBrush)
					RootShellGrid.Background = new LinearGradientBrush
					{
						StartPoint = new Point(0.15, 0),
						EndPoint = new Point(0.85, 1),
						GradientStops =
						{
							new GradientStop((Color)ColorConverter.ConvertFromString("#040208"), 0),
							new GradientStop((Color)ColorConverter.ConvertFromString("#0D0518"), 0.5),
							new GradientStop((Color)ColorConverter.ConvertFromString("#1E0933"), 1)
						}
					};
				}
			}
			if (ShellTopBar != null)
			{
				ShellTopBar.Background = enabled
					? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CC2A2A2E"))
					: new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0AFFFFFF"));
			}
			if (TopPcZoneDot != null)
			{
				var online = (Color)ColorConverter.ConvertFromString("#3DDC97");
				var muted = (Color)ColorConverter.ConvertFromString("#8A8A8E");
				var c = enabled ? muted : online;
				TopPcZoneDot.Fill = new SolidColorBrush(c);
				if (TopPcZoneDot.Effect is DropShadowEffect glow)
					glow.Color = c;
			}
		}
		catch { }
	}

	private void BtnLeaveHighAccess_Click(object sender, RoutedEventArgs e)
	{
		LeaveHighAccessMode(forceReauth: true);
	}

	private void BtnCloseShellHighAccess_Click(object sender, RoutedEventArgs e)
	{
		// Перед закрытием шелла — всегда реавторизация (сброс сессии)
		EndSession();
		CloseShellAfterExitPassword();
	}

	public void ExitShell()
	{
		KeyboardBlocker.Unblock();
		SystemUtils.RemoveRestrictions();
		TaskbarBlocker.Show();
		_canClose = true;
		Application.Current.Shutdown();
	}

	private async void ShowAdminPanel()
	{
		if (_adminPanelOpen)
		{
			return;
		}
		_adminPanelOpen = true;
		try
		{
			if (_adminSession == null)
			{
				AdminLoginWindow adminLoginWindow = new AdminLoginWindow(_adminApi)
				{
					Owner = this
				};
				if (adminLoginWindow.ShowDialog() != true)
				{
					return;
				}
				_adminSession = adminLoginWindow.Session;
			}
			AdminPanelWindow adminPanelWindow = new AdminPanelWindow(_adminApi, _adminSession, this);
			adminPanelWindow.Owner = this;
			adminPanelWindow.ShowDialog();
			await SyncSessionStatus();
		}
		catch (Exception)
		{
		}
		finally
		{
			_adminPanelOpen = false;
		}
	}

	public void EnterAdminDesktopMode()
	{
		IsAdminDesktopUnlocked = true;
		ShellTaskbarWindow.HideForSession();
		KeyboardBlocker.IsFullLock = false;
		KeyboardBlocker.Unblock();
		SystemUtils.RemoveRestrictions();
		TaskbarBlocker.Show();
		base.Topmost = false;
		WindowUtils.SetWindowGhostMode(this, enableGhost: true);
		WindowUtils.SendToBack(this);
	}

	public void LeaveAdminDesktopMode()
	{
		IsAdminDesktopUnlocked = false;
		KeyboardBlocker.Block();
		KeyboardBlocker.OnExitShellHotkeyPressed = delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowExitPasswordAndCloseIfOk));
		};
		KeyboardBlocker.OnAdminHotkeyPressed = delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowAdminPanel));
		};
		SystemUtils.ApplyRestrictions();
		TaskbarBlocker.StartKeepHidden();
		if (_isSessionActive)
		{
			KeyboardBlocker.IsFullLock = false;
			base.Topmost = false;
			WindowUtils.SetWindowGhostMode(this, enableGhost: true);
			WindowUtils.SendToBack(this);
			ShellTaskbarWindow.ShowForSession(this);
		}
		else
		{
			KeyboardBlocker.IsFullLock = true;
			base.WindowState = WindowState.Maximized;
			base.Topmost = true;
			WindowUtils.SetWindowGhostMode(this, enableGhost: false);
			Activate();
		}
	}

	private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		KeyboardBlocker.Block();
		KeyboardBlocker.OnExitShellHotkeyPressed = delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowExitPasswordAndCloseIfOk));
		};
		KeyboardBlocker.OnAdminHotkeyPressed = delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowAdminPanel));
		};
		EndSession();
		TaskbarBlocker.StartKeepHidden();
		WindowUtils.SetWindowGhostMode(this, enableGhost: true);
		if (PcNameText != null)
		{
			PcNameText.Text = "ID: " + MyPcId;
		}
		try
		{
			if (!string.IsNullOrEmpty(Settings.Default.SavedToken))
			{
				_authToken = Settings.Default.SavedToken;
				ApiService.SetAuthToken(_authToken);
			}
		}
		catch
		{
		}
		InitializeSignalR();
		await SyncSessionStatus();
		await LoadInitialData();
	}

	private async Task LoadInitialData()
	{
		_ = 3;
		try
		{
			await ApiService.GetTariffsAsync();
			await ApiService.GetAppsAsync();
			List<UserOrder> list = await ApiService.GetMyOrdersAsync();
			if (list != null)
			{
				foreach (UserOrder order in list)
				{
					if (order.Status != "Выдано" && order.Status != "Отменен" && !ActiveOrders.Any((UserOrder o) => o.Id == order.Id))
					{
						ActiveOrders.Add(order);
					}
				}
			}
			await LoadBanners();
		}
		catch
		{
		}
	}

	private void IdleTimer_Tick(object sender, EventArgs e)
	{
		if (IdleClockText != null)
		{
			IdleClockText.Text = DateTime.Now.ToString("HH:mm");
		}
		if (_isSessionActive)
		{
			if (IdleOverlay.Visibility == Visibility.Visible)
			{
				IdleOverlay.Visibility = Visibility.Collapsed;
			}
		}
		else if ((DateTime.Now - _lastInputTime).TotalSeconds >= 60.0 && IdleOverlay.Visibility != Visibility.Visible)
		{
			if (IdlePcNameText != null)
			{
				IdlePcNameText.Text = ((!string.IsNullOrEmpty(_cachedPcName)) ? _cachedPcName : MyPcId);
			}
			IdleOverlay.Visibility = Visibility.Visible;
		}
	}

	private async void AppWatchTimer_Tick(object sender, EventArgs e)
	{
		if (_signalRService == null || !_signalRService.IsConnected)
		{
			return;
		}
		string text = null;
		string windowTitle = null;
		if (_isSessionActive)
		{
			ForegroundApp foregroundApp = ProcessUtils.GetForegroundApp();
			bool flag = foregroundApp != null && string.Equals(foregroundApp.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
			if (foregroundApp != null && !flag)
			{
				text = foregroundApp.ProcessName;
				windowTitle = foregroundApp.WindowTitle;
			}
		}
		if (text == _lastReportedApp)
		{
			return;
		}
		_lastReportedApp = text;
		try
		{
			await _signalRService.SendCurrentAppAsync(text, windowTitle);
		}
		catch (Exception)
		{
		}
	}

	private void ResetIdleTimer()
	{
		_lastInputTime = DateTime.Now;
		if (IdleOverlay.Visibility == Visibility.Visible)
		{
			IdleOverlay.Visibility = Visibility.Collapsed;
		}
	}

	private async Task LoadBanners()
	{
		try
		{
			List<Banner> list = await ApiService.GetBannersAsync();
			_activeBanners.Clear();
			_bannerTimer.Stop();
			if (list == null || list.Count <= 0)
			{
				return;
			}
			_activeBanners = list.Where((Banner b) => b.IsActive).ToList();
			if (_activeBanners.Count > 0)
			{
				BannerContainer.Visibility = Visibility.Visible;
				_currentBannerIndex = 0;
				SetImageSource(CurrentBannerImg, _activeBanners[0].ImageUrl);
				BannerContainer.UpdateLayout();
				double actualWidth = BannerContainer.ActualWidth;
				if (actualWidth > 0.0)
				{
					CurrentBannerImg.Width = actualWidth;
					NextBannerImg.Width = actualWidth;
					Canvas.SetLeft(NextBannerImg, actualWidth);
				}
				BannerContainer.Tag = _activeBanners[0];
				if (_activeBanners.Count > 1)
				{
					_bannerTimer.Start();
				}
			}
			else
			{
				BannerContainer.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
			if (BannerContainer != null)
			{
				BannerContainer.Visibility = Visibility.Collapsed;
			}
		}
	}

	private void SetImageSource(Image img, string url)
	{
		try
		{
			if (!string.IsNullOrEmpty(url) && url.StartsWith("/"))
			{
				url = AppConstants.SERVER_URL + url;
			}
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri(url, UriKind.Absolute);
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.EndInit();
			img.Source = bitmapImage;
		}
		catch
		{
		}
	}

	private void BannerTimer_Tick(object sender, EventArgs e)
	{
		if (_activeBanners.Count <= 1 || BannerContainer.ActualWidth == 0.0)
		{
			return;
		}
		double actualWidth = BannerContainer.ActualWidth;
		CurrentBannerImg.Width = actualWidth;
		NextBannerImg.Width = actualWidth;
		CurrentBannerImg.BeginAnimation(Canvas.LeftProperty, null);
		NextBannerImg.BeginAnimation(Canvas.LeftProperty, null);
		Canvas.SetLeft(CurrentBannerImg, 0.0);
		Canvas.SetLeft(NextBannerImg, actualWidth);
		int nextIndex = _currentBannerIndex + 1;
		if (nextIndex >= _activeBanners.Count)
		{
			nextIndex = 0;
		}
		SetImageSource(NextBannerImg, _activeBanners[nextIndex].ImageUrl);
		DoubleAnimation animation = new DoubleAnimation
		{
			From = 0.0,
			To = 0.0 - actualWidth,
			Duration = TimeSpan.FromSeconds(0.8),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		};
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			From = actualWidth,
			To = 0.0,
			Duration = TimeSpan.FromSeconds(0.8),
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseInOut
			}
		};
		doubleAnimation.Completed += delegate
		{
			if (_activeBanners != null && _activeBanners.Count != 0 && nextIndex < _activeBanners.Count)
			{
				CurrentBannerImg.Source = NextBannerImg.Source;
				CurrentBannerImg.BeginAnimation(Canvas.LeftProperty, null);
				Canvas.SetLeft(CurrentBannerImg, 0.0);
				_currentBannerIndex = nextIndex;
				BannerContainer.Tag = _activeBanners[_currentBannerIndex];
			}
		};
		CurrentBannerImg.BeginAnimation(Canvas.LeftProperty, animation);
		NextBannerImg.BeginAnimation(Canvas.LeftProperty, doubleAnimation);
	}

	private void PromoBanner_Click(object sender, RoutedEventArgs e)
	{
		if (BannerContainer.Tag is Banner banner && !string.IsNullOrEmpty(banner.ClickUrl))
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = banner.ClickUrl,
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}
	}

	public void AddSpentAmount(decimal amount)
	{
		CurrentUserTotalSpent += amount;
		UpdateLoyaltyUI();
	}

	public int GetCurrentDiscountPercent()
	{
		if (CurrentUserDiscountPercent.HasValue)
		{
			int num = CurrentUserDiscountPercent.Value;
			if (num < 0)
			{
				num = 0;
			}
			if (num > 100)
			{
				num = 100;
			}
			return num;
		}
		decimal currentUserTotalSpent = CurrentUserTotalSpent;
		if (currentUserTotalSpent < 50000m)
		{
			return 0;
		}
		int num2 = (int)((-19.0 + Math.Sqrt(361.0 + (double)currentUserTotalSpent / 625.0)) / 2.0);
		if (num2 > 100)
		{
			num2 = 100;
		}
		return num2;
	}

	public void UpdateLoyaltyUI()
	{
		if (DiscountText != null && LoyaltyProgress != null)
		{
			decimal currentUserTotalSpent = CurrentUserTotalSpent;
			int discountPercent = GetCurrentDiscountPercent();
			decimal progressValue = 0m;
			decimal maxProgress = 0m;
			if (currentUserTotalSpent < 50000m)
			{
				maxProgress = 50000m;
				progressValue = currentUserTotalSpent;
			}
			else
			{
				decimal num = 2500m * (decimal)discountPercent * (decimal)discountPercent + 47500m * (decimal)discountPercent;
				decimal num2 = 50000 + 5000 * discountPercent;
				maxProgress = num2;
				progressValue = currentUserTotalSpent - num;
			}
			base.Dispatcher.Invoke(delegate
			{
				DiscountText.Text = $"{discountPercent}%";
				LoyaltyProgress.Maximum = (double)maxProgress;
				LoyaltyProgress.Value = (double)progressValue;
			});
		}
	}





	private void EnsureAuthMessageTimer()
	{
		if (_authMessageClearTimer != null)
		{
			return;
		}
		_authMessageClearTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		_authMessageClearTimer.Tick += delegate
		{
			_authMessageClearTimer.Stop();
			if (_authMessageClearTarget != null)
			{
				_authMessageClearTarget.Text = "";
			}
			_authMessageClearTarget = null;
		};
	}



	/// <summary>Показать сообщение на экране входа; через несколько секунд само скрывается.</summary>
	private void SetAuthMessage(string text, bool sticky = false)
	{
		if (AuthErrorText != null)
			AuthErrorText.Text = text ?? "";

		_authMessageClearTimer?.Stop();
		if (sticky || string.IsNullOrEmpty(text))
			return;

		if (_authMessageClearTimer == null)
		{
			_authMessageClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
			_authMessageClearTimer.Tick += (_, __) =>
			{
				_authMessageClearTimer.Stop();
				if (AuthErrorText != null)
					AuthErrorText.Text = "";
				if (RecErrorText != null
					&& RecErrorText.Text != "Отправка..."
					&& RecErrorText.Foreground != Brushes.Green)
				{
					// RecError очищаем отдельным таймером ниже
				}
			};
		}
		_authMessageClearTimer.Start();
	}

	private void SetRecMessage(string text, Brush foreground = null, bool sticky = false)
	{
		if (RecErrorText != null)
		{
			RecErrorText.Text = text ?? "";
			if (foreground != null)
				RecErrorText.Foreground = foreground;
		}

		_authMessageClearTimer?.Stop();
		if (sticky || string.IsNullOrEmpty(text))
			return;

		if (_authMessageClearTimer == null)
		{
			_authMessageClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
			_authMessageClearTimer.Tick += (_, __) =>
			{
				_authMessageClearTimer.Stop();
				if (AuthErrorText != null)
					AuthErrorText.Text = "";
				if (RecErrorText != null)
					RecErrorText.Text = "";
			};
		}
		else
		{
			// Переиспользуем тот же таймер: очищаем оба поля
			_authMessageClearTimer.Tick -= AuthMessageClearTick;
			_authMessageClearTimer.Tick -= RecMessageClearTick;
			_authMessageClearTimer.Tick += RecMessageClearTick;
		}
		_authMessageClearTimer.Interval = TimeSpan.FromSeconds(5);
		_authMessageClearTimer.Start();
	}

	private void AuthMessageClearTick(object sender, EventArgs e)
	{
		_authMessageClearTimer.Stop();
		if (AuthErrorText != null)
			AuthErrorText.Text = "";
	}

	private void RecMessageClearTick(object sender, EventArgs e)
	{
		_authMessageClearTimer.Stop();
		if (AuthErrorText != null)
			AuthErrorText.Text = "";
		if (RecErrorText != null)
			RecErrorText.Text = "";
	}

	private void ClearAuthMessages()
	{
		_authMessageClearTimer?.Stop();
		if (AuthErrorText != null)
			AuthErrorText.Text = "";
		if (RecErrorText != null)
			RecErrorText.Text = "";
	}

	private void BtnToggleLoginPassword_Click(object sender, RoutedEventArgs e)
	{
		if (LoginPassword == null || LoginPasswordVisible == null) return;
		if (LoginPassword.Visibility == Visibility.Visible)
		{
			LoginPasswordVisible.Text = LoginPassword.Password;
			LoginPassword.Visibility = Visibility.Collapsed;
			LoginPasswordVisible.Visibility = Visibility.Visible;
		}
		else
		{
			LoginPassword.Password = LoginPasswordVisible.Text ?? "";
			LoginPasswordVisible.Visibility = Visibility.Collapsed;
			LoginPassword.Visibility = Visibility.Visible;
		}
	}

	private string GetLoginPassword()
	{
		if (LoginPasswordVisible != null && LoginPasswordVisible.Visibility == Visibility.Visible)
			return LoginPasswordVisible.Text ?? "";
		return LoginPassword?.Password ?? "";
	}

	private async void BtnLogin_Click(object sender, RoutedEventArgs e)
	{
		string password = GetLoginPassword();
		if (string.IsNullOrWhiteSpace(LoginUsername.Text) || string.IsNullOrWhiteSpace(password))
		{
			return;
		}
		SetAuthMessage("Вход...", sticky: true);
		try
		{
			LoginResponse result = await ApiService.LoginAsync(LoginUsername.Text, password, MyPcId);
			if (result == null || string.IsNullOrWhiteSpace(result.token))
			{
				SetAuthMessage("Неверный логин или пароль.");
				return;
			}
			_authToken = result.token;
			CurrentUserTotalSpent = result.totalSpent;
			CurrentUserDiscountPercent = result.discountPercent;
			UpdateLoyaltyUI();
			try
			{
				Settings.Default.SavedToken = _authToken;
				Settings.Default.Save();
			}
			catch
			{
			}
			ApiService.SetAuthToken(_authToken);
			CurrentUsername = result.username;
			CurrentAvatarUrl = result.avatarUrl;
			UpdateUserChrome(CurrentUsername);
			UpdateBalanceUI(result.balance);
			if (_signalRService != null)
			{
				await _signalRService.StopAsync();
			}
			InitializeSignalR();
			if (result.hasActiveSession)
			{
				if (LockedScreen != null)
				{
					LockedScreen.Visibility = Visibility.Collapsed;
				}
				await SyncSessionStatus();
			}
			else
			{
				ShowLockScreenShop();
			}
			await LoadBanners();
		}
		catch (Exception ex)
		{
			SetAuthMessage("Ошибка: " + ex.Message);
		}
	}

	private async void ShowLockScreenShop()
	{
		if (LockScreenShopPanel != null)
		{
			LockScreenShopPanel.Visibility = Visibility.Visible;
		}
		if (AuthContentLayer != null)
		{
			AuthContentLayer.Effect = new BlurEffect
			{
				Radius = 30.0,
				RenderingBias = RenderingBias.Quality
			};
		}
		if (LoginPanel != null)
		{
			LoginPanel.Visibility = Visibility.Visible;
		}
		if (RegisterPanel != null)
		{
			RegisterPanel.Visibility = Visibility.Collapsed;
		}
		ClearAuthMessages();
		try
		{
			List<TariffItem> itemsSource = await ApiService.GetTariffsAsync();
			if (LockScreenTariffsList != null)
			{
				LockScreenTariffsList.ItemsSource = itemsSource;
			}
		}
		catch
		{
			SetAuthMessage("Не удалось загрузить тарифы.");
		}
	}

	private async void BtnBuyLockTariff_Click(object sender, RoutedEventArgs e)
	{
		Button button = sender as Button;
		int tariffId = 0;
		int num2;
		object tag;
		if (button != null)
		{
			tag = button.Tag;
			int num;
			if (tag is int)
			{
				tariffId = (int)tag;
				num = 1;
			}
			else
			{
				num = 0;
			}
			num2 = ((num == 0) ? 1 : 0);
		}
		else
		{
			num2 = 1;
		}
		if (num2 != 0)
		{
			return;
		}
		decimal num3 = 0m;
		tag = button.DataContext;
		if (tag is TariffItem tariffItem)
		{
			num3 = tariffItem.Price;
		}
		int currentDiscountPercent = GetCurrentDiscountPercent();
		decimal finalPrice = num3;
		if (currentDiscountPercent > 0)
		{
			finalPrice = num3 - num3 * (decimal)currentDiscountPercent / 100m;
		}
		string m = ((currentDiscountPercent > 0) ? $"Купить тариф?\nЦена: {num3:N0} ?\nСкидка {currentDiscountPercent}%: -{num3 - finalPrice:N0} ?\nИтого: {finalPrice:N0} ?" : $"Купить тариф за {num3:N0} ??");
		ShowCustomMessage("Покупка", m, async delegate
		{
			_ = 1;
			try
			{
				BuyTariffRequest request = new BuyTariffRequest
				{
					Username = CurrentUsername,
					MacAddress = MyPcId,
					TariffId = tariffId
				};
				OrderResponse orderResponse = await ApiService.BuyTariffAsync(request);
				UpdateBalanceUI(orderResponse.newBalance);
				if (finalPrice > 0m)
				{
					AddSpentAmount(finalPrice);
				}
				await SyncSessionStatus();
				if (LockedScreen != null)
				{
					LockedScreen.Visibility = Visibility.Collapsed;
				}
				StartSession();
			}
			catch (Exception ex)
			{
				ShowCustomMessage("Ошибка", ex.Message);
			}
		});
	}

	public void StartSession()
	{
		_isSessionActive = true;
		_warning20MinPlayed = false;
		_warning10MinPlayed = false;
		_warning5MinPlayed = false;
		KeyboardBlocker.IsFullLock = false;
		TaskbarBlocker.StartKeepHidden();
		SystemUtils.ApplyRestrictions();
		if (LockedScreen != null)
		{
			LockedScreen.Visibility = Visibility.Collapsed;
		}
		if (LockScreenShopPanel != null)
		{
			LockScreenShopPanel.Visibility = Visibility.Collapsed;
		}
		base.Topmost = false;
		WindowUtils.SetWindowGhostMode(this, enableGhost: true);
		WindowUtils.SendToBack(this);
		if (MainFrame.Content == null)
		{
			Nav_Apps_Click(null, null);
		}
		UpdateLoyaltyUI();
		ShellTaskbarWindow.ShowForSession(this);
	}

	/// <summary>Освобождает низ экрана под кастомную панель задач.</summary>


	public void ApplyTaskbarInset(bool enabled)
	{
		if (RootShellGrid != null)
		{
			RootShellGrid.Margin = (enabled ? new Thickness(0.0, 0.0, 0.0, 48.0) : new Thickness(0.0));
		}
	}

	public void EndSession()
	{
		ShellTaskbarWindow.HideForSession();
		_isSessionActive = false;
		_sessionEndTime = DateTime.MinValue;
		ProcessKiller.KillGames();
		SystemUtils.ApplyRestrictions();
		TaskbarBlocker.StartKeepHidden();
		TaskbarBlocker.StartKeepHidden();
		ChatMessagesItems.Clear();
		ActiveOrders.Clear();
		NotificationWindow.Reset();
		if (ChatInput != null)
		{
			ChatInput.Text = "";
		}
		if (ChatOverlay != null)
		{
			ChatOverlay.Visibility = Visibility.Collapsed;
		}
		if (SidebarTimer != null)
		{
			SidebarTimer.Text = "00:00:00";
			SidebarTimer.Foreground = _normalColor;
		}
		KeyboardBlocker.IsFullLock = true;
		if (LockedScreen != null)
		{
			LockedScreen.Visibility = Visibility.Visible;
		}
		if (AuthContentLayer != null)
		{
			AuthContentLayer.Effect = null;
		}
		if (LockScreenShopPanel != null)
		{
			LockScreenShopPanel.Visibility = Visibility.Collapsed;
		}
		if (RegisterPanel != null)
		{
			RegisterPanel.Visibility = Visibility.Collapsed;
		}
		if (LoginPanel != null)
		{
			LoginPanel.Visibility = Visibility.Visible;
		}
		CurrentUsername = null;
		CurrentAvatarUrl = null;
		_authToken = null;
		CurrentUserTotalSpent = 0m;
		CurrentUserDiscountPercent = null;
		ApiService.SetAuthToken(null);
		_bannerTimer.Stop();
		_activeBanners.Clear();
		try
		{
			Settings.Default.SavedToken = "";
			Settings.Default.Save();
		}
		catch
		{
		}
		if (LoginUsername != null)
		{
			LoginUsername.Text = "";
		}
		if (LoginPassword != null)
		{
			LoginPassword.Password = "";
		}
		if (SidebarUsername != null)
		{
			SidebarUsername.Text = "...";
		}
		if (SidebarAvatar != null)
		{
			SidebarAvatar.Text = "?";
		}
		ApplySidebarAvatarImage(null);
		if (SidebarBalance != null)
		{
			SidebarBalance.Text = "0 ₸";
		}
		if (DiscountText != null)
		{
			DiscountText.Text = "0%";
		}
		if (SidebarTariffHint != null)
		{
			SidebarTariffHint.Text = "Тариф: —";
		}
		base.WindowState = WindowState.Maximized;
		base.Topmost = true;
		WindowUtils.SetWindowGhostMode(this, enableGhost: false);
		Activate();
	}

	private async void BtnLogout_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!string.IsNullOrEmpty(MyPcId))
			{
				await ApiService.StopSessionAsync(MyPcId);
			}
		}
		catch (Exception)
		{
		}
		CurrentUsername = null;
		CurrentAvatarUrl = null;
		ApiService.SetAuthToken(null);
		try
		{
			Settings.Default.SavedToken = "";
			Settings.Default.Save();
		}
		catch
		{
		}
		_sessionEndTime = DateTime.MinValue;
		_isSessionActive = false;
		if (SidebarUsername != null)
		{
			SidebarUsername.Text = "...";
		}
		if (SidebarAvatar != null)
		{
			SidebarAvatar.Text = "?";
		}
		ApplySidebarAvatarImage(null);
		if (SidebarTimer != null)
		{
			SidebarTimer.Text = "00:00:00";
		}
		if (SidebarBalance != null)
		{
			SidebarBalance.Text = "0 ₸";
		}
		if (SidebarTariffHint != null)
		{
			SidebarTariffHint.Text = "Тариф: —";
		}
		EndSession();
	}

	private List<ClubMapPcItem> _clubMapPcs = new List<ClubMapPcItem>();

	private async void BtnOpenClubMap_Click(object sender, RoutedEventArgs e)
	{
		if (!_isSessionActive || string.IsNullOrEmpty(_authToken))
		{
			new AetherShell.Client.Windows.MessageBox("Сначала войдите и начните сессию.", "Карта клуба").ShowDialog();
			return;
		}
		if (ClubMapOverlay != null)
			ClubMapOverlay.Visibility = Visibility.Visible;
		await ReloadClubMapAsync();
	}

	private void BtnCloseClubMap_Click(object sender, RoutedEventArgs e)
	{
		if (ClubMapOverlay != null)
			ClubMapOverlay.Visibility = Visibility.Collapsed;
	}

	private async void BtnRefreshClubMap_Click(object sender, RoutedEventArgs e)
	{
		await ReloadClubMapAsync();
	}

	private void ClubMapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RenderClubMap();
	}

	private async Task ReloadClubMapAsync()
	{
		try
		{
			if (ClubMapHint != null)
				ClubMapHint.Text = "Загрузка…";
			_clubMapPcs = await ApiService.GetComputersMapAsync(MyPcId) ?? new List<ClubMapPcItem>();
			RenderClubMap();
			if (ClubMapHint != null)
				ClubMapHint.Text = "Нажмите на свободный ПК, чтобы перенести сессию";
		}
		catch (Exception ex)
		{
			if (ClubMapHint != null)
				ClubMapHint.Text = "Не удалось загрузить карту: " + ex.Message;
		}
	}

	private void RenderClubMap()
	{
		if (ClubMapCanvas == null) return;
		ClubMapCanvas.Children.Clear();
		double w = ClubMapCanvas.ActualWidth;
		double h = ClubMapCanvas.ActualHeight;
		if (w < 10 || h < 10 || _clubMapPcs == null || _clubMapPcs.Count == 0) return;

		var positions = BuildMapPositions(_clubMapPcs);
		const double tile = 78;

		foreach (var pc in _clubMapPcs)
		{
			if (!positions.TryGetValue(pc.id, out var pos)) continue;
			double left = pos.Item1 / 100.0 * w - tile / 2;
			double top = pos.Item2 / 100.0 * h - tile / 2;
			left = Math.Max(4, Math.Min(w - tile - 4, left));
			top = Math.Max(4, Math.Min(h - tile - 4, top));

			Brush fill;
			if (pc.isCurrent)
				fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4FC3F7"));
			else if (string.Equals(pc.availability, "Free", StringComparison.OrdinalIgnoreCase))
				fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3DDC97"));
			else if (string.Equals(pc.availability, "Busy", StringComparison.OrdinalIgnoreCase))
				fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5ED7"));
			else
				fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));

			var border = new Border
			{
				Width = tile,
				Height = tile,
				CornerRadius = new CornerRadius(12),
				Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1F2B")),
				BorderBrush = fill,
				BorderThickness = new Thickness(2),
				Cursor = (pc.isCurrent || !string.Equals(pc.availability, "Free", StringComparison.OrdinalIgnoreCase))
					? Cursors.Arrow
					: Cursors.Hand,
				Tag = pc,
				ToolTip = (pc.displayName ?? pc.name) + " — " + pc.StatusLabel
			};
			Canvas.SetLeft(border, left);
			Canvas.SetTop(border, top);

			var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) };
			stack.Children.Add(new TextBlock
			{
				Text = pc.displayName ?? pc.name,
				Foreground = Brushes.White,
				FontWeight = FontWeights.SemiBold,
				FontSize = 12,
				TextAlignment = TextAlignment.Center,
				TextWrapping = TextWrapping.Wrap
			});
			stack.Children.Add(new TextBlock
			{
				Text = pc.StatusLabel,
				Foreground = fill,
				FontSize = 11,
				TextAlignment = TextAlignment.Center,
				Margin = new Thickness(0, 4, 0, 0)
			});
			border.Child = stack;

			if (!pc.isCurrent && string.Equals(pc.availability, "Free", StringComparison.OrdinalIgnoreCase))
				border.MouseLeftButtonUp += ClubMapPc_Click;

			ClubMapCanvas.Children.Add(border);
		}
	}

	private static Dictionary<int, Tuple<double, double>> BuildMapPositions(List<ClubMapPcItem> pcs)
	{
		var result = new Dictionary<int, Tuple<double, double>>();
		var needAuto = new List<ClubMapPcItem>();
		foreach (var pc in pcs)
		{
			if (pc.mapX.HasValue && pc.mapY.HasValue)
				result[pc.id] = Tuple.Create(pc.mapX.Value, pc.mapY.Value);
			else
				needAuto.Add(pc);
		}

		int cols = Math.Max(4, (int)Math.Ceiling(Math.Sqrt(Math.Max(needAuto.Count, 1))));
		for (int i = 0; i < needAuto.Count; i++)
		{
			int col = i % cols;
			int row = i / cols;
			double x = 12 + col * (76.0 / Math.Max(cols - 1, 1));
			double y = 18 + row * 16;
			result[needAuto[i].id] = Tuple.Create(x, y);
		}
		return result;
	}

	private async void ClubMapPc_Click(object sender, MouseButtonEventArgs e)
	{
		if (!(sender is FrameworkElement fe) || !(fe.Tag is ClubMapPcItem pc)) return;
		if (!_isSessionActive || string.IsNullOrEmpty(MyPcId)) return;

		string label = pc.displayName ?? pc.name;
		if (!WindowUtils.Msg.Ask(
			"Перенести сессию на «" + label + "»?\nТекущий ПК освободится.",
			"Пересадка"))
			return;

		try
		{
			if (ClubMapHint != null) ClubMapHint.Text = "Перенос…";
			var result = await ApiService.TransferSessionAsync(MyPcId, pc.name);
			if (ClubMapOverlay != null)
				ClubMapOverlay.Visibility = Visibility.Collapsed;
			new AetherShell.Client.Windows.MessageBox(
				"Сессия перенесена на «" + result.TargetDisplayName + "».\nПерейдите к этому компьютеру.",
				"Готово").ShowDialog();
			EndSession();
		}
		catch (Exception ex)
		{
			new AetherShell.Client.Windows.MessageBox(ex.Message, "Пересадка").ShowDialog();
			await ReloadClubMapAsync();
		}
	}

	private async Task SyncSessionStatus()
	{
		try
		{
			SessionStatusDto sessionStatusDto = await ApiService.GetSessionStatusAsync(MyPcId);
			if (sessionStatusDto == null)
			{
				return;
			}
			if (!string.IsNullOrEmpty(sessionStatusDto.PcName))
			{
				_cachedPcName = sessionStatusDto.PcName;
			}
			if (PcNameText != null)
			{
				PcNameText.Text = sessionStatusDto.PcName ?? ("ID: " + MyPcId);
			}
			if (TopPcZoneText != null)
			{
				TopPcZoneText.Text = sessionStatusDto.PcName ?? MyPcId ?? "pc";
			}
			_enableShop = sessionStatusDto.enableShop;
			ApplyShopVisibility();
			if (!string.IsNullOrEmpty(sessionStatusDto.avatarUrl))
			{
				SetAvatarUrl(sessionStatusDto.avatarUrl);
			}
			if (SidebarTariffHint != null)
			{
				SidebarTariffHint.Text = (string.IsNullOrEmpty(sessionStatusDto.tariffName) ? "Тариф: —" : ("Тариф: " + sessionStatusDto.tariffName));
			}
			if (SidebarSessionLabel != null)
			{
				SidebarSessionLabel.Text = (sessionStatusDto.IsActive ? "Активна - осталось" : "Нет активной сессии");
			}
			if (!string.IsNullOrEmpty(CurrentUsername) && !string.IsNullOrEmpty(sessionStatusDto.Username) && CurrentUsername != sessionStatusDto.Username)
			{
				return;
			}
			if (string.IsNullOrEmpty(CurrentUsername) && !string.IsNullOrEmpty(sessionStatusDto.Username))
			{
				CurrentUsername = sessionStatusDto.Username;
				UpdateUserChrome(CurrentUsername);
			}
			UpdateBalanceUI(sessionStatusDto.Balance);
			if (sessionStatusDto.IsActive)
			{
				_sessionEndTime = ((sessionStatusDto.EndTime.Kind == DateTimeKind.Unspecified) ? DateTime.SpecifyKind(sessionStatusDto.EndTime, DateTimeKind.Utc) : sessionStatusDto.EndTime).ToLocalTime();
				if (_sessionEndTime > DateTime.Now)
				{
					if (!_isSessionActive)
					{
						StartSession();
					}
					UpdateTimerDisplay();
				}
				else if (_isSessionActive)
				{
					EndSession();
				}
			}
			else if (_isSessionActive)
			{
				EndSession();
			}
		}
		catch (Exception)
		{
		}
	}

	private void UiTimer_Tick(object sender, EventArgs e)
	{
		if (BigClockTime != null)
		{
			BigClockTime.Text = DateTime.Now.ToString("HH:mm");
		}
		UpdateTimerDisplay();
	}

	private void UpdateTimerDisplay()
	{
		if (!_isSessionActive)
		{
			if (SidebarTimer != null)
			{
				SidebarTimer.Text = "00:00:00";
			}
			return;
		}
		TimeSpan timeSpan = _sessionEndTime - DateTime.Now;
		double totalMinutes = timeSpan.TotalMinutes;
		if (timeSpan.TotalSeconds <= 0.0)
		{
			EndSession();
			return;
		}
		if (SidebarTimer != null)
		{
			SidebarTimer.Text = timeSpan.ToString("hh\\:mm\\:ss");
			SidebarTimer.Foreground = ((totalMinutes < 5.0) ? _warningColor : _normalColor);
		}
		if (totalMinutes > 20.0)
		{
			_warning20MinPlayed = false;
		}
		if (totalMinutes > 10.0)
		{
			_warning10MinPlayed = false;
		}
		if (totalMinutes > 5.0)
		{
			_warning5MinPlayed = false;
		}
		if (totalMinutes <= 20.0 && totalMinutes > 19.0 && !_warning20MinPlayed)
		{
			_warning20MinPlayed = true;
			SoundUtils.PlayNotificationSound();
			NotificationWindow.UpdateTimer("До конца сессии осталось 20 минут");
			Task.Delay(5000).ContinueWith(delegate
			{
				base.Dispatcher.Invoke(NotificationWindow.ClearTimer);
			});
		}
		else if (totalMinutes <= 10.0 && totalMinutes > 9.0 && !_warning10MinPlayed)
		{
			_warning10MinPlayed = true;
			SoundUtils.PlayNotificationSound();
			NotificationWindow.UpdateTimer("До конца сессии осталось 10 минут");
			Task.Delay(5000).ContinueWith(delegate
			{
				base.Dispatcher.Invoke(NotificationWindow.ClearTimer);
			});
		}
		else if (totalMinutes <= 5.0 && !_warning5MinPlayed)
		{
			_warning5MinPlayed = true;
			SoundUtils.PlayWarningSound();
			NotificationWindow.UpdateTimer($"До конца сессии осталось {(int)totalMinutes + 1} мин");
			Task.Delay(5000).ContinueWith(delegate
			{
				base.Dispatcher.Invoke(NotificationWindow.ClearTimer);
			});
		}
	}

	private void HighlightMenuButton(Button activeButton)
	{
		SetButtonStyle(BtnNavGames, "ShellNavTabStyle");
		SetButtonStyle(BtnNavApps, "ShellNavTabStyle");
		SetButtonStyle(BtnNavFood, "ShellNavTabStyle");
		SetButtonStyle(BtnNavTariffs, "ShellNavTabStyle");
		if (activeButton != null)
		{
			SetButtonStyle(activeButton, "ShellNavTabActiveStyle");
		}
	}

	private void SetButtonStyle(Button btn, string styleKey)
	{
		if (btn != null)
		{
			btn.Style = (Style)FindResource(styleKey);
		}
	}

	private void ApplyShopVisibility()
	{
		if (BtnNavFood != null)
		{
			BtnNavFood.Visibility = ((!_enableShop) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (!_enableShop && MainFrame?.Content is FoodPage)
		{
			Nav_Apps_Click(null, null);
		}
	}

	private void Nav_Dashboard_Click(object sender, RoutedEventArgs e)
	{
		Nav_Apps_Click(sender, e);
	}

	private void Nav_Games_Click(object sender, RoutedEventArgs e)
	{
		HighlightMenuButton(BtnNavGames);
		SetCategoryRailVisible(true, "Game");
		MainFrame.Navigate(new GamesPage("Game"));
	}

	private void Nav_Apps_Click(object sender, RoutedEventArgs e)
	{
		HighlightMenuButton(BtnNavApps);
		SetCategoryRailVisible(true, "Application");
		MainFrame.Navigate(new GamesPage("Application"));
	}

	private void Nav_Food_Click(object sender, RoutedEventArgs e)
	{
		if (_enableShop)
		{
			HighlightMenuButton(BtnNavFood);
			SetCategoryRailVisible(false);
			MainFrame.Navigate(new FoodPage());
		}
	}

	private void Nav_Tariffs_Click(object sender, RoutedEventArgs e)
	{
		HighlightMenuButton(BtnNavTariffs);
		SetCategoryRailVisible(false);
		MainFrame.Navigate(new TariffsPage());
	}

	public void NavigateToGames(string category = "Game")
	{
		HighlightMenuButton((category == "Game") ? BtnNavGames : BtnNavApps);
		SetCategoryRailVisible(true, category);
		MainFrame.Navigate(new GamesPage(category));
	}

	public void NavigateToTariffs()
	{
		HighlightMenuButton(BtnNavTariffs);
		SetCategoryRailVisible(false);
		MainFrame.Navigate(new TariffsPage());
	}

	public void SetCategoryRailVisible(bool visible, string mainCategory = null)
	{
		if (ShellCategoryRail != null)
			ShellCategoryRail.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
		if (CategoryCol != null)
			CategoryCol.Width = visible ? new GridLength(240) : new GridLength(0);
		if (ShellCategorySectionTitle != null && mainCategory != null)
			ShellCategorySectionTitle.Text = mainCategory == "Game" ? "ИГРЫ" : "ПРИЛОЖЕНИЯ";
	}

	private void UpdateUserChrome(string username)
	{
		if (SidebarUsername != null)
		{
			SidebarUsername.Text = username ?? "...";
		}
		if (SidebarAvatar != null)
		{
			string text = username ?? "?";
			SidebarAvatar.Text = ((text.Length >= 1) ? text.Substring(0, 1).ToUpperInvariant() : "?");
		}
		ApplySidebarAvatarImage(CurrentAvatarUrl);
	}

	public void SetAvatarUrl(string url)
	{
		CurrentAvatarUrl = url;
		ApplySidebarAvatarImage(url);
	}

		private void ApplySidebarAvatarImage(string url)
	{
		if (SidebarAvatarImage == null || SidebarAvatar == null)
			return;
		if (string.IsNullOrEmpty(url))
		{
			SidebarAvatarImage.Source = null;
			SidebarAvatarImage.Visibility = Visibility.Collapsed;
			SidebarAvatar.Visibility = Visibility.Visible;
			return;
		}
		try
		{
			string fullUrl = ResolveMediaUrl(url);
			if (string.IsNullOrEmpty(fullUrl))
				throw new Exception("empty");
			string sep = fullUrl.Contains("?") ? "&" : "?";
			var bmp = new BitmapImage();
			bmp.BeginInit();
			bmp.CacheOption = BitmapCacheOption.OnLoad;
			bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bmp.UriSource = new Uri(fullUrl + sep + "t=" + DateTime.UtcNow.Ticks, UriKind.Absolute);
			bmp.EndInit();
			bmp.Freeze();
			SidebarAvatarImage.Source = bmp;
			SidebarAvatarImage.Visibility = Visibility.Visible;
			SidebarAvatar.Visibility = Visibility.Collapsed;
		}
		catch
		{
			SidebarAvatarImage.Source = null;
			SidebarAvatarImage.Visibility = Visibility.Collapsed;
			SidebarAvatar.Visibility = Visibility.Visible;
		}
	}

	private static string ResolveMediaUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url)) return null;
		url = url.Trim();
		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
			|| url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return url;
		string root = (AppConstants.SERVER_URL ?? "").TrimEnd('/');
		if (url.StartsWith("/")) return root + url;
		return root + "/" + url;
	}


	private async void BtnEditAvatar_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "Выберите аватар",
				Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp|Все файлы|*.*"
			};
			if (openFileDialog.ShowDialog() == true)
			{
				SetAvatarUrl(await ApiService.UploadAvatarAsync(openFileDialog.FileName));
			}
		}
		catch (Exception ex)
		{
			new AetherShell.Client.Windows.MessageBox("Не удалось обновить аватар: " + ex.Message, "Ошибка").ShowDialog();
		}
	}

	public void UpdateBalanceUI(decimal balance)
	{
		if (SidebarBalance != null)
		{
			SidebarBalance.Text = $"{balance:N0} ₸";
		}
		if (LockScreenBalance != null)
		{
			LockScreenBalance.Text = $"{balance:N0} ₸";
		}
	}

	private void ShowRegister_Click(object s, RoutedEventArgs e)
	{
		LoginPanel.Visibility = Visibility.Collapsed;
		RegisterPanel.Visibility = Visibility.Visible;
		ClearAuthMessages();
	}

	private void ShowLogin_Click(object s, RoutedEventArgs e)
	{
		LoginPanel.Visibility = Visibility.Visible;
		RegisterPanel.Visibility = Visibility.Collapsed;
		if (RecoveryPanel != null)
		{
			RecoveryPanel.Visibility = Visibility.Collapsed;
		}
		ClearAuthMessages();
	}

	private async void BtnRegister_Click(object s, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(RegUsername.Text) || string.IsNullOrWhiteSpace(RegPassword.Password) || string.IsNullOrWhiteSpace(RegEmail.Text))
		{
			SetAuthMessage("Заполните все поля");
			return;
		}
		if (RegUsername.Text.Trim().Length < 4)
		{
			SetAuthMessage("Логин должен содержать минимум 4 символа");
			return;
		}
		if (RegPassword.Password.Length < 6)
		{
			SetAuthMessage("Пароль должен содержать минимум 6 символов");
			return;
		}
		if (!IsValidEmail(RegEmail.Text))
		{
			SetAuthMessage("Введите корректный Email адрес");
			return;
		}
		if (RegPassword.Password != RegPasswordConfirm.Password)
		{
			SetAuthMessage("Пароли не совпадают");
			return;
		}
		try
		{
			await ApiService.RegisterAsync(RegUsername.Text.Trim(), RegEmail.Text.Trim(), RegPassword.Password);
			RegEmail.Text = "";
			RegUsername.Text = "";
			RegPassword.Password = "";
			RegPasswordConfirm.Password = "";
			ShowCustomMessage("Успех", "Регистрация успешна! Теперь войдите.");
			ShowLogin_Click(null, null);
		}
		catch (Exception ex)
		{
			SetAuthMessage(ex.Message);
		}
	}

	private static bool IsValidEmail(string email)
	{
		if (string.IsNullOrWhiteSpace(email))
		{
			return false;
		}
		try
		{
			return new MailAddress(email).Address == email;
		}
		catch
		{
			return false;
		}
	}

	private void ShowRecovery_Click(object sender, RoutedEventArgs e)
	{
		LoginPanel.Visibility = Visibility.Collapsed;
		RegisterPanel.Visibility = Visibility.Collapsed;
		RecoveryPanel.Visibility = Visibility.Visible;
		RecStep1.Visibility = Visibility.Visible;
		RecStep2.Visibility = Visibility.Collapsed;
		ClearAuthMessages();
		RecEmail.Text = "";
		RecCode.Text = "";
		RecNewPassword.Password = "";
	}

	private async void BtnSendCode_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(RecEmail.Text))
		{
			SetRecMessage("Введите Email", Brushes.Red);
			return;
		}
		SetRecMessage("Отправка...", Brushes.Red, sticky: true);
		try
		{
			await ApiService.ForgotPasswordAsync(RecEmail.Text);
			RecStep1.Visibility = Visibility.Collapsed;
			RecStep2.Visibility = Visibility.Visible;
			SetRecMessage("Код отправлен!", Brushes.Green);
		}
		catch (Exception ex)
		{
			SetRecMessage(ex.Message, Brushes.Red);
		}
	}

	private async void BtnResetPass_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(RecCode.Text) || string.IsNullOrWhiteSpace(RecNewPassword.Password))
		{
			SetRecMessage("Введите код и пароль", Brushes.Red);
			return;
		}
		try
		{
			await ApiService.ResetPasswordAsync(RecEmail.Text, RecCode.Text, RecNewPassword.Password);
			ShowCustomMessage("Успех", "Пароль изменен! Теперь вы можете войти.");
			ShowLogin_Click(null, null);
		}
		catch (Exception ex)
		{
			SetRecMessage(ex.Message, Brushes.Red);
		}
	}

	public void AddToCart(ProductItem product)
	{
		CartItem cartItem = Cart.FirstOrDefault((CartItem c) => c.Product.Id == product.Id);
		if (cartItem != null)
		{
			cartItem.Quantity++;
			return;
		}
		Cart.Add(new CartItem
		{
			Product = product,
			Quantity = 1
		});
	}

	private void BtnTopUp_Click(object sender, RoutedEventArgs e)
	{
		string text = TopUpAmountBox?.Text?.Trim();
		if (!string.IsNullOrEmpty(text) && decimal.TryParse(text, out var result) && result > 0m)
		{
			StartTopUpAsync(result);
			return;
		}
		ShowInputOverlay("Пополнение", "Введите сумму (?):", string.IsNullOrEmpty(text) ? "1000" : text, async delegate(string amountStr)
		{
			if (decimal.TryParse(amountStr, out var result2) && result2 > 0m)
			{
				await StartTopUpAsync(result2);
			}
		});
	}

	private async Task StartTopUpAsync(decimal amount)
	{
		try
		{
			PaymentWindow paymentWindow = new PaymentWindow((await ApiService.CreatePaymentLinkAsync(new PaymentRequest
			{
				Amount = amount,
				Username = CurrentUsername,
				MacAddress = MyPcId
			})).url);
			paymentWindow.Owner = this;
			paymentWindow.ShowDialog();
		}
		catch (Exception ex)
		{
			ShowCustomMessage("Ошибка", ex.Message);
		}
	}

	private void BtnPaymentClose_Click(object s, RoutedEventArgs e)
	{
		PaymentOverlay.Visibility = Visibility.Collapsed;
	}

	private void BtnToggleChat_Click(object sender, RoutedEventArgs e)
	{
		if (ChatOverlay.Visibility == Visibility.Visible)
		{
			ChatOverlay.Visibility = Visibility.Collapsed;
			return;
		}
		ChatOverlay.Visibility = Visibility.Visible;
		ChatInput.Focus();
		ScrollChatDown();
	}

	private async void BtnSendMessage_Click(object sender, RoutedEventArgs e)
	{
		string text = ChatInput.Text.Trim();
		if (!string.IsNullOrWhiteSpace(text))
		{
			try
			{
				ChatMessagesItems.Add(new ChatMessageViewModel
				{
					Text = text,
					IsAdmin = false
				});
				ChatInput.Text = "";
				ScrollChatDown();
				await _signalRService.SendToAdminAsync(text);
			}
			catch (Exception ex)
			{
				ShowCustomMessage("Ошибка", ex.Message);
			}
		}
	}

	private void ChatInput_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			BtnSendMessage_Click(sender, e);
		}
	}

	private void ScrollChatDown()
	{
		try
		{
			ChatScrollViewer?.ScrollToBottom();
		}
		catch
		{
		}
	}

	public void ShowInputOverlay(string t, string m, string d, Action<string> c)
	{
		InputTitle.Text = t;
		InputMessage.Text = m;
		InputTextBox.Text = d;
		_pendingInputCallback = c;
		InputOverlay.Visibility = Visibility.Visible;
	}

	private void BtnInputOk_Click(object s, RoutedEventArgs e)
	{
		InputOverlay.Visibility = Visibility.Collapsed;
		_pendingInputCallback?.Invoke(InputTextBox.Text);
	}

	private void BtnInputCancel_Click(object s, RoutedEventArgs e)
	{
		InputOverlay.Visibility = Visibility.Collapsed;
	}

	public void ShowCustomMessage(string t, string m, Action a = null)
	{
		MsgTitle.Text = t;
		MsgContent.Text = m;
		_pendingMsgAction = a;
		BtnMsgNo.Visibility = ((a == null) ? Visibility.Collapsed : Visibility.Visible);
		BtnMsgYes.Content = ((a == null) ? "OK" : "Да");
		ConfirmationOverlay.Visibility = Visibility.Visible;
	}

	private void BtnMsgYes_Click(object s, RoutedEventArgs e)
	{
		ConfirmationOverlay.Visibility = Visibility.Collapsed;
		_pendingMsgAction?.Invoke();
	}

	private void BtnMsgClose_Click(object s, RoutedEventArgs e)
	{
		ConfirmationOverlay.Visibility = Visibility.Collapsed;
	}

	public void AddNotification(string m, bool w = false)
	{
		if (w)
		{
			ShowCustomMessage("Внимание", m);
		}
		Notifications.Add(new NotificationItem
		{
			Message = m,
			IsWarning = w,
			TimeStr = DateTime.Now.ToString("HH:mm")
		});
	}

	private void RefreshOpenCatalogPages()
	{
		try
		{
			object obj = MainFrame?.Content;
			if (!(obj is GamesPage gamesPage))
			{
				if (!(obj is FoodPage foodPage))
				{
					if (!(obj is TariffsPage tariffsPage))
					{
						if (obj is DashboardPage dashboardPage)
						{
							dashboardPage.RefreshFromServer();
						}
					}
					else
					{
						tariffsPage.RefreshFromServer();
					}
				}
				else
				{
					foodPage.RefreshFromServer();
				}
			}
			else
			{
				gamesPage.RefreshFromServer();
			}
		}
		catch
		{
		}
	}

	private void InitializeSignalR()
	{
		try
		{
			_signalRService = new SignalRService(MyPcId);
			if (!string.IsNullOrEmpty(_authToken))
			{
				_signalRService.SetAuthToken(_authToken);
			}
			_signalRService.OnUnlock += async delegate
			{
				await base.Dispatcher.InvokeAsync((Func<Task>)async delegate
				{
					await SyncSessionStatus();
				});
			};
			_signalRService.OnLock += delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					if (_isSessionActive)
					{
						EndSession();
					}
				});
			};
			_signalRService.OnPaymentSuccess += delegate(decimal bal)
			{
				base.Dispatcher.Invoke(delegate
				{
					UpdateBalanceUI(bal);
					SoundUtils.PlayNotificationSound();
					ShowCustomMessage("Успех", "Оплата прошла успешно!");
				});
			};
			_signalRService.OnBalanceUpdated += delegate(decimal bal)
			{
				base.Dispatcher.Invoke(delegate
				{
					UpdateBalanceUI(bal);
				});
			};
			_signalRService.OnBannersUpdated += delegate
			{
				base.Dispatcher.Invoke((Func<Task>)async delegate
				{
					await LoadBanners();
				});
			};
			_signalRService.OnAppsUpdated += delegate
			{
				base.Dispatcher.Invoke(RefreshOpenCatalogPages);
			};
			_signalRService.OnProductsUpdated += delegate
			{
				base.Dispatcher.Invoke(RefreshOpenCatalogPages);
			};
			_signalRService.OnTariffsUpdated += delegate
			{
				base.Dispatcher.Invoke(RefreshOpenCatalogPages);
			};
			_signalRService.OnLoyaltyUpdated += delegate
			{
				base.Dispatcher.Invoke(RefreshOpenCatalogPages);
			};
			_signalRService.OnChatMessage += delegate(string senderName, string message)
			{
				base.Dispatcher.Invoke(delegate
				{
					if (!(senderName == CurrentUsername))
					{
						ChatMessagesItems.Add(new ChatMessageViewModel
						{
							Text = message,
							IsAdmin = true
						});
						ChatOverlay.Visibility = Visibility.Visible;
						ScrollChatDown();
						SoundUtils.PlayNotificationSound();
					}
				});
			};
			_signalRService.OnShutdown += delegate
			{
				base.Dispatcher.Invoke(ProcessUtils.Shutdown);
			};
			_signalRService.OnReboot += delegate
			{
				base.Dispatcher.Invoke(ProcessUtils.Reboot);
			};
			_signalRService.OnOrderStatusUpdated += delegate(int orderId, string statusEng)
			{
				base.Dispatcher.Invoke(delegate
				{
					UserOrder userOrder = ActiveOrders.FirstOrDefault((UserOrder o) => o.Id == orderId);
					if (userOrder != null)
					{
						userOrder.Status = statusEng;
						int num = ActiveOrders.IndexOf(userOrder);
						if (num != -1)
						{
							ActiveOrders[num] = userOrder;
						}
						SoundUtils.PlayNotificationSound();
						bool isCompleted = statusEng.Equals("Completed", StringComparison.OrdinalIgnoreCase) || statusEng.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
						NotificationWindow.UpdateOrder($"Заказ #{orderId}: {userOrder.StatusText}", isCompleted);
					}
				});
			};
			_signalRService.OnPendingApproval += delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					_isPendingApproval = true;
					ShowPendingApprovalScreen();
				});
			};
			_signalRService.OnApproved += delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					_isPendingApproval = false;
					HidePendingApprovalScreen();
					AddNotification("ПК подтверждён администратором");
				});
			};
			_signalRService.OnConnected += delegate
			{
				base.Dispatcher.Invoke((Func<Task>)async delegate
				{
					_isOffline = false;
					HideOfflineIndicator();
					NotificationItem notificationItem = Notifications.FirstOrDefault((NotificationItem n) => n.Message.Contains("связь с сервером"));
					if (notificationItem != null)
					{
						Notifications.Remove(notificationItem);
					}
					await SendSystemInfoToServer();
				});
			};
			_signalRService.OnDisconnected += delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					if (!_isOffline)
					{
						_isOffline = true;
						ShowOfflineIndicator();
						AddNotification("Потеряна связь с сервером. Таймер работает локально.", w: true);
					}
				});
			};
			_signalRService.OnReconnecting += delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					if (!_isOffline)
					{
						_isOffline = true;
						ShowOfflineIndicator();
					}
				});
			};
			_signalRService.OnReconnected += async delegate
			{
				await base.Dispatcher.InvokeAsync((Func<Task>)async delegate
				{
					_isOffline = false;
					HideOfflineIndicator();
					NotificationItem notificationItem = Notifications.FirstOrDefault((NotificationItem n) => n.Message.Contains("связь с сервером"));
					if (notificationItem != null)
					{
						Notifications.Remove(notificationItem);
					}
					await SyncSessionStatus();
					AddNotification("Связь с сервером восстановлена");
				});
			};
			InitializeSignalRConnectionAsync();
		}
		catch (Exception)
		{
		}
	}

	private async Task InitializeSignalRConnectionAsync()
	{
		try
		{
			await _signalRService.InitializeAsync();
		}
		catch (Exception ex)
		{
			_ = ex;
			await Task.Delay(5000);
			InitializeSignalRConnectionAsync();
		}
	}

	private async Task SendSystemInfoToServer()
	{
		try
		{
			SystemInfoDto systemInfoDto = SystemInfoCollector.Collect();
			if (_signalRService != null)
			{
				await _signalRService.SendSystemInfoAsync(systemInfoDto.IpAddress, systemInfoDto.CpuName, systemInfoDto.RamTotalMb, systemInfoDto.RamUsedMb, systemInfoDto.GpuName, systemInfoDto.DiskInfo, systemInfoDto.OsVersion, NetworkUtils.GetMacAddress());
			}
		}
		catch (Exception)
		{
		}
	}

	private void ShowPendingApprovalScreen()
	{
		if (PendingApprovalOverlay != null)
		{
			PendingApprovalOverlay.Visibility = Visibility.Visible;
		}
	}

	private void HidePendingApprovalScreen()
	{
		if (PendingApprovalOverlay != null)
		{
			PendingApprovalOverlay.Visibility = Visibility.Collapsed;
		}
	}

	private void ShowOfflineIndicator()
	{
		if (OfflineIndicator != null)
		{
			OfflineIndicator.Visibility = Visibility.Visible;
		}
	}

	private void HideOfflineIndicator()
	{
		if (OfflineIndicator != null)
		{
			OfflineIndicator.Visibility = Visibility.Collapsed;
		}
	}

	private string GetRussianStatus(string statusEng)
	{
		return statusEng?.ToLower() switch
		{
			"new" => "В очереди", 
			"processing" => "Готовится", 
			"ready" => "Готов", 
			"completed" => "Выдано", 
			"cancelled" => "Отменен", 
			_ => statusEng, 
		};
	}

	public void LaunchGame(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return;
		}
		try
		{
			ProcessUtils.StartGame(path, delegate
			{
				base.Dispatcher.Invoke(delegate
				{
					if (_isSessionActive)
					{
						WindowUtils.SendToBack(this);
					}
				});
			});
		}
		catch (Exception ex)
		{
			try
			{
				new AetherShell.Client.Windows.MessageBox(ex.Message, "Запуск игры").ShowDialog();
			}
			catch
			{
				System.Windows.MessageBox.Show(ex.Message, "Запуск игры");
			}
		}
	}

}
}
