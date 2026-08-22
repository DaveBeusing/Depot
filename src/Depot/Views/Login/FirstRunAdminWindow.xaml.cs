// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;

using Depot.Services;

namespace Depot.Views.Login;

public partial class FirstRunAdminWindow : Window
{
	private readonly AdministratorBootstrapService _bootstrap;

	public FirstRunAdminWindow(AdministratorBootstrapService bootstrap)
	{
		_bootstrap = bootstrap;
		InitializeComponent();
	}

	private async void CreateAdministrator_Click(object sender, RoutedEventArgs e)
	{
		ErrorText.Visibility = Visibility.Collapsed;
		if (!string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
		{
			ShowError("The password confirmation does not match.");
			return;
		}

		CreateButton.IsEnabled = false;
		try
		{
			await _bootstrap.CreateAdministratorAsync(EmailBox.Text, DisplayNameBox.Text, PasswordBox.Password, CancellationToken.None);
			DialogResult = true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
		{
			ShowError(exception.Message);
		}
		finally
		{
			CreateButton.IsEnabled = true;
		}
	}

	private void ShowError(string message)
	{
		ErrorText.Text = message;
		ErrorText.Visibility = Visibility.Visible;
	}
}
