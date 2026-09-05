using System.Windows;

namespace DepotManager;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (ManagerSelfUpdateBootstrap.TryHandle(args)) return;
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    static App()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;
        WindowsTitleBarTheme.Apply(window);
        if (window is MainWindow mainWindow)
        {
            mainWindow.InitializeCompletionUi();
            ManagerSelfUpdateBootstrap.AcknowledgeStartup();
        }
    }
}
