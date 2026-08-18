// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using Depot.Controls;
using Depot.Models;
using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels;
using Depot.ViewModels.Administration;
using Depot.Views;

namespace Depot;

public partial class MainWindow : Window
{
	private const string UserIconData = "M12,12 C15.3,12 18,9.3 18,6 C18,2.7 15.3,0 12,0 C8.7,0 6,2.7 6,6 C6,9.3 8.7,12 12,12 M2,24 C2,18.5 6.5,14 12,14 C17.5,14 22,18.5 22,24";
	private const string NotificationIconData = "M4,15 L16,15 M6,15 L6,9 C6,5.7 7.8,3 10,3 C12.2,3 14,5.7 14,9 L14,15 M8,18 L12,18";
	private const string HelpIconData = "M10,18 A8,8 0 1 0 10,2 A8,8 0 1 0 10,18 M7.8,7.2 C8,5.8 9,5 10.3,5 C11.8,5 12.8,5.9 12.8,7.2 C12.8,8.2 12.2,8.8 11.2,9.5 C10.4,10.1 10,10.7 10,11.8 M10,14.5 L10,14.6";
	private const string SalesIconData = "M3,4 L17,4 L17,17 L3,17 Z M6,13 L9,10 L12,12 L15,7 M6,7 L10,7";

	private readonly IAuthorizationService _authorization;
	private readonly INotificationNavigationService _notificationNavigation;
	private readonly SalesViewModel _salesViewModel;
	private readonly CurrentUserViewModel _currentUserViewModel;
	private readonly ShellNavigationItem _currentUserNavigationItem;
	private readonly List<ShellPaletteEntry> _recentQuickOpenEntries = [];
	private ShellNavigationItem? _notificationNavigationItem;
	private ShellNavigationItem? _helpNavigationItem;
	private ShellNavigationItem? _salesNavigationItem;
	private MainViewModel? _observedViewModel;

	public MainWindow(IAuthorizationService authorization, INotificationNavigationService notificationNavigation, SalesViewModel salesViewModel)
	{
		_authorization = authorization;
		_notificationNavigation = notificationNavigation;
		_salesViewModel = salesViewModel;
		var user = authorization.CurrentUser ?? throw new InvalidOperationException("A signed-in user is required to open the main window.");
		_currentUserViewModel = new CurrentUserViewModel(user);
		_currentUserNavigationItem = new ShellNavigationItem("User", UserIconData, () => _currentUserViewModel, (_, _) => Task.CompletedTask, "getting-started.first-login");
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
	}

