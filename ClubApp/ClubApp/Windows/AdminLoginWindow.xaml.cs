using System;
using System.Windows;
using System.Windows.Input;
using AetherShell.Client.Services;

namespace AetherShell.Client.Windows
{
    /// <summary>
    /// Вход в режим администратора по Ctrl+Alt+A. Проверку делает сервер, поэтому
    /// в зале не нужен общий пароль: видно, кто именно открывал панель.
    /// </summary>
    public partial class AdminLoginWindow : Window
    {
        private readonly AdminApiService _api;

        /// <summary>Заполняется только при успешном входе.</summary>
        public AdminSession Session { get; private set; }

        public AdminLoginWindow(AdminApiService api)
        {
            InitializeComponent();
            _api = api;
            LoginBox.Focus();
            this.MouseLeftButtonDown += (s, e) => DragMove();
        }

        private async void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var login = (LoginBox.Text ?? "").Trim();
            var password = PasswordBox.Password ?? "";

            if (login.Length == 0 || password.Length == 0)
            {
                ShowError("Введите логин и пароль.");
                return;
            }

            BtnOk.IsEnabled = false;
            try
            {
                Session = await _api.LoginAsync(login, password);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
            finally
            {
                BtnOk.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Field_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnOk_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancel_Click(sender, e);
                e.Handled = true;
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
