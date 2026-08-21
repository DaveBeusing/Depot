// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using Depot.Controls;
using Depot.Diagnostics;
using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels;
using Depot.Views;

namespace Depot;

public partial class MainWindow : Window
{
	private const string UserIconData = "M12,12 C15.3,12 18,9.3 18,6 C18,2.7 15.3,0 12,0 C8.7,0 6,2.7 6,6 C6,9.3 8.7,12 12,12 M2,24 C2,18.5 6.5,14 12,14 C17.5,14 22,18.5 22,24";
	private const string NotificationIconData = "M4,15 L16,15 M6,15 L6,9 C6,5.7 7.8,3 10,3 C12.2,3 14,5.7 14,9 L14,15 M8,18 L12,18";
	private const string HelpIconData = "M10,18 A8,8 0 1 0 10,2 A8,8 0 1 0 10,18 M7.8,7.2 C8,5.8 9,5 10.3,5 C11.8,5 12.8,5.9 12.8,7.2 C12.8,8.2 12.2,8.8 11.2,9.5 C10.4,10.1 10,10.7 10,11.8 M10,14.5 L10,14.6";

	public static readonly DependencyProperty BreadcrumbTextProperty = DependencyProperty.Register(
		nameof(BreadcrumbText), typeof(string), typeof(MainWindow), new PropertyMetadata(string.Empty));
	public static readonly DependencyProperty CanNavigateBackProperty = DependencyProperty.Register(
		nameof(CanNavigateBack), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
	public static readonly DependencyProperty CanNavigateForwardProperty = DependencyProperty.Register(
		nameof(CanNavigateForward), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

	private readonly CurrentUserViewModel _currentUserViewModel;
	private readonly ShellNavigationItem _currentUserNavigationItem;
	private readonly List<ShellPaletteEntry> _recentQuickOpenEntries = [];
	private readonly NavigationHistoryService _history = new();
	private ShellNavigationItem? _notificationNavigationItem;
	private ShellNavigationItem? _helpNavigationItem;
	private MainViewModel? _observedViewModel;
	private ShellModuleViewModel? _observedModule;
	private bool _historyNavigation;

	public MainWindow(IAuthorizationService authorization)
	{
		var user = authorization.CurrentUser ?? throw new InvalidOperationException("A signed-in user is required to open the main window.");
		_currentUserViewModel = new CurrentUserViewModel(user);
		_currentUserNavigationItem = new ShellNavigationItem("User", UserIconData, () => _currentUserViewModel, (_, _) => Task.CompletedTask, "getting-started.first-login");
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
	}

	public string CurrentUserInitials => _currentUserViewModel.Initials;
	public string CurrentUserDisplayName => _currentUserViewModel.User.DisplayName;
	public string BreadcrumbText { get => (string)GetValue(BreadcrumbTextProperty); private set => SetValue(BreadcrumbTextProperty, value); }
	public bool CanNavigateBack { get => (bool)GetValue(CanNavigateBackProperty); private set => SetValue(CanNavigateBackProperty, value); }
	public bool CanNavigateForward { get => (bool)GetValue(CanNavigateForwardProperty); private set => SetValue(CanNavigateForwardProperty, value); }

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!e.Cancel && DataContext is MainViewModel viewModel && !viewModel.ConfirmDiscardChanges(viewModel.CurrentViewModel)) e.Cancel = true;
		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		Loaded -= OnLoaded;
		DataContextChanged -= OnDataContextChanged;
		ObserveModule(null);
		ObserveViewModel(null);
		_notificationNavigationItem?.Dispose();
		_helpNavigationItem?.Dispose();
		_currentUserNavigationItem.Dispose();
		base.OnClosed(e);
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		var modifiers = Keyboard.Modifiers;
		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if ((modifiers & ModifierKeys.Alt) != 0 && key == Key.Left) { _ = NavigateHistoryAsync(false); e.Handled = true; return; }
		if ((modifiers & ModifierKeys.Alt) != 0 && key == Key.Right) { _ = NavigateHistoryAsync(true); e.Handled = true; return; }
		if ((modifiers & ModifierKeys.Control) == 0) return;
		if (key == Key.W) { WorkspaceTabs.CloseActiveTab(); e.Handled = true; }
		else if (key == Key.Tab) { WorkspaceTabs.SelectRelativeTab((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; }
		else if (key == Key.P && (modifiers & ModifierKeys.Shift) != 0) { OpenPalette(ShellPaletteMode.Commands); e.Handled = true; }
		else if (key == Key.P) { OpenPalette(ShellPaletteMode.QuickOpen); e.Handled = true; }
	}

	private void OpenPalette(ShellPaletteMode mode)
	{
		if (DataContext is not MainViewModel viewModel) return;
		var palette = new ShellPaletteWindow(
			viewModel,
			mode,
			() => viewModel.OpenNotificationsAsync(),
			() => viewModel.OpenHelpAsync(),
			() => viewModel.NavigateAsync(_currentUserNavigationItem),
			_recentQuickOpenEntries) { Owner = this };
		palette.ShowDialog();
	}

	private void OnWorkspaceTabClosing(object? sender, WorkspaceTabClosingEventArgs e)
	{
		if (DataContext is MainViewModel viewModel && !viewModel.ConfirmDiscardChanges(e.Item)) e.Cancel = true;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		ObserveViewModel(DataContext as MainViewModel);
		if (DataContext is MainViewModel viewModel) UpdateNavigationContext(viewModel);
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => ObserveViewModel(e.NewValue as MainViewModel);

	private void ObserveViewModel(MainViewModel? viewModel)
	{
		if (ReferenceEquals(_observedViewModel, viewModel)) return;
		if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
		_observedViewModel = viewModel;
		if (_observedViewModel is not null)
		{
			_observedViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
			UpdateNavigationContext(_observedViewModel);
		}
	}

	private void ObserveModule(ShellModuleViewModel? module)
	{
		if (ReferenceEquals(_observedModule, module)) return;
		if (_observedModule is not null) _observedModule.PropertyChanged -= OnModulePropertyChanged;
		_observedModule = module;
		if (_observedModule is not null) _observedModule.PropertyChanged += OnModulePropertyChanged;
	}

	private void OnModulePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ShellModuleViewModel.SelectedPage) && DataContext is MainViewModel viewModel) UpdateNavigationContext(viewModel);
	}

