using AetherShell.Client.Models;
using AetherShell.Client.Services;
using AetherShell.Client.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AetherShell.Client.Pages
{
    public partial class GamesPage : Page, INotifyPropertyChanged
    {
        private string _mainCategory;
        private List<AppItem> _allLoadedApps = new List<AppItem>();
        private string _selectedGenre = "All";
        private string _searchText = "";
        private bool _showEditTools;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool ShowEditTools
        {
            get => _showEditTools;
            set
            {
                if (_showEditTools == value) return;
                _showEditTools = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowEditTools)));
            }
        }

        private readonly Dictionary<string, string> _genreTranslations = new Dictionary<string, string>
        {
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
            DataContext = this;
            _mainCategory = category;

            if (MainWindow.Instance != null)
            {
                ShowEditTools = MainWindow.Instance.IsHighAccessMode;
                MainWindow.Instance.HighAccessChanged += OnHighAccessChanged;
                MainWindow.Instance.SetCategoryRailVisible(true, _mainCategory);
            }

            Unloaded += (_, __) =>
            {
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.HighAccessChanged -= OnHighAccessChanged;
                    ClearHostCategories();
                }
            };

            LoadData();
        }

        private StackPanel CategoriesPanel => MainWindow.Instance?.ShellCategoriesPanel;

        private void ClearHostCategories()
        {
            var panel = CategoriesPanel;
            if (panel != null) panel.Children.Clear();
        }

        private void OnHighAccessChanged()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowEditTools = MainWindow.Instance != null && MainWindow.Instance.IsHighAccessMode;
            }));
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
                    _allLoadedApps = allApps.Where(x => x.Category == _mainCategory).ToList();
                    GenerateGenreButtons();
                    ApplyFilters();
                }
            }
            catch { }
        }

        private void GenerateGenreButtons()
        {
            var panel = CategoriesPanel;
            if (panel == null) return;
            panel.Children.Clear();
            CreateCategoryButton("Все", "All", true);

            var uniqueGenres = _allLoadedApps
                .Select(x => x.Genre)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .ToList();

            foreach (var genreKey in uniqueGenres)
            {
                string label = _genreTranslations.ContainsKey(genreKey) ? _genreTranslations[genreKey] : genreKey;
                CreateCategoryButton(label, genreKey, false);
            }
        }

        private void CreateCategoryButton(string label, string tag, bool isActive)
        {
            var panel = CategoriesPanel;
            if (panel == null) return;

            bool isAll = string.Equals(tag, "All", StringComparison.OrdinalIgnoreCase);
            var mute = (Brush)new BrushConverter().ConvertFrom("#A299B8");
            var content = new StackPanel { Orientation = Orientation.Horizontal };

            if (isAll)
            {
                content.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconSliders"),
                    Stroke = isActive ? Brushes.White : mute,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Fill = Brushes.Transparent,
                    Width = 18,
                    Height = 18,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                content.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconZap"),
                    Fill = isActive ? Brushes.White : mute,
                    Stroke = null,
                    Width = 18,
                    Height = 18,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = isActive ? Brushes.White : mute,
                VerticalAlignment = VerticalAlignment.Center
            });

            var btn = new Button
            {
                Content = content,
                Tag = tag,
                Style = (Style)FindResource("CategorySideItemStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1)
            };

            btn.Click += FilterCategory_Click;
            panel.Children.Add(btn);
        }

        private void ApplyFilters()
        {
            var filtered = _allLoadedApps.AsEnumerable();
            if (_selectedGenre != "All")
                filtered = filtered.Where(x => x.Genre == _selectedGenre);
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string searchLower = _searchText.ToLower();
                filtered = filtered.Where(x => x.Title != null && x.Title.ToLower().Contains(searchLower));
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
                var panel = CategoriesPanel;
                if (panel == null) return;
                var mute = (Brush)new BrushConverter().ConvertFrom("#A299B8");
                foreach (var child in panel.Children)
                {
                    if (child is Button btn && btn.Content is StackPanel sp)
                    {
                        btn.Background = Brushes.Transparent;
                        btn.BorderBrush = Brushes.Transparent;
                        foreach (var el in sp.Children)
                        {
                            if (el is System.Windows.Shapes.Path p)
                            {
                                if (p.Stroke != null && p.Stroke != Brushes.Transparent)
                                    p.Stroke = mute;
                                else
                                    p.Fill = mute;
                            }
                            else if (el is TextBlock tb)
                            {
                                tb.Foreground = mute;
                                tb.FontWeight = FontWeights.Medium;
                            }
                        }
                    }
                }
                if (clickedBtn.Content is StackPanel activeSp)
                {
                    foreach (var el in activeSp.Children)
                    {
                        if (el is System.Windows.Shapes.Path p)
                        {
                            if (p.Stroke != null && p.Stroke != Brushes.Transparent)
                                p.Stroke = Brushes.White;
                            else
                                p.Fill = Brushes.White;
                        }
                        else if (el is TextBlock tb)
                        {
                            tb.Foreground = Brushes.White;
                            tb.FontWeight = FontWeights.SemiBold;
                        }
                    }
                }
                ApplyFilters();
            }
        }

        private void Game_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AppItem app)
                MainWindow.Instance.LaunchGame(app.ExePath);
        }

        private void EditImage_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!(sender is Button btn) || !(btn.Tag is AppItem app)) return;
            if (MainWindow.Instance == null || !MainWindow.Instance.IsHighAccessMode) return;

            var dlg = new OpenFileDialog
            {
                Title = "Картинка для «" + app.Title + "»",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Все файлы|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string destDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AetherShell", "app-images");
                Directory.CreateDirectory(destDir);
                string dest = Path.Combine(destDir, "app-" + app.Id + Path.GetExtension(dlg.FileName));
                File.Copy(dlg.FileName, dest, true);
                LocalAppOverrides.SetImage(app.Id, dest);
                app.ImageUrl = dest;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                new AetherShell.Client.Windows.MessageBox("Не удалось сохранить картинку: " + ex.Message, "Ошибка").ShowDialog();
            }
        }

        private void EditPath_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!(sender is Button btn) || !(btn.Tag is AppItem app)) return;
            if (MainWindow.Instance == null || !MainWindow.Instance.IsHighAccessMode) return;

            var win = new AppPathEditWindow(app.ExePath) { Owner = MainWindow.Instance };
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.ResultPath))
            {
                LocalAppOverrides.SetExePath(app.Id, win.ResultPath);
                app.ExePath = win.ResultPath;
                ApplyFilters();
            }
        }
    }
}
