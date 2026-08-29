using System;
using System.Runtime.InteropServices; // Для работы с системными стилями
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;       // Для доступа к хендлу окна
using System.Windows.Media.Animation;

namespace AetherShell.Client
{
    public partial class NotificationWindow : Window
    {
        private static NotificationWindow _instance;

        private string _currentTimerMsg = null;
        private string _currentOrderMsg = null;
        private bool _isVisible = false;

        // === МАГИЯ ДЛЯ СКРЫТИЯ ИЗ ALT+TAB ===
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        // ======================================

        public NotificationWindow()
        {
            InitializeComponent();
        }

        // Переопределяем метод инициализации окна, чтобы внедрить стиль
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Получаем системный хендл (ID) нашего окна
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Получаем текущие стили и добавляем стиль ToolWindow
            // Это говорит Windows: "Это служебное окно, не показывай его в переключателе задач"
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            this.Left = (screenWidth - this.Width) / 2;
            this.Top = screenHeight - this.Height - 80;
        }

        public static void Reset()
        {
            if (_instance == null) return;
            _instance._currentTimerMsg = null;
            _instance._currentOrderMsg = null;
            _instance.UpdateUiState();
        }

        public static void UpdateTimer(string message)
        {
            EnsureInstance();
            _instance._currentTimerMsg = message;
            _instance.UpdateUiState();
        }

        public static void ClearTimer()
        {
            if (_instance == null) return;
            _instance._currentTimerMsg = null;
            _instance.UpdateUiState();
        }

        public static void UpdateOrder(string statusMessage, bool isCompleted)
        {
            EnsureInstance();

            if (isCompleted)
            {
                _instance._currentOrderMsg = statusMessage;
                _instance.UpdateUiState();

                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                timer.Tick += (s, e) =>
                {
                    _instance._currentOrderMsg = null;
                    _instance.UpdateUiState();
                    timer.Stop();
                };
                timer.Start();
            }
            else
            {
                _instance._currentOrderMsg = statusMessage;
                _instance.UpdateUiState();
            }
        }

        private static void EnsureInstance()
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new NotificationWindow();
                // Привязываем к главному окну, чтобы оно не перекрывало игры
                if (MainWindow.Instance != null)
                {
                    _instance.Owner = MainWindow.Instance;
                }
                _instance.Show();
            }
        }

        private void UpdateUiState()
        {
            bool hasTimer = !string.IsNullOrEmpty(_currentTimerMsg);
            bool hasOrder = !string.IsNullOrEmpty(_currentOrderMsg);

            TimerText.Text = _currentTimerMsg;
            OrderText.Text = _currentOrderMsg;

            TimerPanel.Visibility = hasTimer ? Visibility.Visible : Visibility.Collapsed;
            OrderPanel.Visibility = hasOrder ? Visibility.Visible : Visibility.Collapsed;

            if (hasTimer && hasOrder)
            {
                Divider.Visibility = Visibility.Visible;
                ColTimer.Width = new GridLength(1, GridUnitType.Star);
                ColOrder.Width = new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(TimerPanel, 0);
                Grid.SetColumnSpan(TimerPanel, 1);
                Grid.SetColumn(OrderPanel, 2);
                Grid.SetColumnSpan(OrderPanel, 1);
            }
            else if (hasTimer)
            {
                Divider.Visibility = Visibility.Collapsed;
                Grid.SetColumn(TimerPanel, 0);
                Grid.SetColumnSpan(TimerPanel, 3);
            }
            else if (hasOrder)
            {
                Divider.Visibility = Visibility.Collapsed;
                Grid.SetColumn(OrderPanel, 0);
                Grid.SetColumnSpan(OrderPanel, 3);
            }

            if (hasTimer || hasOrder)
            {
                if (!_isVisible) FadeIn();
            }
            else
            {
                if (_isVisible) FadeOut();
            }
        }

        private void FadeIn()
        {
            _isVisible = true;
            DoubleAnimation anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
            MainBorder.BeginAnimation(OpacityProperty, anim);
        }

        private void FadeOut()
        {
            _isVisible = false;
            DoubleAnimation anim = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
            MainBorder.BeginAnimation(OpacityProperty, anim);
        }
    }
}