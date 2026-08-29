using System;
using System.Windows;
using AetherShell.Client.Services;
using AetherShell.Client.Utils;

namespace AetherShell.Client.Windows
{
    /// <summary>
    /// Действия сотрудника на конкретном ПК: разблокировать рабочий стол, выдать
    /// или снять время, пополнить баланс, перезагрузить машину, выйти из шелла.
    ///
    /// Управление сессией и балансом идёт через сервер, а не напрямую по месту:
    /// так каждое действие попадает в журнал и видно, кто его сделал.
    /// </summary>
    public partial class AdminPanelWindow : Window
    {
        private readonly AdminApiService _api;
        private readonly AdminSession _session;
        private readonly MainWindow _shell;

        public AdminPanelWindow(AdminApiService api, AdminSession session, MainWindow shell)
        {
            InitializeComponent();

            _api = api;
            _session = session;
            _shell = shell;

            this.MouseLeftButtonDown += (s, e) => DragMove();

            HeaderInfo.Text = $"{session.Username} · {session.Role} · ПК {shell.MyPcId}";

            // Питанием и залом целиком распоряжаются старшие смены.
            var isSenior = session.Role == "Senior" || session.Role == "Super";
            BtnReboot.IsEnabled = isSenior;
            BtnShutdown.IsEnabled = isSenior;
            BtnExitShell.IsEnabled = isSenior;

            UpdateDesktopButtons();
        }

        private void UpdateDesktopButtons()
        {
            BtnUnlockDesktop.IsEnabled = !_shell.IsAdminDesktopUnlocked;
            BtnLockDesktop.IsEnabled = _shell.IsAdminDesktopUnlocked;
        }

        private void BtnUnlockDesktop_Click(object sender, RoutedEventArgs e)
        {
            _shell.EnterAdminDesktopMode();
            UpdateDesktopButtons();
            Status("Рабочий стол разблокирован. Не забудьте вернуть блокировку.");
        }

        private void BtnLockDesktop_Click(object sender, RoutedEventArgs e)
        {
            _shell.LeaveAdminDesktopMode();
            UpdateDesktopButtons();
            Status("Блокировка возвращена.");
        }

        private void BtnTaskManager_Click(object sender, RoutedEventArgs e)
        {
            // Диспетчер запрещён политикой, пока действуют ограничения, поэтому
            // сначала снимаем их — иначе Windows просто откажет.
            _shell.EnterAdminDesktopMode();
            UpdateDesktopButtons();

            if (ProcessUtils.OpenTaskManager()) Status("Диспетчер задач открыт.");
            else Status("Не удалось открыть диспетчер задач.");
        }

        private void BtnExplorer_Click(object sender, RoutedEventArgs e)
        {
            _shell.EnterAdminDesktopMode();
            UpdateDesktopButtons();

            if (ProcessUtils.OpenExplorer()) Status("Проводник открыт.");
            else Status("Не удалось открыть проводник.");
        }

        private async void BtnStartSession_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse((MinutesBox.Text ?? "").Trim(), out var minutes) || minutes <= 0)
            {
                Status("Укажите количество минут числом больше нуля.");
                return;
            }

            await RunAsync(() => _api.StartSessionAsync(_shell.MyPcId, minutes),
                $"Выдано {minutes} мин.");
        }

        private async void BtnStopSession_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(() => _api.StopSessionAsync(_shell.MyPcId),
                "Сессия завершена, остаток времени сохранён на аккаунт.");
        }

        private async void BtnTopUp_Click(object sender, RoutedEventArgs e)
        {
            var username = _shell.CurrentUsername;
            if (string.IsNullOrEmpty(username))
            {
                Status("На этом ПК сейчас никто не вошёл — пополнять нечего.");
                return;
            }

            if (!decimal.TryParse((AmountBox.Text ?? "").Trim(), out var amount) || amount <= 0)
            {
                Status("Укажите сумму числом больше нуля.");
                return;
            }

            await RunAsync(() => _api.TopUpAsync(username, amount),
                $"Баланс {username} пополнен на {amount:N0} ₸.");
        }

        private async void BtnReboot_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Перезагрузить этот компьютер?")) return;
            await RunAsync(() => _api.RebootAsync(_shell.MyPcId), "Команда перезагрузки отправлена.");
        }

        private async void BtnShutdown_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Выключить этот компьютер?")) return;
            await RunAsync(() => _api.ShutdownAsync(_shell.MyPcId), "Команда выключения отправлена.");
        }

        private void BtnExitShell_Click(object sender, RoutedEventArgs e)
        {
            if (!Confirm("Закрыть шелл на этом ПК?")) return;

            Close();
            _shell.ExitShell();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private async System.Threading.Tasks.Task RunAsync(Func<System.Threading.Tasks.Task> action, string successMessage)
        {
            IsEnabled = false;
            try
            {
                await action();
                Status(successMessage);
            }
            catch (Exception ex)
            {
                Status("Ошибка: " + ex.Message);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private bool Confirm(string question)
        {
            var dlg = new MessageBox(question, "Подтверждение", true) { Owner = this };
            dlg.ShowDialog();
            return dlg.Result;
        }

        private void Status(string message) => StatusText.Text = message;
    }
}
