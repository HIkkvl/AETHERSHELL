using System;
using System.Globalization;
using System.IO;
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

                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    }
                    else if (url.StartsWith("/") && !url.StartsWith("//"))
                    {
                        bitmap.UriSource = new Uri(AppConstants.SERVER_URL + url, UriKind.Absolute);
                    }
                    else if (File.Exists(url) || Path.IsPathRooted(url))
                    {
                        bitmap.UriSource = new Uri(Path.GetFullPath(url), UriKind.Absolute);
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    }
                    else
                    {
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
