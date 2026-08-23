// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;

using Depot.Services;

namespace Depot.Views.Login;

public partial class FirstRunAdminWindow : Window
{
	private readonly AdministratorBootstrapService _bootstrap;

	public FirstRunAdminWindow(AdministratorBootstrapService bootstrap)
	{
		_bootstrap = bootstrap;
		InitializeComponent();
		EmailBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(EmailInput_Changed));
		UpdatePasswordFeedback();
	}

	private void PasswordInput_Changed(object sender, RoutedEventArgs e) => UpdatePasswordFeedback();
	private void EmailInput_Changed(object sender, TextChangedEventArgs e) => UpdatePasswordFeedback();

	private void UpdatePasswordFeedback()
	{
		var password = PasswordBox.PasswordValue;
		var confirmation = ConfirmPasswordBox.PasswordValue;
		var evaluation = PasswordPolicy.Evaluate(password, EmailBox.Text);

		SetRequirement(LengthRequirementText, evaluation.HasValidLength, "12–128 characters");
		SetRequirement(UppercaseRequirementText, evaluation.HasUppercase, "At least one uppercase letter");
		SetRequirement(LowercaseRequirementText, evaluation.HasLowercase, "At least one lowercase letter");
		SetRequirement(DigitRequirementText, evaluation.HasDigit, "At least one number");
		SetRequirement(SymbolRequirementText, evaluation.HasSymbol, "At least one symbol");
		SetRequirement(AccountNameRequirementText, evaluation.ExcludesAccountName, "Must not contain the account name");

		if (string.IsNullOrEmpty(confirmation))
		{
			SetFeedback(PasswordMatchText, "✕ Re-enter the password to confirm it.", isValid: false);
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

	private void SetRequirement(TextBlock target, bool isMet, string label) =>
		SetFeedback(target, $"{(isMet ? "✓" : "✕")} {label}", isMet);

	private static void SetFeedback(TextBlock target, string message, bool isValid)
	{
		target.Text = message;
		target.SetResourceReference(ForegroundProperty, isValid ? "SuccessForegroundBrush" : "ErrorForegroundBrush");
	}

	private async void CreateAdministrator_Click(object sender, RoutedEventArgs e)
	{
		ErrorPanel.Visibility = Visibility.Collapsed;
		UpdatePasswordFeedback();
		var evaluation = PasswordPolicy.Evaluate(PasswordBox.PasswordValue, EmailBox.Text);
		if (!evaluation.IsValid)
		{
			ShowError("The password does not meet all security requirements shown above.");
			return;
		}
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
