using System.Windows;
using System.Windows.Controls;
using AetherShell.Client.Models;
using static AetherShell.Client.Utils.WindowUtils;

namespace AetherShell.Client.Pages
{
    public partial class TariffsPage : Page
    {
        public TariffsPage()
        {
            InitializeComponent();
            LoadTariffs();
        }

        public void RefreshFromServer() => LoadTariffs();

        private async void LoadTariffs()
        {
            try
            {
                var tariffs = await MainWindow.Instance.ApiService.GetTariffsAsync();
                TariffsList.ItemsSource = tariffs;
            }
            catch { }
        }

        private async void Tariff_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TariffItem tariff)
            {
                int tariffId = tariff.Id;
                decimal originalPrice = tariff.Price;

                // === РАСЧЕТ СКИДКИ ===
                int discount = MainWindow.Instance.GetCurrentDiscountPercent();
                decimal finalPrice = originalPrice;
                if (discount > 0)
                {
                    finalPrice = originalPrice - (originalPrice * discount / 100);
                }

                // Формируем красивое сообщение
                string message = discount > 0
                    ? $"Купить тариф '{tariff.Name}'?\n\nЦена: {originalPrice:N0} ₸\nСкидка {discount}%: -{(originalPrice - finalPrice):N0} ₸\nИтого: {finalPrice:N0} ₸"
                    : $"Купить тариф '{tariff.Name}' за {originalPrice:N0} ₸?";

                if (Msg.Ask(message, "Подтверждение"))
                {
                    try
                    {
                        var request = new BuyTariffRequest
                        {
                            Username = MainWindow.Instance.CurrentUsername,
                            MacAddress = MainWindow.Instance.MyPcId,
                            TariffId = tariffId
                        };

                        var result = await MainWindow.Instance.ApiService.BuyTariffAsync(request);
                        MainWindow.Instance.UpdateBalanceUI(result.newBalance);

                        // Добавляем фактически потраченную сумму в прогресс
                        MainWindow.Instance.AddSpentAmount(finalPrice);

                        MainWindow.Instance.StartSession();
                        Msg.Show("Успешно!");
                    }
                    catch (System.Exception ex)
                    {
                        Msg.Show("Ошибка: " + ex.Message);
                    }
                }
            }
        }
    }
}