using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AetherShell.Client
{
    public partial class PaymentWindow : Window
    {
        public PaymentWindow(string url)
        {
            InitializeComponent();
            InitializeWebView(url);
        }

        private async void InitializeWebView(string url)
        {
            // 1. Инициализация
            await webView.EnsureCoreWebView2Async(null);

            // 2. Настройки (отключаем лишнее)
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; // Отключаем меню правой кнопки мыши

            // Окно оплаты тоже не должно скачивать файлы
            webView.CoreWebView2.DownloadStarting += (s, e) =>
            {
                e.Cancel = true;
                e.Handled = true;
            };

            // 4. Загружаем ссылку
            webView.Source = new Uri(url);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}