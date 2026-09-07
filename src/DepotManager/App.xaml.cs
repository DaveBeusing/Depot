using System.Windows;

namespace DepotManager;

public partial class App : Application
{
    private const string PackagedE2EEnabledVariable = "DEPOT_PACKAGED_E2E";
    private const string PackagedE2EFailStartupVariable = "DEPOT_PACKAGED_E2E_FORCE_STARTUP_FAILURE";
    private const string PackagedE2EExitAfterStartupVariable = "DEPOT_PACKAGED_E2E_EXIT_AFTER_STARTUP";

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

            if (ShouldForcePackagedE2EStartupFailure(e.Args))
            {
                Shutdown(2);
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
            SchedulePackagedE2EExitIfRequested();
        }
        catch
        {
            // Do not acknowledge a self-update unless the real manager window reached a usable Loaded state.
            // The staged helper will detect the missing readiness marker and restore the previous manager.
            Application.Current?.Shutdown(2);
        }
    }

    private static bool ShouldForcePackagedE2EStartupFailure(string[] args) =>
        IsPackagedE2EEnabled()
        && string.Equals(Environment.GetEnvironmentVariable(PackagedE2EFailStartupVariable), "1", StringComparison.Ordinal)
        && args.Length >= 1
        && string.Equals(args[0], "--manager-update-verification", StringComparison.Ordinal);

    private static void SchedulePackagedE2EExitIfRequested()
    {
        if (!IsPackagedE2EEnabled()
            || !string.Equals(Environment.GetEnvironmentVariable(PackagedE2EExitAfterStartupVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var application = Application.Current;
            if (application is null) return;
            application.Dispatcher.Invoke(application.Shutdown);
        });
    }

    private static bool IsPackagedE2EEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable(PackagedE2EEnabledVariable), "1", StringComparison.Ordinal);
}
