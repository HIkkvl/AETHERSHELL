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
            try
            {
                await webView.EnsureCoreWebView2Async(null);

                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                webView.CoreWebView2.DownloadStarting += (s, e) =>
                {
                    e.Cancel = true;
                    e.Handled = true;
                };

                webView.Source = new Uri(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть страницу оплаты (WebView2).\n\n" +
                    "Установите «Microsoft Edge WebView2 Runtime» и перезапустите клиент.\n\n" +
                    ex.Message,
                    "Оплата",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Close();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
