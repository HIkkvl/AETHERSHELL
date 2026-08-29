using AetherShell.Client.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AetherShell.Client.Pages
{
    public partial class GamesPage : Page
    {
        private string _mainCategory; // "Game" или "Application"
        private List<AppItem> _allLoadedApps = new List<AppItem>();

        // Текущие фильтры
        private string _selectedGenre = "All";
        private string _searchText = "";

        // Словарь для перевода жанров (База -> Экран)
        private readonly Dictionary<string, string> _genreTranslations = new Dictionary<string, string>
        {
            // --- ИГРЫ ---
            { "Shooter", "Шутеры" },
            { "Racing", "Гонки" },
            { "RPG", "RPG" },
            { "Strategy", "Стратегии" },
            { "Sports", "Спорт" },
            { "MOBA", "MOBA" },
            { "Simulation", "Симуляторы" },
            { "Fighting", "Файтинги" },
            { "Horror", "Хорроры" },
            { "Survival", "Выживание" },
            { "Action", "Экшен" },
            { "Arcade", "Аркады" },
            { "Roguelike", "Рогалики" },
            { "Other", "Разное" },

            // --- ПРИЛОЖЕНИЯ ---
            { "Browser", "Браузеры" },
            { "Social", "Общение" },
            { "Launcher", "Лаунчеры" },
            { "Tool", "Инструменты" },
            { "Office", "Офис" },
            { "Media", "Мультимедиа" },
            { "System", "Система" },
            { "Education", "Учеба" }
        };

        public GamesPage(string category)
        {
            InitializeComponent();
            _mainCategory = category;

            if (_mainCategory == "Game") PageTitle.Text = "Игры";
            else if (_mainCategory == "Application") PageTitle.Text = "Приложения";
            else PageTitle.Text = _mainCategory;

            LoadData();
        }

        /// <summary>Перезагрузка списка после изменений в админ-панели (SignalR AppsUpdated).</summary>
        public void RefreshFromServer() => LoadData();

        private async void LoadData()
        {
            var api = MainWindow.Instance.ApiService;
            try
            {
                var allApps = await api.GetAppsAsync();

                if (allApps != null)
                {
                    // 1. Сохраняем только те, что подходят под текущий раздел (Игры или Софт)
                    _allLoadedApps = allApps.Where(x => x.Category == _mainCategory).ToList();

                    // 2. Генерируем кнопки жанров (ТЕПЕРЬ ДЛЯ ВСЕХ КАТЕГОРИЙ)
                    // Мы убрали проверку "if (_mainCategory == Game)", теперь это работает и для софта
                    GenerateGenreButtons();

                    // 3. Показываем список
                    ApplyFilters();
                }
            }
            catch { }
        }

        private void GenerateGenreButtons()
        {
            CategoriesPanel.Children.Clear();

            // --- 1. Кнопка "Все" (Всегда первая) ---
            CreateCategoryButton("Все", "All", true);

            // --- 2. Собираем уникальные жанры из загруженных приложений ---
            var uniqueGenres = _allLoadedApps
                                .Select(x => x.Genre)
                                .Where(g => !string.IsNullOrEmpty(g)) // Исключаем пустые
                                .Distinct()
                                .ToList();

            // --- 3. Создаем кнопки для каждого найденного жанра ---
            foreach (var genreKey in uniqueGenres)
            {
                // Пытаемся перевести, если нет перевода — показываем как есть (английский ключ)
                string label = _genreTranslations.ContainsKey(genreKey) ? _genreTranslations[genreKey] : genreKey;

                CreateCategoryButton(label, genreKey, false);
            }
        }

        private void CreateCategoryButton(string label, string tag, bool isActive)
        {
            var btn = new Button
            {
                Content = label,
                Tag = tag,
                Style = (Style)FindResource("CategorySideItemStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (isActive)
            {
                btn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#33FF5ED7");
                btn.Foreground = Brushes.White;
            }

            btn.Click += FilterCategory_Click;
            CategoriesPanel.Children.Add(btn);
        }

        // ================== ЛОГИКА ФИЛЬТРАЦИИ ==================

        private void ApplyFilters()
        {
            var filtered = _allLoadedApps.AsEnumerable();

            // 1. Фильтр по жанру
            if (_selectedGenre != "All")
            {
                filtered = filtered.Where(x => x.Genre == _selectedGenre);
            }

            // 2. Фильтр по тексту
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string searchLower = _searchText.ToLower();
                filtered = filtered.Where(x => x.Title.ToLower().Contains(searchLower));
            }

            GamesList.ItemsSource = filtered.ToList();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = TxtSearch.Text;
            ApplyFilters();
        }

        private void FilterCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedBtn && clickedBtn.Tag != null)
            {
                _selectedGenre = clickedBtn.Tag.ToString();

                // Визуальное обновление кнопок
                foreach (var child in CategoriesPanel.Children)
                {
                    if (child is Button btn)
                    {
                        btn.Background = Brushes.Transparent;
                        btn.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#B8A8B8");
                    }
                }

                clickedBtn.Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#33FF5ED7");
                clickedBtn.Foreground = Brushes.White;

                ApplyFilters();
            }
        }
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Если крутим колесико вверх/вниз -> список едет влево/вправо
                if (e.Delta > 0)
                {
                    scrollViewer.LineLeft();
                    scrollViewer.LineLeft(); // Ускоряем прокрутку
                    scrollViewer.LineLeft();
                }
                else
                {
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                    scrollViewer.LineRight();
                }
                e.Handled = true; // Отменяем стандартное поведение (вертикальный скролл)
            }
        }

        private void Game_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AppItem app)
            {
                MainWindow.Instance.LaunchGame(app.ExePath);
            }
        }
    }
}