using AetherShell.Client.Models;
using AetherShell.Client.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static AetherShell.Client.Utils.WindowUtils;

namespace AetherShell.Client.Pages
{
    public partial class FoodPage : Page
    {
        private List<ProductItem> _allProducts = new List<ProductItem>();
        private string _selectedCategory = "All";
        private string _searchText = "";

        public FoodPage()
        {
            InitializeComponent();
            this.Loaded += (s, e) =>
            {
                UpdateCartButton();
                if (CartPopup != null) CartPopup.Visibility = Visibility.Collapsed;
                if (CartButton != null) CartButton.Visibility = Visibility.Visible;
                LoadProducts();
            };
        }

        public void RefreshFromServer() => LoadProducts();

        private async void LoadProducts()
        {
            try
            {
                _allProducts = await MainWindow.Instance.ApiService.GetProductsAsync() ?? new List<ProductItem>();
                GenerateCategoryButtons();
                ApplyFilters();
            }
            catch { }
        }

        private void GenerateCategoryButtons()
        {
            CategoriesPanel.Children.Clear();
            CreateCategoryButton("Все", "All", true);
            var uniqueCategories = _allProducts.Select(p => p.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            foreach (var category in uniqueCategories) CreateCategoryButton(category, category, false);
        }

        private void CreateCategoryButton(string label, string tag, bool isActive)
        {
            var btn = new Button { Content = label, Tag = tag, Style = (Style)FindResource("CategoryPillStyle") };
            if (isActive) { btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#252535"); btn.Foreground = Brushes.White; }
            btn.Click += FilterCategory_Click;
            CategoriesPanel.Children.Add(btn);
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                if (e.Delta > 0) { scrollViewer.LineLeft(); scrollViewer.LineLeft(); scrollViewer.LineLeft(); }
                else { scrollViewer.LineRight(); scrollViewer.LineRight(); scrollViewer.LineRight(); }
                e.Handled = true;
            }
        }

        private void ApplyFilters()
        {
            if (ProductsList == null) return;
            var filtered = _allProducts.AsEnumerable();
            if (_selectedCategory != "All") filtered = filtered.Where(p => p.Category == _selectedCategory);
            if (!string.IsNullOrWhiteSpace(_searchText)) filtered = filtered.Where(p => p.Name.ToLower().Contains(_searchText.ToLower()));
            ProductsList.ItemsSource = filtered.ToList();
        }

        private void FilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedBtn && clickedBtn.Tag != null)
            {
                _selectedCategory = clickedBtn.Tag.ToString();
                foreach (var child in CategoriesPanel.Children)
                {
                    if (child is Button btn) { btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#0F0F1A"); btn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#AAAAAA"); }
                }
                clickedBtn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#252535"); clickedBtn.Foreground = Brushes.White;
                ApplyFilters();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) { _searchText = SearchTextBox.Text; ApplyFilters(); }

        private void AddToCart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProductItem product)
            {
                MainWindow.Instance.AddToCart(product);
                UpdateCartButton();
                if (CartPopup.Visibility == Visibility.Visible) UpdateCartPopupUI();
            }
        }

        private void UpdateCartButton()
        {
            var cartCount = MainWindow.Instance.Cart?.Sum(c => c.Quantity) ?? 0;
            var cartButton = this.FindName("CartButton") as Button;
            if (cartButton != null)
            {
                var textBlock = cartButton.Template?.FindName("BtnText", cartButton) as TextBlock;
                if (textBlock != null) textBlock.Text = cartCount > 0 ? $"Корзина ({cartCount})" : "Корзина";
            }
        }

        private void CartButton_Click(object sender, RoutedEventArgs e) { UpdateCartPopupUI(); if (CartPopup != null) CartPopup.Visibility = Visibility.Visible; if (CartButton != null) CartButton.Visibility = Visibility.Collapsed; }
        private void CloseCart_Click(object sender, RoutedEventArgs e) { if (CartPopup != null) CartPopup.Visibility = Visibility.Collapsed; if (CartButton != null) CartButton.Visibility = Visibility.Visible; }

        private void UpdateCartPopupUI()
        {
            var cartListBox = this.FindName("CartListBox") as ItemsControl;
            if (cartListBox != null) { cartListBox.ItemsSource = null; cartListBox.ItemsSource = MainWindow.Instance.Cart ?? new List<CartItem>(); }

            var total = MainWindow.Instance.Cart?.Sum(x => x.TotalPrice) ?? 0;

            // --- ТУТ МОЖНО ТОЖЕ ПОКАЗАТЬ СКИДКУ, НО ЛУЧШЕ В CHECKOUT ---
            var totalText = this.FindName("TotalText") as TextBlock;
            if (totalText != null) totalText.Text = $"Итого: {total:N0} тг";
        }

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CartItem item)
            {
                MainWindow.Instance.Cart?.Remove(item);
                UpdateCartPopupUI(); UpdateCartButton();
                if (MainWindow.Instance.Cart.Count == 0) CloseCart_Click(null, null);
            }
        }

        private async void Checkout_Click(object sender, RoutedEventArgs e)
        {
            var cart = MainWindow.Instance.Cart;
            if (cart == null || !cart.Any()) { Msg.Show("Корзина пуста", "Ошибка"); return; }

            decimal originalTotal = cart.Sum(x => x.TotalPrice);

            // === РАСЧЕТ СКИДКИ ===
            int discount = MainWindow.Instance.GetCurrentDiscountPercent();
            decimal finalTotal = originalTotal;
            if (discount > 0)
            {
                finalTotal = originalTotal - (originalTotal * discount / 100);
            }

            string msg = discount > 0
                ? $"Оформить заказ?\n\nСумма: {originalTotal:N0} ₸\nСкидка {discount}%: -{(originalTotal - finalTotal):N0} ₸\nК оплате: {finalTotal:N0} ₸"
                : $"Оформить заказ на сумму {originalTotal:N0} тг?";

            if (Msg.Ask(msg, "Подтверждение заказа"))
            {
                try
                {
                    var orderDto = new CreateOrderDto
                    {
                        Username = MainWindow.Instance.CurrentUsername,
                        MacAddress = MainWindow.Instance.MyPcId,
                        Items = cart.Select(c => new OrderItemDto { ProductId = c.Product.Id, Quantity = c.Quantity }).ToList()
                    };

                    var orderResult = await MainWindow.Instance.ApiService.CreateOrderAsync(orderDto);
                    MainWindow.Instance.UpdateBalanceUI(orderResult.newBalance);

                    // Обновляем лояльность
                    MainWindow.Instance.AddSpentAmount(finalTotal);

                    var newOrder = new UserOrder { Id = orderResult.OrderId, Status = "New" };
                    MainWindow.Instance.ActiveOrders.Add(newOrder);

                    cart.Clear(); UpdateCartPopupUI(); UpdateCartButton(); CloseCart_Click(null, null);
                    NotificationWindow.UpdateOrder($"Заказ #{newOrder.Id}: Обработка", false);
                    Msg.Show("Заказ успешно оформлен!", "Успех");
                }
                catch (System.Exception ex) { Msg.Show($"Ошибка при оформлении: {ex.Message}", "Ошибка"); }
            }
        }
    }
}