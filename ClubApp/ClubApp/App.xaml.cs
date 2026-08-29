using System.Windows;

namespace AetherShell.Client
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // OverrideMetadata(FrameworkElement) падает — метаданные уже заданы.
            // Убираем пунктирную рамку фокуса на каждом элементе при Loaded.
            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((_, args) =>
                {
                    if (args.OriginalSource is FrameworkElement fe)
                        fe.FocusVisualStyle = null;
                }));

            base.OnStartup(e);
        }
    }
}
