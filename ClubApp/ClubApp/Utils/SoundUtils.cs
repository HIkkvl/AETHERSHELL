using System;
using System.IO;
using System.Windows; 
using System.Windows.Media;

namespace AetherShell.Client.Utils
{
    public static class SoundUtils
    {
        public static void PlayWarningSound()
        {
            try
            {
                string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.SOUND_WARNING_PATH);
                if (!File.Exists(soundPath)) return;

                var mediaPlayer = new MediaPlayer();
                mediaPlayer.Open(new Uri(soundPath, UriKind.Absolute));
                mediaPlayer.Volume = 1.0;
                mediaPlayer.Play();
            }
            catch { }
        }
        public static void PlayNotificationSound()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var player = new MediaPlayer();

                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string soundPath = Path.Combine(baseDir, "Sounds", "notification.mp3");

                    if (File.Exists(soundPath))
                    {
                        player.Open(new Uri(soundPath));
                        player.Play();
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка воспроизведения звука: " + ex.Message);
            }
        }
    }
}
