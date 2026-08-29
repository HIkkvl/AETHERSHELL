using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;
using AetherShell.Client.Utils;

namespace AetherShell.Client
{
    public partial class ShellTaskbarWindow : Window
    {
        public const double BarHeight = 48;

        private static ShellTaskbarWindow _instance;

        private MainWindow _shell;
        private readonly DispatcherTimer _refreshTimer;
        private readonly ObservableCollection<RunningWindowItem> _apps = new ObservableCollection<RunningWindowItem>();
        private bool _volumeSliderUpdating;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public ShellTaskbarWindow()
        {
            InitializeComponent();
            AppsList.ItemsSource = _apps;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, __) => RefreshAll();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionBar();
            RefreshVolumeUi();
        }

        public static void ShowForSession(MainWindow shell)
        {
            if (shell == null) return;

            if (_instance == null)
                _instance = new ShellTaskbarWindow();

            _instance._shell = shell;
            _instance.PositionBar();
            _instance.Show();
            _instance.Topmost = true;
            shell.ApplyTaskbarInset(true);
            _instance.RefreshAll();
            if (!_instance._refreshTimer.IsEnabled)
                _instance._refreshTimer.Start();
        }

        public static void HideForSession()
        {
            if (_instance == null) return;
            _instance._refreshTimer.Stop();
            _instance._apps.Clear();
            if (_instance.VolumePopup != null)
                _instance.VolumePopup.IsOpen = false;
            if (_instance._shell != null)
                _instance._shell.ApplyTaskbarInset(false);
            _instance._shell = null;
            _instance.Hide();
        }

        private void PositionBar()
        {
            Width = SystemParameters.PrimaryScreenWidth;
            Height = BarHeight;
            Left = 0;
            Top = SystemParameters.PrimaryScreenHeight - Height;
        }

        private void RefreshAll()
        {
            try
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm");
                BtnKeyboardLayout.Content = KeyboardLayoutHelper.GetCurrentLayoutLabel();
                RefreshApps();
                if (VolumePopup == null || !VolumePopup.IsOpen)
                    RefreshVolumeUi();
                if (!Topmost) Topmost = true;
            }
            catch { }
        }

        private void RefreshApps()
        {
            IntPtr selfHwnd = IntPtr.Zero;
            IntPtr shellHwnd = IntPtr.Zero;
            try { selfHwnd = new WindowInteropHelper(this).Handle; } catch { }
            try
            {
                if (_shell != null)
                    shellHwnd = new WindowInteropHelper(_shell).Handle;
            }
            catch { }

            var fresh = WindowEnumerator.GetRunningWindows(selfHwnd, shellHwnd);

            for (int i = _apps.Count - 1; i >= 0; i--)
            {
                if (!fresh.Any(f => f.Hwnd == _apps[i].Hwnd))
                    _apps.RemoveAt(i);
            }

            foreach (var item in fresh)
            {
                var existing = _apps.FirstOrDefault(a => a.Hwnd == item.Hwnd);
                if (existing == null)
                {
                    _apps.Add(item);
                }
                else
                {
                    existing.Title = item.Title;
                    existing.IsActive = item.IsActive;
                    if (existing.Icon == null && item.Icon != null)
                        existing.Icon = item.Icon;
                }
            }
        }

        private void RefreshVolumeUi()
        {
            bool muted = VolumeHelper.IsMuted();
            float level = VolumeHelper.GetLevel();
            int percent = (int)Math.Round(level * 100);
            bool showMuted = muted || percent == 0;

            if (IconVolume != null && IconMuted != null)
            {
                IconVolume.Visibility = showMuted ? Visibility.Collapsed : Visibility.Visible;
                IconMuted.Visibility = showMuted ? Visibility.Visible : Visibility.Collapsed;
            }

            BtnVolume.ToolTip = muted
                ? "Звук выключен (ПКМ — включить)"
                : $"Громкость {percent}% (ПКМ — выкл.)";

            if (VolumePercentText != null)
                VolumePercentText.Text = muted ? "Выкл" : (percent + "%");

            if (VolumeSlider != null && (VolumePopup == null || !VolumePopup.IsOpen))
            {
                _volumeSliderUpdating = true;
                VolumeSlider.Value = percent;
                _volumeSliderUpdating = false;
            }

            if (MuteGlyph != null)
                MuteGlyph.Text = muted ? "🔇" : "🔊";
        }

        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RunningWindowItem item)
            {
                if (_shell != null)
                {
                    _shell.Topmost = false;
                    WindowUtils.SendToBack(_shell);
                }
                WindowEnumerator.ActivateWindow(item.Hwnd);
                Topmost = true;
                RefreshAll();
            }
        }

        private void KeyboardLayout_Click(object sender, RoutedEventArgs e)
        {
            KeyboardLayoutHelper.CycleNextLayout();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BtnKeyboardLayout.Content = KeyboardLayoutHelper.GetCurrentLayoutLabel();
            }), DispatcherPriority.Background);
        }

        private void Volume_Click(object sender, RoutedEventArgs e)
        {
            RefreshVolumeUi();
            _volumeSliderUpdating = true;
            VolumeSlider.Value = Math.Round(VolumeHelper.GetLevel() * 100);
            _volumeSliderUpdating = false;
            VolumePopup.IsOpen = !VolumePopup.IsOpen;
        }

        private void Volume_RightClick(object sender, MouseButtonEventArgs e)
        {
            VolumeHelper.ToggleMute();
            RefreshVolumeUi();
            e.Handled = true;
        }

        private void Volume_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            VolumeHelper.Adjust(e.Delta > 0 ? 0.05f : -0.05f);
            RefreshVolumeUi();
            if (VolumePopup.IsOpen)
            {
                _volumeSliderUpdating = true;
                VolumeSlider.Value = Math.Round(VolumeHelper.GetLevel() * 100);
                _volumeSliderUpdating = false;
            }
            e.Handled = true;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_volumeSliderUpdating) return;
            VolumeHelper.SetLevel((float)(e.NewValue / 100.0));
            RefreshVolumeUi();
        }

        private void MuteToggle_Click(object sender, RoutedEventArgs e)
        {
            VolumeHelper.ToggleMute();
            RefreshVolumeUi();
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    }
}
