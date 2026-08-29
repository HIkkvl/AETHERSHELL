using AetherShell.Client.Models;
using AetherShell.Client.Services;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using static AetherShell.Client.Utils.WindowUtils;

namespace AetherShell.Client.Pages
{
    public partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            InitializeComponent();
            LoadData();
        }

        public void RefreshFromServer() => LoadData();

        private async void LoadData()
        {
            var api = MainWindow.Instance.ApiService;
            try
            {
                var allApps = await api.GetAppsAsync();
                if (allApps != null)
                {
                    LocalAppOverrides.ApplyTo(allApps);
                    var gamesOnly = allApps.Where(x => x.Category == "Game").Take(10).ToList();
                    NewGamesList.ItemsSource = gamesOnly;
                }

                var tariffs = await api.GetTariffsAsync();
                if (tariffs != null)
                {
                    PopularTariffsList.ItemsSource = tariffs.Take(5).ToList();
                }
            }
            catch { }
        }

        private void Game_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AppItem app)
            {
                MainWindow.Instance.LaunchGame(app.ExePath);
            }
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