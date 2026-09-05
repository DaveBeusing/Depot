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
        if (sender is not Window window) return;
        WindowsTitleBarTheme.Apply(window);
        if (window is not MainWindow mainWindow) return;

        try
        {
            mainWindow.InitializeCompletionUi();
            ManagerSelfUpdateBootstrap.AcknowledgeStartup();
        }
        catch
        {
            // Do not acknowledge a self-update unless the real manager window reached a usable Loaded state.
            // The staged helper will detect the missing readiness marker and restore the previous manager.
            Application.Current?.Shutdown(2);
        }
    }
}
