// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Commands;
using Depot.Diagnostics;
using Depot.Services;

namespace Depot.ViewModels.Login;

/// <summary>
/// Represents the login dialog.
/// </summary>
public sealed class LoginViewModel : BaseViewModel
{
	private readonly AuthenticationService _authenticationService;
	private string _email = string.Empty;
	private string _password = string.Empty;
	private string? _errorMessage;

	public LoginViewModel(
		AuthenticationService authenticationService,
		ConnectionStatusService connectionStatusService)
	{
		_authenticationService = authenticationService;
		ConnectionStatus = connectionStatusService;
		LoginCommand = new RelayCommand(Login, CanLogin);
	}

	public string Email
	{
		get => _email;
		set
		{
			if (_email == value)
			{
				return;
			}

			_email = value;
			OnPropertyChanged();
			ClearError();
			LoginCommand.RaiseCanExecuteChanged();
		}
	}

	public string Password
	{
		get => _password;
		set
		{
			if (_password == value)
			{
				return;
			}

			_password = value;
			OnPropertyChanged();
			ClearError();
			LoginCommand.RaiseCanExecuteChanged();
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		private set
		{
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

	public RelayCommand LoginCommand { get; }

	public ConnectionStatusService ConnectionStatus { get; }

	public event EventHandler? LoginSucceeded;

	private bool CanLogin() =>
		!string.IsNullOrWhiteSpace(Email) &&
		!string.IsNullOrEmpty(Password);

	private void Login()
	{
		StartupDiagnostics.Log("Login: authentication started.");
		if (!_authenticationService.SignIn(Email, Password))
		{
			Password = string.Empty;
			ErrorMessage = "The email or password is incorrect, or the account is inactive.";
			StartupDiagnostics.Log("Login: authentication failed.");
			return;
		}

		Password = string.Empty;
		StartupDiagnostics.Log("Login: authentication succeeded.");
		LoginSucceeded?.Invoke(this, EventArgs.Empty);
	}

	private void ClearError()
	{
		if (ErrorMessage is not null)
		{
			ErrorMessage = null;
		}
	}
}
