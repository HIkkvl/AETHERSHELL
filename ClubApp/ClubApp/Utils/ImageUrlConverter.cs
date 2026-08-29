using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AetherShell.Client.Utils
{
    public class ImageUrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string url && !string.IsNullOrEmpty(url))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();

                    // OnLoad сразу декодирует картинку и отпускает поток файла.
                    // Если файла нет, UriSource кинет исключение — ловим в catch.
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;

                    if (url.StartsWith("http"))
                    {
                        bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    }
                    else if (url.StartsWith("/"))
                    {
                        // Картинки из панели приходят путём вида
                        // /uploads/club-1/xxx.png — дополняем адресом сервера.
                        bitmap.UriSource = new Uri(AppConstants.SERVER_URL + url, UriKind.Absolute);
                    }
                    else
                    {
                        // Локальный относительный или абсолютный путь
                        bitmap.UriSource = new Uri(url, UriKind.RelativeOrAbsolute);
                    }

                    bitmap.EndInit(); // после EndInit картинка готова к показу
                    return bitmap;
                }
            }
            catch
            {
                // Ошибка (нет файла и т.п.) — не роняем UI, просто пустая картинка
                return null;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