	private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not MainViewModel viewModel) return;
		if (e.PropertyName is nameof(MainViewModel.SelectedNavigationItem) or nameof(MainViewModel.CurrentViewModel)) UpdateNavigationContext(viewModel);
		if (e.PropertyName != nameof(MainViewModel.CurrentViewModel)) return;
		if (ReferenceEquals(viewModel.CurrentViewModel, viewModel.NotificationCenterViewModel))
		{
			_notificationNavigationItem ??= new ShellNavigationItem("Notifications", NotificationIconData, () => viewModel.NotificationCenterViewModel, (_, _) => Task.CompletedTask, "getting-started.first-login");
			WorkspaceTabs.ActiveItem = _notificationNavigationItem;
			BreadcrumbText = "Notifications";
		}
		else if (ReferenceEquals(viewModel.CurrentViewModel, viewModel.HelpViewModel))
		{
			_helpNavigationItem ??= new ShellNavigationItem("Help", HelpIconData, () => viewModel.HelpViewModel, (_, _) => Task.CompletedTask, HelpService.FallbackTopicId);
			WorkspaceTabs.ActiveItem = _helpNavigationItem;
			BreadcrumbText = "Help";
		}
	}

	private void UpdateNavigationContext(MainViewModel viewModel)
	{
		var item = viewModel.SelectedNavigationItem;
		if (item is null) return;
		var route = item.Route;
		var breadcrumb = item.Name;
		if (item.IsContentCreated && item.Content is ShellModuleViewModel module)
		{
			ObserveModule(module);
			if (module.SelectedPage is { } page)
			{
				route = page.Route;
				breadcrumb = $"{item.Name} › {page.Name}";
			}
		}
		else
		{
			ObserveModule(null);
		}

		BreadcrumbText = breadcrumb;
		if (!_historyNavigation) _history.Record(route);
		CanNavigateBack = _history.CanGoBack;
		CanNavigateForward = _history.CanGoForward;
	}

	private async Task NavigateHistoryAsync(bool forward)
	{
		if (DataContext is not MainViewModel viewModel) return;
		var target = forward ? _history.GoForward() : _history.GoBack();
		if (target is null) return;
		_historyNavigation = true;
		try { await viewModel.NavigateToRouteAsync(target.Value); }
		finally
		{
			_historyNavigation = false;
			UpdateNavigationContext(viewModel);
		}
	}

	private async void OnBackClick(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(false);
	private async void OnForwardClick(object sender, RoutedEventArgs e) => await NavigateHistoryAsync(true);
	private void OnWindowActivated(object? sender, EventArgs e) { if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(true); }
	private void OnWindowDeactivated(object? sender, EventArgs e) { if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(false); }
	private async void OnCurrentUserClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.NavigateAsync(_currentUserNavigationItem); }
	private async void OnOpenHelpExecuted(object sender, ExecutedRoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(); e.Handled = true; }
	private async void OnOpenHelpTopicExecuted(object sender, ExecutedRoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(e.Parameter as string); e.Handled = true; }
	private void OnCopyDiagnosticsExecuted(object sender, ExecutedRoutedEventArgs e) { var sanitized = new DiagnosticsSanitizer().Sanitize(e.Parameter as string); if (!string.IsNullOrWhiteSpace(sanitized)) Clipboard.SetText(sanitized); e.Handled = true; }
}
