using System.Windows;

namespace AetherShell.Client.Windows
{
    public partial class MessageBox : Window
    {
        // Результат выбора пользователя (true = OK/Yes, false = No/Cancel)
        public bool Result { get; private set; } = false;

        public MessageBox(string message, string title = "Внимание", bool isConfirmation = false)
        {
            InitializeComponent();

            TxtMessage.Text = message;
            TxtTitle.Text = title;

            if (isConfirmation)
            {
                // Если это вопрос, показываем две кнопки
                BtnYes.Content = "Да";
                BtnNo.Visibility = Visibility.Visible;
                BtnNo.Content = "Нет";
            }
            else
            {
                // Если просто инфо, только кнопка ОК
                BtnYes.Content = "OK";
                BtnNo.Visibility = Visibility.Collapsed;
            }

            // Позволяем перетаскивать окно мышкой
            this.MouseLeftButtonDown += (s, e) => this.DragMove();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.Close();
        }
    }
}