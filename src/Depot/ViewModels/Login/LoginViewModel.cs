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
	private readonly EnterpriseAuthenticationService _enterpriseAuthentication;
	private string _email = string.Empty;
	private string _password = string.Empty;
	private string? _errorMessage;
	private IReadOnlyList<EnterpriseLoginProviderOption> _enterpriseProviders = [];
	private EnterpriseLoginProviderOption? _selectedEnterpriseProvider;

	public LoginViewModel(
		AuthenticationService authenticationService,
		EnterpriseAuthenticationService enterpriseAuthentication,
		ConnectionStatusService connectionStatusService)
	{
		_authenticationService = authenticationService;
		_enterpriseAuthentication = enterpriseAuthentication;
		ConnectionStatus = connectionStatusService;
		LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
		EnterpriseLoginCommand = new AsyncRelayCommand(EnterpriseLoginAsync, CanEnterpriseLogin);
	}

	public string Email
	{
		get => _email;
		set
		{
			if (_email == value) return;
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
			if (_password == value) return;
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
			if (_errorMessage == value) return;
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public IReadOnlyList<EnterpriseLoginProviderOption> EnterpriseProviders
	{
		get => _enterpriseProviders;
		private set
		{
			_enterpriseProviders = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasEnterpriseProviders));
		}
	}

	public EnterpriseLoginProviderOption? SelectedEnterpriseProvider
	{
		get => _selectedEnterpriseProvider;
		set
		{
			if (Equals(_selectedEnterpriseProvider, value)) return;
			_selectedEnterpriseProvider = value;
			OnPropertyChanged();
			ClearError();
			EnterpriseLoginCommand.RaiseCanExecuteChanged();
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
	public bool HasEnterpriseProviders => EnterpriseProviders.Count > 0;
	public AsyncRelayCommand LoginCommand { get; }
	public AsyncRelayCommand EnterpriseLoginCommand { get; }
	public ConnectionStatusService ConnectionStatus { get; }
	public event EventHandler? LoginSucceeded;

	public async Task LoadEnterpriseProvidersAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			EnterpriseProviders = await _enterpriseAuthentication.ListLoginProvidersAsync(cancellationToken);
			SelectedEnterpriseProvider = EnterpriseProviders.FirstOrDefault();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			StartupDiagnostics.Log($"Login: enterprise provider discovery failed: {exception.GetType().Name}.");
			EnterpriseProviders = [];
			SelectedEnterpriseProvider = null;
		}
	}

	private bool CanLogin() =>
		!IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrEmpty(Password);

	private bool CanEnterpriseLogin() => !IsBusy && SelectedEnterpriseProvider is not null;

	private async Task LoginAsync(CancellationToken cancellationToken)
	{
		StartupDiagnostics.Log("Login: local authentication started.");
		ClearError();
		BeginOperation("Signing in");
		RaiseLoginCommands();
		try
		{
			if (!await _authenticationService.SignInAsync(Email, Password, cancellationToken))
			{
				Password = string.Empty;
				ErrorMessage = "The email or password is incorrect, or the account is inactive.";
				StartupDiagnostics.Log("Login: local authentication failed.");
				CompleteOperation();
				return;
			}

			Password = string.Empty;
			StartupDiagnostics.Log("Login: local authentication succeeded.");
			CompleteOperation(statusText: "Signed in");
			LoginSucceeded?.Invoke(this, EventArgs.Empty);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation();
			throw;
		}
		finally
		{
			RaiseLoginCommands();
		}
	}

	private async Task EnterpriseLoginAsync(CancellationToken cancellationToken)
	{
		var provider = SelectedEnterpriseProvider;
		if (provider is null) return;
		StartupDiagnostics.Log($"Login: enterprise authentication started for provider '{provider.Code}'.");
		ClearError();
		BeginOperation("Continue sign-in in your browser");
		RaiseLoginCommands();
		try
		{
			var result = await _enterpriseAuthentication.SignInAsync(provider.Code, cancellationToken);
			switch (result.Status)
			{
				case EnterpriseSignInStatus.Succeeded:
					StartupDiagnostics.Log($"Login: enterprise authentication succeeded for provider '{provider.Code}'.");
					CompleteOperation(statusText: "Signed in");
					LoginSucceeded?.Invoke(this, EventArgs.Empty);
					return;
				case EnterpriseSignInStatus.Cancelled:
					CompleteOperation(statusText: "Organization sign-in cancelled");
					return;
				case EnterpriseSignInStatus.IdentityNotLinked:
					ErrorMessage = "This organization account is valid but is not linked to an active Depot user. Contact a Depot administrator.";
					break;
				case EnterpriseSignInStatus.SessionLimitExceeded:
					ErrorMessage = "The active session limit for this Depot account has been reached.";
					break;
				default:
					ErrorMessage = "Organization sign-in could not be completed. Verify the configured identity provider or contact an administrator.";
					break;
			}
			StartupDiagnostics.Log($"Login: enterprise authentication did not complete for provider '{provider.Code}' ({result.FailureCode ?? result.Status.ToString()}).");
			CompleteOperation();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation();
			throw;
		}
		finally
		{
			RaiseLoginCommands();
		}
	}

	private void RaiseLoginCommands()
	{
		LoginCommand.RaiseCanExecuteChanged();
		EnterpriseLoginCommand.RaiseCanExecuteChanged();
	}

	private void ClearError()
	{
		if (ErrorMessage is not null) ErrorMessage = null;
	}
}
