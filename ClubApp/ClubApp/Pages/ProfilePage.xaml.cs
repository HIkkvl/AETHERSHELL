using AetherShell.Client.Models;
using AetherShell.Client.Utils;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AetherShell.Client.Pages
{
    public partial class ProfilePage : Page
    {
        public ProfilePage()
        {
            InitializeComponent();
            Loaded += async (_, __) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            var win = MainWindow.Instance;
            if (win == null) return;

            TxtUsername.Text = win.CurrentUsername ?? "—";
            AvatarLetter.Text = string.IsNullOrEmpty(win.CurrentUsername)
                ? "?"
                : win.CurrentUsername.Substring(0, 1).ToUpperInvariant();

            ApplyAvatar(win.CurrentAvatarUrl);

            try
            {
                var me = await win.ApiService.GetMyProfileAsync();
                if (me == null) return;
                TxtUsername.Text = me.username ?? TxtUsername.Text;
                TxtEmail.Text = string.IsNullOrWhiteSpace(me.email) ? "—" : me.email;
                if (!string.IsNullOrEmpty(me.avatarUrl))
                {
                    win.SetAvatarUrl(me.avatarUrl);
                    ApplyAvatar(me.avatarUrl);
                }
            }
            catch (Exception ex)
            {
                PasswordMsg.Foreground = Brushes.OrangeRed;
                PasswordMsg.Text = ex.Message;
            }
        }

        private void ApplyAvatar(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                AvatarPreview.Source = null;
                AvatarLetter.Visibility = Visibility.Visible;
                return;
            }

            var converter = new ImageUrlConverter();
            var bmp = converter.Convert(url, typeof(BitmapImage), null, null) as BitmapImage;
            AvatarPreview.Source = bmp;
            AvatarLetter.Visibility = bmp == null ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnPickAvatar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp",
                Title = "Выберите аватар"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var url = await MainWindow.Instance.ApiService.UploadAvatarAsync(dlg.FileName);
                MainWindow.Instance.SetAvatarUrl(url);
                ApplyAvatar(url);
                PasswordMsg.Foreground = Brushes.LightGreen;
                PasswordMsg.Text = "Аватар обновлён";
            }
            catch (Exception ex)
            {
                PasswordMsg.Foreground = Brushes.OrangeRed;
                PasswordMsg.Text = ex.Message;
            }
        }

        private async void BtnChangePassword_Click(object sender, RoutedEventArgs e)
        {
            PasswordMsg.Text = "";
            if (BoxNewPassword.Password.Length < 6)
            {
                PasswordMsg.Foreground = Brushes.OrangeRed;
                PasswordMsg.Text = "Новый пароль — минимум 6 символов";
                return;
            }
            if (BoxNewPassword.Password != BoxNewPassword2.Password)
            {
                PasswordMsg.Foreground = Brushes.OrangeRed;
                PasswordMsg.Text = "Пароли не совпадают";
                return;
            }

            try
            {
                await MainWindow.Instance.ApiService.ChangeClientPasswordAsync(
                    BoxCurrentPassword.Password,
                    BoxNewPassword.Password);
                BoxCurrentPassword.Password = "";
                BoxNewPassword.Password = "";
                BoxNewPassword2.Password = "";
                PasswordMsg.Foreground = Brushes.LightGreen;
                PasswordMsg.Text = "Пароль изменён";
            }
            catch (Exception ex)
            {
                PasswordMsg.Foreground = Brushes.OrangeRed;
                PasswordMsg.Text = ex.Message;
            }
        }
    }
}
