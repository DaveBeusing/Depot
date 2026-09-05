using System.Windows;

namespace DepotManager;

public partial class App : Application
{
    static App()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (ManagerSelfUpdateBootstrap.TryHandle(e.Args))
            {
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Depot Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) WindowsTitleBarTheme.Apply(window);
    }
}
