using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AetherShell.Client.Windows
{
    public partial class AppPathEditWindow : Window
    {
        public string ResultPath { get; private set; }

        public AppPathEditWindow(string currentPath)
        {
            InitializeComponent();
            PathBox.Text = currentPath ?? "";
            PathBox.Focus();
            PathBox.SelectAll();
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл запуска",
                Filter = "Программы|*.exe;*.lnk;*.bat;*.cmd|Все файлы|*.*"
            };
            if (dlg.ShowDialog() == true)
                PathBox.Text = dlg.FileName;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultPath = (PathBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ResultPath))
            {
                new MessageBox("Укажите путь или steam:// ссылку", "Путь").ShowDialog();
                return;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
