// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Markup;

using Depot.Composition;
using Depot.Diagnostics;
using Depot.Services;
using Depot.Views.Login;

namespace Depot;

public partial class App : Application
{
	private readonly ApplicationInformationService _applicationInformation =
		new(typeof(App).Assembly);
	private readonly IFileDialogService _fileDialogs = new FileDialogService();
	private DepotApplicationServices? _composition;

	static App()
	{
		FrameworkElement.LanguageProperty.OverrideMetadata(
			typeof(FrameworkElement),
			new FrameworkPropertyMetadata(
				XmlLanguage.GetLanguage("de-DE")));
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		try
		{
			base.OnStartup(e);
			StartupDiagnostics.Log(
				$"Application startup. Version {_applicationInformation.GetVersionInfo().InformationalVersion}.");
			var composition = DepotApplicationServices.Create(_fileDialogs, _applicationInformation);
			_composition = composition;
			StartupDiagnostics.Log("Application composition initialized.");
			RunApplication(composition);
		}
		catch (Exception exception)
		{
			StartupDiagnostics.LogException(exception);
			StartupDiagnostics.ShowStartupError(exception);
			Shutdown();
		}
	}

	private void RunApplication(DepotApplicationServices composition)
	{
		while (true)
		{
			composition.Services.Session.Reset();
			if (!ShowLogin(composition))
			{
				Shutdown();
				return;
			}

			ShowMainWindow(composition);
			if (!composition.Services.Session.LogoutRequestedByUser)
			{
				Shutdown();
				return;
			}

			StartupDiagnostics.Log("Restarting session.");
		}
	}

	private static bool ShowLogin(DepotApplicationServices composition)
	{
		var loginWindow = new LoginWindow(composition.ViewModels.CreateLogin());
		StartupDiagnostics.Log("Showing login dialog.");
		var result = loginWindow.ShowDialog();
		StartupDiagnostics.Log($"Login dialog returned: {result}");
		return result == true;
	}

	private void ShowMainWindow(DepotApplicationServices composition)
	{
		var mainViewModel = composition.ViewModels.CreateMain();
		StartupDiagnostics.Log("MainViewModel created.");

		var mainWindow = new MainWindow { DataContext = mainViewModel };
		MainWindow = mainWindow;
		StartupDiagnostics.Log("MainWindow created.");
		mainViewModel.LogoutRequested += OnLogoutRequested;
		try
		{
			mainWindow.ShowDialog();
		}
		finally
		{
			mainViewModel.LogoutRequested -= OnLogoutRequested;
		}

		StartupDiagnostics.Log("MainWindow closed.");

		void OnLogoutRequested(object? sender, EventArgs e)
		{
			mainWindow.Close();
		}
	}

	private static void OnDispatcherUnhandledException(
		object sender,
		System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		StartupDiagnostics.LogException(e.Exception);
		StartupDiagnostics.ShowRuntimeError(e.Exception);
		e.Handled = true;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_composition?.Dispose();
		_composition = null;
		base.OnExit(e);
	}
}
