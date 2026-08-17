// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Input;

using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels;

namespace Depot;

public partial class MainWindow : Window
{
	private const string UserIconData =
		"M12,12 C15.3,12 18,9.3 18,6 C18,2.7 15.3,0 12,0 C8.7,0 6,2.7 6,6 C6,9.3 8.7,12 12,12 M2,24 C2,18.5 6.5,14 12,14 C17.5,14 22,18.5 22,24";

	private readonly CurrentUserViewModel _currentUserViewModel;
	private readonly ShellNavigationItem _currentUserNavigationItem;

	public MainWindow(IAuthorizationService authorization)
	{
		var user = authorization.CurrentUser
			?? throw new InvalidOperationException("A signed-in user is required to open the main window.");

		_currentUserViewModel = new CurrentUserViewModel(user);
		_currentUserNavigationItem = new ShellNavigationItem(
			"User",
			UserIconData,
			() => _currentUserViewModel,
			(_, _) => Task.CompletedTask,
			"getting-started.first-login");

		InitializeComponent();
	}

	public string CurrentUserInitials => _currentUserViewModel.Initials;
	public string CurrentUserDisplayName => _currentUserViewModel.User.DisplayName;

	protected override void OnClosed(EventArgs e)
	{
		_currentUserNavigationItem.Dispose();
		base.OnClosed(e);
	}

	private void OnWindowActivated(object? sender, EventArgs e)
	{
		if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(true);
	}

	private void OnWindowDeactivated(object? sender, EventArgs e)
	{
		if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(false);
	}

	private async void OnCurrentUserClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel)
			await viewModel.NavigateAsync(_currentUserNavigationItem);
	}

	private async void OnOpenHelpExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync();
		e.Handled = true;
	}

	private async void OnOpenHelpTopicExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(e.Parameter as string);
		e.Handled = true;
	}

	private void OnCopyDiagnosticsExecuted(object sender, ExecutedRoutedEventArgs e)
	{
		var sanitized = new DiagnosticsSanitizer().Sanitize(e.Parameter as string);
		if (!string.IsNullOrWhiteSpace(sanitized)) Clipboard.SetText(sanitized);
		e.Handled = true;
	}
}
