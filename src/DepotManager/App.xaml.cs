using System.Windows;

namespace DepotManager;

public partial class App : Application
{
	static App()
	{
		EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded));
	}

	private static void OnWindowLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is Window window) WindowsTitleBarTheme.Apply(window);
	}
}
