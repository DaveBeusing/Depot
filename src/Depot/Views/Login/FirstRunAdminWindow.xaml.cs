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
		UpdatePasswordFeedback();
	}

	private void PasswordInput_Changed(object sender, RoutedEventArgs e) => UpdatePasswordFeedback();

	private void UpdatePasswordFeedback()
	{
		var password = PasswordBox.PasswordValue;
		var confirmation = ConfirmPasswordBox.PasswordValue;

		if (string.IsNullOrEmpty(password))
		{
			SetFeedback(PasswordStatusText, "Enter 12–128 characters with uppercase, lowercase, number and symbol.", isValid: null);
		}
		else
		{
			try
			{
				PasswordPolicy.Validate(password, EmailBox.Text);
				SetFeedback(PasswordStatusText, "✓ Password meets the security requirements.", isValid: true);
			}
			catch (ArgumentException exception)
			{
				SetFeedback(PasswordStatusText, $"✕ {exception.Message}", isValid: false);
			}
		}

		if (string.IsNullOrEmpty(confirmation))
		{
			SetFeedback(PasswordMatchText, "Re-enter the password to confirm it.", isValid: null);
		}
		else if (string.Equals(password, confirmation, StringComparison.Ordinal))
		{
			SetFeedback(PasswordMatchText, "✓ Passwords match.", isValid: true);
		}
		else
		{
			SetFeedback(PasswordMatchText, "✕ Passwords do not match.", isValid: false);
		}
	}

	private void SetFeedback(System.Windows.Controls.TextBlock target, string message, bool? isValid)
	{
		target.Text = message;
		target.SetResourceReference(ForegroundProperty, isValid == false ? "ErrorForegroundBrush" : "SecondaryTextBrush");
	}

	private async void CreateAdministrator_Click(object sender, RoutedEventArgs e)
	{
		ErrorPanel.Visibility = Visibility.Collapsed;
		UpdatePasswordFeedback();
		if (!string.Equals(PasswordBox.PasswordValue, ConfirmPasswordBox.PasswordValue, StringComparison.Ordinal))
		{
			ShowError("The password confirmation does not match.");
			return;
		}

		CreateButton.IsEnabled = false;
		try
		{
			await _bootstrap.CreateAdministratorAsync(EmailBox.Text, DisplayNameBox.Text, PasswordBox.PasswordValue, CancellationToken.None);
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
		ErrorPanel.Visibility = Visibility.Visible;
	}
}
