// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;

using Depot.Diagnostics;
using Depot.ViewModels.Login;

namespace Depot.Views.Login;

/// <summary>
/// Interaction logic for LoginWindow.
/// </summary>
public partial class LoginWindow : Window
{
	private readonly LoginViewModel _viewModel;
	private readonly CancellationTokenSource _lifetimeCancellation = new();

	public LoginWindow(LoginViewModel viewModel)
	{
		_viewModel = viewModel;
		StartupDiagnostics.Log("LoginWindow: initialization started.");
		InitializeComponent();
		StartupDiagnostics.Log("LoginWindow: components initialized.");
		DataContext = viewModel;
		StartupDiagnostics.Log("LoginWindow: data context assigned.");
		SourceInitialized += (_, _) => StartupDiagnostics.Log("LoginWindow: source initialized.");
		Loaded += OnLoaded;
		Closed += OnClosed;
		viewModel.LoginSucceeded += OnLoginSucceeded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		StartupDiagnostics.Log("LoginWindow: loaded.");
		try
		{
			await _viewModel.LoadEnterpriseProvidersAsync(_lifetimeCancellation.Token);
		}
		catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
		{
		}
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_viewModel.LoginCommand.Cancel();
		_viewModel.EnterpriseLoginCommand.Cancel();
		_lifetimeCancellation.Cancel();
		_lifetimeCancellation.Dispose();
		_viewModel.LoginSucceeded -= OnLoginSucceeded;
	}

	private void OnLoginSucceeded(object? sender, EventArgs e)
	{
		StartupDiagnostics.Log("LoginWindow: setting DialogResult");
		DialogResult = true;
		StartupDiagnostics.Log("LoginWindow: DialogResult set");
	}
}
