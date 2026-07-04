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
	public LoginWindow(LoginViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
		viewModel.LoginSucceeded += OnLoginSucceeded;
	}

	private void OnLoginSucceeded(object? sender, EventArgs e)
	{
		StartupDiagnostics.Log("LoginWindow: setting DialogResult");
		DialogResult = true;
		StartupDiagnostics.Log("LoginWindow: DialogResult set");
	}
}