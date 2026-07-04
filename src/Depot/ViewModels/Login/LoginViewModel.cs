// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Diagnostics;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Login;

/// <summary>
/// Represents the login dialog.
/// </summary>
public sealed class LoginViewModel : BaseViewModel
{
	private readonly AuthorizationService _authorizationService;

    private User? _selectedUser;

    public LoginViewModel(UserService userService, AuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
        StartupDiagnostics.Log("LoginViewModel: Begin");
        LoginCommand =
            new RelayCommand(
                Login,
                CanLogin);

        StartupDiagnostics.Log("LoginViewModel: Command created");
        foreach (var user in userService.GetUsers())
        {
            Users.Add(user);
        }
        StartupDiagnostics.Log($"LoginViewModel: Loaded {Users.Count} users");
        SelectedUser = Users.FirstOrDefault();
        StartupDiagnostics.Log("LoginViewModel: Selected user assigned");
    }

    public ObservableCollection<User> Users { get; }
        = new();

    public User? SelectedUser
    {
        get => _selectedUser;
        set
        {
            StartupDiagnostics.Log("SelectedUser: Setter entered");
            _selectedUser = value;
            StartupDiagnostics.Log("SelectedUser: Field assigned");
            OnPropertyChanged();
            StartupDiagnostics.Log("SelectedUser: PropertyChanged");
            LoginCommand.RaiseCanExecuteChanged();
            StartupDiagnostics.Log("SelectedUser: CanExecuteChanged");
        }
    }

    public RelayCommand LoginCommand { get; }

    public event EventHandler? LoginSucceeded;

    private bool CanLogin()
    {
        return SelectedUser is not null;
    }

    private void Login()
    {
        StartupDiagnostics.Log("Login: begin");
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }
    	_authorizationService.SignIn(user);
        StartupDiagnostics.Log("Login: user signed in");
        LoginSucceeded?.Invoke(this, EventArgs.Empty);
        StartupDiagnostics.Log("Login: event raised");
    }
}