	public string CurrentUserInitials => _currentUserViewModel.Initials;
	public string CurrentUserDisplayName => _currentUserViewModel.User.DisplayName;

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!e.Cancel && DataContext is MainViewModel viewModel && !viewModel.ConfirmDiscardChanges(viewModel.CurrentViewModel)) e.Cancel = true;
		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		Loaded -= OnLoaded;
		DataContextChanged -= OnDataContextChanged;
		ObserveViewModel(null);
		_notificationNavigation.SetNavigationHandler(null);
		_notificationNavigationItem?.Dispose();
		_helpNavigationItem?.Dispose();
		_currentUserNavigationItem.Dispose();
		_salesViewModel.Dispose();
		base.OnClosed(e);
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		base.OnPreviewKeyDown(e);
		var modifiers = Keyboard.Modifiers;
		if ((modifiers & ModifierKeys.Control) == 0) return;
		if (e.Key == Key.W) { WorkspaceTabs.CloseActiveTab(); e.Handled = true; }
		else if (e.Key == Key.Tab) { WorkspaceTabs.SelectRelativeTab((modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); e.Handled = true; }
		else if (e.Key == Key.P && (modifiers & ModifierKeys.Shift) != 0) { OpenPalette(ShellPaletteMode.Commands); e.Handled = true; }
		else if (e.Key == Key.P) { OpenPalette(ShellPaletteMode.QuickOpen); e.Handled = true; }
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

	private void OnLoaded(object sender, RoutedEventArgs e) => ObserveViewModel(DataContext as MainViewModel);
	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => ObserveViewModel(e.NewValue as MainViewModel);

	private void ObserveViewModel(MainViewModel? viewModel)
	{
		if (ReferenceEquals(_observedViewModel, viewModel)) return;
		if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
		_observedViewModel = viewModel;
		if (_observedViewModel is not null)
		{
			_observedViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
			EnsureSalesNavigation(_observedViewModel);
			_notificationNavigation.SetNavigationHandler((target, token) => NavigateToNotificationAsync(_observedViewModel, target, token));
		}
	}

	private void EnsureSalesNavigation(MainViewModel viewModel)
	{
		if (_salesNavigationItem is not null || !_authorization.HasPermission(ApplicationPermission.SalesView)) return;
		var pages = new List<SecondaryNavigationItem>();
		AddSalesPage(pages, ApplicationPermission.SalesView, "Overview", SalesSection.Overview, "sales.overview");
		AddSalesPage(pages, ApplicationPermission.CustomersView, "Customers", SalesSection.Customers, "sales.customers");
		AddSalesPage(pages, ApplicationPermission.SalesOrdersView, "Sales Orders", SalesSection.SalesOrders, "sales.orders");
		AddSalesPage(pages, ApplicationPermission.SalesOrdersApprove, "Approvals", SalesSection.Approvals, "sales.approvals");
		AddSalesPage(pages, ApplicationPermission.ShipmentsView, "Shipping", SalesSection.Shipping, "sales.shipping");
		AddSalesPage(pages, ApplicationPermission.SalesInvoicesView, "Invoices", SalesSection.Invoices, "sales.invoices");
		if (pages.Count == 0) return;
		var module = new ShellModuleViewModel("Sales", "Manage customers, sales orders, approvals, fulfillment, shipping, and invoicing.", pages) { NavigationGuard = viewModel.ConfirmDiscardChanges };
		_salesNavigationItem = new ShellNavigationItem("Sales", SalesIconData, () => module, (content, token) => ((ShellModuleViewModel)content).ActivateAsync(token), pages[0].HelpTopicId, false, false, (content, token) => ((ShellModuleViewModel)content).RefreshAsync(token));
		var reportsIndex = viewModel.NavigationItems.ToList().FindIndex(item => item.Name == "Reports");
		if (reportsIndex < 0) viewModel.NavigationItems.Add(_salesNavigationItem);
		else viewModel.NavigationItems.Insert(reportsIndex, _salesNavigationItem);
	}

	private void AddSalesPage(ICollection<SecondaryNavigationItem> pages, ApplicationPermission permission, string name, SalesSection section, string helpTopic)
	{
		if (!_authorization.HasPermission(permission)) return;
		pages.Add(new SecondaryNavigationItem(name, () => _salesViewModel, (content, token) => ((SalesViewModel)content).LoadAsync(token), helpTopic, () => _salesViewModel.Section = section, false, true));
	}

	private async Task NavigateToNotificationAsync(MainViewModel viewModel, NotificationNavigationTarget target, CancellationToken cancellationToken)
	{
		switch (target.SourceType)
		{
			case NotificationSourceTypes.PurchaseOrder:
				if (target.SourceId is not long orderId) throw new InvalidOperationException("The purchase-order reference is invalid.");
				await NavigateModulePageAsync(viewModel, "Purchasing", "Purchase Orders", cancellationToken); await viewModel.ProcurementViewModel.OpenOrderAsync(orderId, cancellationToken); break;
			case NotificationSourceTypes.PurchaseOrderApproval:
				if (target.SourceId is not long approvalId) throw new InvalidOperationException("The approval reference is invalid.");
				await NavigateDirectAsync(viewModel, "Approvals", cancellationToken); await viewModel.PurchaseOrderApprovalsViewModel.OpenApprovalAsync(approvalId, cancellationToken); break;
			case NotificationSourceTypes.InventoryCount:
				if (target.SourceId is not long countId) throw new InvalidOperationException("The inventory-count reference is invalid.");
				await NavigateModulePageAsync(viewModel, "Warehouse", "Inventory Counts", cancellationToken); await viewModel.InventoryCountsViewModel.OpenCountAsync(countId, cancellationToken); break;
			case NotificationSourceTypes.DatabaseAdministration:
				await NavigateDirectAsync(viewModel, "Administration", cancellationToken); await viewModel.AdministrationViewModel.NavigateToAsync(AdministrationSection.Database, cancellationToken); break;
			case NotificationSourceTypes.SalesOrder:
				if (target.SourceId is not long salesOrderId) throw new InvalidOperationException("The sales-order reference is invalid.");
				await NavigateSalesAsync(viewModel, "Sales Orders", cancellationToken); await _salesViewModel.OpenQuickItemAsync(new(SalesQuickOpenKind.SalesOrder, salesOrderId, string.Empty, string.Empty), cancellationToken); break;
			case NotificationSourceTypes.SalesOrderApproval:
				if (target.SourceId is not long salesApprovalId) throw new InvalidOperationException("The sales approval reference is invalid.");
				await NavigateSalesAsync(viewModel, "Approvals", cancellationToken); await _salesViewModel.OpenQuickItemAsync(new(SalesQuickOpenKind.SalesOrder, salesApprovalId, string.Empty, string.Empty), cancellationToken); _salesViewModel.Section = SalesSection.Approvals; break;
			case NotificationSourceTypes.Shipment:
				if (target.SourceId is not long shipmentId) throw new InvalidOperationException("The shipment reference is invalid.");
				await NavigateSalesAsync(viewModel, "Shipping", cancellationToken); await _salesViewModel.OpenQuickItemAsync(new(SalesQuickOpenKind.Shipment, shipmentId, string.Empty, string.Empty), cancellationToken); break;
			case NotificationSourceTypes.SalesInvoice:
				if (target.SourceId is not long invoiceId) throw new InvalidOperationException("The invoice reference is invalid.");
				await NavigateSalesAsync(viewModel, "Invoices", cancellationToken); await _salesViewModel.OpenQuickItemAsync(new(SalesQuickOpenKind.Invoice, invoiceId, string.Empty, string.Empty), cancellationToken); break;
		}
	}

	private static Task NavigateDirectAsync(MainViewModel viewModel, string name, CancellationToken token)
	{
		var item = viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == name) ?? throw new UnauthorizedAccessException("The requested page is not available.");
		return viewModel.NavigateAsync(item, token);
	}

	private static async Task NavigateModulePageAsync(MainViewModel viewModel, string moduleName, string pageName, CancellationToken token)
	{
		var item = viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName) ?? throw new UnauthorizedAccessException("The requested module is not available.");
		if (item.Content is not ShellModuleViewModel module) throw new InvalidOperationException("The requested navigation target is invalid.");
		var page = module.Pages.FirstOrDefault(candidate => candidate.Name == pageName) ?? throw new UnauthorizedAccessException("The requested page is not available.");
		if (!module.SetSelectedPage(page)) return;
		await viewModel.NavigateAsync(item, token);
	}

	private static Task NavigateSalesAsync(MainViewModel viewModel, string pageName, CancellationToken token) => NavigateModulePageAsync(viewModel, "Sales", pageName, token);

	private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(MainViewModel.CurrentViewModel) || sender is not MainViewModel viewModel) return;
		if (ReferenceEquals(viewModel.CurrentViewModel, viewModel.NotificationCenterViewModel))
		{
			_notificationNavigationItem ??= new ShellNavigationItem("Notifications", NotificationIconData, () => viewModel.NotificationCenterViewModel, (_, _) => Task.CompletedTask, "getting-started.first-login");
			WorkspaceTabs.ActiveItem = _notificationNavigationItem;
		}
		else if (ReferenceEquals(viewModel.CurrentViewModel, viewModel.HelpViewModel))
		{
			_helpNavigationItem ??= new ShellNavigationItem("Help", HelpIconData, () => viewModel.HelpViewModel, (_, _) => Task.CompletedTask, HelpService.FallbackTopicId);
			WorkspaceTabs.ActiveItem = _helpNavigationItem;
		}
	}

	private void OnWindowActivated(object? sender, EventArgs e) { if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(true); }
	private void OnWindowDeactivated(object? sender, EventArgs e) { if (DataContext is MainViewModel viewModel) viewModel.SetApplicationActive(false); }
	private async void OnCurrentUserClick(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.NavigateAsync(_currentUserNavigationItem); }
	private async void OnOpenHelpExecuted(object sender, ExecutedRoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(); e.Handled = true; }
	private async void OnOpenHelpTopicExecuted(object sender, ExecutedRoutedEventArgs e) { if (DataContext is MainViewModel viewModel) await viewModel.OpenHelpAsync(e.Parameter as string); e.Handled = true; }
	private void OnCopyDiagnosticsExecuted(object sender, ExecutedRoutedEventArgs e) { var sanitized = new DiagnosticsSanitizer().Sanitize(e.Parameter as string); if (!string.IsNullOrWhiteSpace(sanitized)) Clipboard.SetText(sanitized); e.Handled = true; }
}
