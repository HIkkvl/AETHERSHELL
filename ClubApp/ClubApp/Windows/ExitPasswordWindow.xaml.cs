using System.Windows;
using System.Windows.Input;

namespace AetherShell.Client.Windows
{
    public partial class ExitPasswordWindow : Window
    {
        public string? EnteredPassword { get; private set; }

        public ExitPasswordWindow()
        {
            InitializeComponent();
            PasswordBox.Focus();
            this.MouseLeftButtonDown += (s, e) => DragMove();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = null;
            DialogResult = false;
            Close();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnOk_Click(sender, e);
                e.Handled = true;
            }
            if (e.Key == Key.Escape)
            {
                BtnCancel_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
