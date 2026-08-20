// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Suppliers;

namespace Depot.Views;

public enum ShellPaletteMode { Commands, QuickOpen }

public sealed record ShellPaletteEntry(
	string Title,
	string Subtitle,
	string Group,
	string TypeLabel,
	string IconData,
	Func<Task> ExecuteAsync,
	string? RecordKey = null);

public partial class ShellPaletteWindow : Window
{
	private const string WorkspaceIcon = "M3,4 L17,4 L17,16 L3,16 Z M3,8 L17,8 M7,8 L7,16";
	private const string ActionIcon = "M10,3 L10,17 M3,10 L17,10";
	private const string ItemIcon = "M2,6 L10,2 L18,6 L10,10 Z M2,6 L2,14 L10,18 L10,10 M18,6 L18,14 L10,18";
	private const string PurchaseOrderIcon = "M3,4 L17,4 L16,17 L4,17 Z M7,4 L7,2 L13,2 L13,4 M7,8 L13,8 M7,12 L13,12";
	private const string SupplierIcon = "M3,17 L3,6 L10,2 L17,6 L17,17 M7,17 L7,11 L13,11 L13,17";
	private const string CustomerIcon = "M3,17 L3,6 L10,2 L17,6 L17,17 M6,9 L14,9 M6,13 L11,13";
	private const string SalesOrderIcon = "M3,3 L17,3 L17,17 L3,17 Z M6,7 L14,7 M6,10 L14,10 M6,13 L11,13";
	private const string ShipmentIcon = "M2,6 L10,2 L18,6 L18,14 L10,18 L2,14 Z M2,6 L10,10 L18,6 M10,10 L10,18";
	private const string InvoiceIcon = "M4,2 L15,2 L18,5 L18,18 L4,18 Z M14,2 L14,6 L18,6 M7,10 L15,10 M7,13 L15,13";
	private const string ReturnIcon = "M4,7 L9,3 L9,6 L14,6 C16.2,6 18,7.8 18,10 C18,12.2 16.2,14 14,14 L6,14 M4,7 L9,11 L9,8";
	private const string CreditNoteIcon = "M4,2 L15,2 L18,5 L18,18 L4,18 Z M7,11 L15,11 M11,7 L11,15";
	private const string NotificationIcon = "M4,15 L16,15 M6,15 L6,9 C6,5.7 7.8,3 10,3 C12.2,3 14,5.7 14,9 L14,15 M8,18 L12,18";
	private const string HelpIcon = "M10,18 A8,8 0 1 0 10,2 A8,8 0 1 0 10,18 M7.8,7.2 C8,5.8 9,5 10.3,5 C11.8,5 12.8,5.9 12.8,7.2 C12.8,8.2 12.2,8.8 11.2,9.5 C10.4,10.1 10,10.7 10,11.8 M10,14.5 L10,14.6";
	private const string UserIcon = "M10,10 C12.8,10 15,7.8 15,5 C15,2.2 12.8,0 10,0 C7.2,0 5,2.2 5,5 C5,7.8 7.2,10 10,10 M2,20 C2,15.6 5.6,12 10,12 C14.4,12 18,15.6 18,20";

	private readonly MainViewModel _viewModel;
	private readonly ShellPaletteMode _mode;
	private readonly Func<Task> _openNotifications;
	private readonly Func<Task> _openHelp;
	private readonly Func<Task> _openUser;
	private readonly IList<ShellPaletteEntry> _recentEntries;
	private CancellationTokenSource? _searchCancellation;
	private bool _updating;

	public ShellPaletteWindow(MainViewModel viewModel, ShellPaletteMode mode, Func<Task> openNotifications, Func<Task> openHelp, Func<Task> openUser, IList<ShellPaletteEntry> recentEntries)
	{
		_viewModel = viewModel;
		_mode = mode;
		_openNotifications = openNotifications;
		_openHelp = openHelp;
		_openUser = openUser;
		_recentEntries = recentEntries;
		InitializeComponent();
		Loaded += OnLoaded;
		Closed += (_, _) => { _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); };
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		SearchBox.Focus();
		Keyboard.Focus(SearchBox);
		ShortcutHint.Text = _mode == ShellPaletteMode.Commands ? "Ctrl+Shift+P" : "Ctrl+P";
		PromptGlyph.Text = _mode == ShellPaletteMode.Commands ? ">" : "⌕";
		SearchBox.ToolTip = _mode == ShellPaletteMode.Commands ? "Search commands" : "Search Depot";
		if (_mode == ShellPaletteMode.Commands) ApplyEntries(BuildCommandEntries(string.Empty)); else await RefreshQuickOpenAsync(string.Empty);
	}

	private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating) return;
		if (_mode == ShellPaletteMode.Commands) ApplyEntries(BuildCommandEntries(SearchBox.Text)); else await RefreshQuickOpenAsync(SearchBox.Text);
	}

	private async void OnSearchKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
		if (e.Key == Key.Down) { MoveSelection(1); e.Handled = true; return; }
		if (e.Key == Key.Up) { MoveSelection(-1); e.Handled = true; return; }
		if (e.Key == Key.Enter) { await ExecuteSelectedAsync(); e.Handled = true; }
	}

	private void MoveSelection(int offset)
	{
		if (ResultsList.Items.Count == 0) return;
		var index = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex + offset;
		ResultsList.SelectedIndex = Math.Clamp(index, 0, ResultsList.Items.Count - 1);
		ResultsList.ScrollIntoView(ResultsList.SelectedItem);
	}

	private async void OnResultsDoubleClick(object sender, MouseButtonEventArgs e) => await ExecuteSelectedAsync();

	private async Task ExecuteSelectedAsync()
	{
		if (ResultsList.SelectedItem is not ShellPaletteEntry entry) return;
		if (entry.RecordKey is not null) Remember(entry);
		Close();
		await entry.ExecuteAsync();
	}

	private IReadOnlyList<ShellPaletteEntry> BuildCommandEntries(string query)
	{
		var entries = BuildNavigationEntries();
		entries.Add(new("Open Notifications", "Open the notification center workspace", "Shell", "SHELL", NotificationIcon, _openNotifications));
		entries.Add(new("Open Help", "Open Depot help", "Shell", "SHELL", HelpIcon, _openHelp));
		entries.Add(new("Open User", "Open the signed-in user workspace", "Shell", "SHELL", UserIcon, _openUser));
		AddWorkflowAction(entries, "Inventory", "Items", "New Item", "Create a new inventory item", ItemIcon, async () => { if (await NavigateModulePageAsync("Inventory", "Items")) _viewModel.ItemsViewModel.NewItemCommand.Execute(null); });
		AddWorkflowAction(entries, "Purchasing", "Purchase Orders", "New Purchase Order", "Create a new purchase order", PurchaseOrderIcon, async () => { if (await NavigateModulePageAsync("Purchasing", "Purchase Orders")) _viewModel.ProcurementViewModel.NewOrderCommand.Execute(null); });
		AddWorkflowAction(entries, "Warehouse", "Inventory Counts", "Start Inventory Count", "Open a new physical inventory count", ActionIcon, async () => { if (await NavigateModulePageAsync("Warehouse", "Inventory Counts")) _viewModel.InventoryCountsViewModel.NewCommand.Execute(null); });
		AddWorkflowAction(entries, "Warehouse", "Transfers", "Transfer Stock", "Create a warehouse stock transfer", ActionIcon, async () => { if (await NavigateModulePageAsync("Warehouse", "Transfers")) _viewModel.StockTransfersViewModel.NewTransferCommand.Execute(null); });
		AddWorkflowAction(entries, "Purchasing", "Goods Receipts", "Receive Goods", "Open the goods receipt workflow", PurchaseOrderIcon, async () => { await NavigateModulePageAsync("Purchasing", "Goods Receipts"); });

		AddWorkflowAction(entries, "Sales", "Customers", "Sales: New Customer", "Create a customer account", CustomerIcon, async () => { if (await NavigateModulePageAsync("Sales", "Customers")) _viewModel.CustomersViewModel.Workspace.NewCustomerCommand.Execute(null); });
		AddWorkflowAction(entries, "Sales", "Sales Orders", "Sales: New Sales Order", "Create a customer sales order", SalesOrderIcon, async () => { if (await NavigateModulePageAsync("Sales", "Sales Orders")) _viewModel.SalesOrdersViewModel.Workspace.NewOrderCommand.Execute(null); });
		AddWorkflowAction(entries, "Sales", "Approvals", "Sales: Open Approval Queue", "Review submitted sales orders", SalesOrderIcon, async () => { await NavigateModulePageAsync("Sales", "Approvals"); });
		AddWorkflowAction(entries, "Sales", "Shipping", "Sales: Ship Order", "Open released orders for picking and shipping", ShipmentIcon, async () => { await NavigateModulePageAsync("Sales", "Shipping"); });
		AddWorkflowAction(entries, "Sales", "Shipping", "Sales: Create Customer Return", "Open shipping and customer returns", ReturnIcon, async () => { await NavigateModulePageAsync("Sales", "Shipping"); });
		AddWorkflowAction(entries, "Sales", "Invoices", "Sales: Create Invoice", "Create an invoice from a posted shipment", InvoiceIcon, async () => { await NavigateModulePageAsync("Sales", "Invoices"); });

		entries.Add(new("Sign Out", "End the current Depot session", "Account", "ACCOUNT", UserIcon, () => { _viewModel.LogoutCommand.Execute(null); return Task.CompletedTask; }));
		return Filter(entries, query);
	}

	private void AddWorkflowAction(List<ShellPaletteEntry> entries, string moduleName, string pageName, string title, string subtitle, string icon, Func<Task> execute)
	{
		if (HasModulePage(moduleName, pageName)) entries.Add(new(title, subtitle, "Actions", "ACTION", icon, execute));
	}

	private List<ShellPaletteEntry> BuildNavigationEntries()
	{
		var entries = new List<ShellPaletteEntry>();
		foreach (var item in _viewModel.NavigationItems)
		{
			var capturedItem = item;
			entries.Add(new(item.Name, "Open workspace", "Workspaces", "WORKSPACE", item.IconData, () => _viewModel.NavigateAsync(capturedItem)));
			if (item.Content is not ShellModuleViewModel module) continue;
			foreach (var page in module.Pages)
			{
				var capturedPage = page;
				entries.Add(new($"{item.Name}: {page.Name}", "Open workspace section", "Workspace Sections", "SECTION", item.IconData, async () => { if (!module.SetSelectedPage(capturedPage)) return; await _viewModel.NavigateAsync(capturedItem); }));
			}
		}
		foreach (var adminItem in _viewModel.AdministrationViewModel.NavigationItems)
		{
			if (adminItem.Section is not AdministrationSection section) continue;
			var capturedSection = section;
			entries.Add(new($"Administration: {adminItem.Name}", "Open administration section", "Workspace Sections", "SECTION", WorkspaceIcon, async () => { if (!await NavigateTopLevelAsync("Administration")) return; await _viewModel.AdministrationViewModel.NavigateToAsync(capturedSection); }));
		}
		return entries;
	}

	private async Task RefreshQuickOpenAsync(string query)
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = new CancellationTokenSource();
		var token = _searchCancellation.Token;
		var navigationEntries = Filter(BuildNavigationEntries(), query).Take(12).ToList();
		if (string.IsNullOrWhiteSpace(query))
		{
			var entries = _recentEntries.Take(8).ToList();
			entries.AddRange(navigationEntries);
			ApplyEntries(entries);
			StatusText.Text = _recentEntries.Count > 0 ? "Recent records and workspaces" : "Workspaces · type to search records";
			return;
		}
		if (query.Trim().Length < 2) { ApplyEntries(navigationEntries); StatusText.Text = "Type at least 2 characters to search records"; return; }
		StatusText.Text = "Searching records…";
		try
		{
			var text = query.Trim();
			var entries = navigationEntries;
			var oldItemSearch = _viewModel.ItemsViewModel.SearchText;
			_viewModel.ItemsViewModel.SearchText = text;
			await _viewModel.ItemsViewModel.LoadItemsAsync(token);
			foreach (var item in _viewModel.ItemsViewModel.Items.Take(8))
			{
				var captured = item;
				entries.Add(new(captured.PartNumber, captured.Description, "Items", "ITEM", ItemIcon, async () => { if (!await NavigateModulePageAsync("Inventory", "Items")) return; _viewModel.ItemsViewModel.SelectedItem = captured; }, $"item:{captured.Id}"));
			}
			_viewModel.ItemsViewModel.SearchText = oldItemSearch;

			var oldSection = _viewModel.ProcurementViewModel.Section;
			var oldOrderSearch = _viewModel.ProcurementViewModel.SearchText;
			var oldSupplierSearch = _viewModel.ProcurementViewModel.SupplierSearchText;
			_viewModel.ProcurementViewModel.Section = ProcurementSection.PurchaseOrders;
			_viewModel.ProcurementViewModel.SearchText = text;
			_viewModel.ProcurementViewModel.SupplierSearchText = text;
			await _viewModel.ProcurementViewModel.LoadAsync(token);
			foreach (var order in _viewModel.ProcurementViewModel.Orders.Take(8))
			{
				var captured = order;
				entries.Add(new(captured.OrderNumber, captured.SupplierName ?? "Purchase order", "Purchase Orders", "PO", PurchaseOrderIcon, async () => { if (!await NavigateModulePageAsync("Purchasing", "Purchase Orders")) return; await _viewModel.ProcurementViewModel.OpenOrderAsync(captured.Id); }, $"po:{captured.Id}"));
			}
			foreach (var supplier in _viewModel.ProcurementViewModel.Suppliers.Take(6))
			{
				var captured = supplier;
				entries.Add(new(captured.Name, captured.AccountNumber == 0 ? "Supplier" : $"Account {captured.AccountNumber}", "Suppliers", "SUPPLIER", SupplierIcon, () => OpenSupplierAsync(captured, text), $"supplier:{captured.Id}"));
			}
			_viewModel.ProcurementViewModel.SearchText = oldOrderSearch;
			_viewModel.ProcurementViewModel.SupplierSearchText = oldSupplierSearch;
			_viewModel.ProcurementViewModel.Section = oldSection;

			if (HasModulePage("Sales", "Overview"))
			{
				foreach (var result in await _viewModel.SalesViewModel.QuickSearchAsync(text, token))
				{
					var captured = result;
					var (group, type, icon, key) = captured.Kind switch
					{
						SalesQuickOpenKind.Customer => ("Customers", "CUSTOMER", CustomerIcon, $"customer:{captured.Id}"),
						SalesQuickOpenKind.SalesOrder => ("Sales Orders", "SO", SalesOrderIcon, $"sales-order:{captured.Id}"),
						SalesQuickOpenKind.Shipment => ("Shipments", "SHIPMENT", ShipmentIcon, $"shipment:{captured.Id}"),
						SalesQuickOpenKind.CustomerReturn => ("Customer Returns", "RET", ReturnIcon, $"customer-return:{captured.Id}"),
						SalesQuickOpenKind.CreditNote => ("Credit Notes", "CN", CreditNoteIcon, $"credit-note:{captured.Id}"),
						_ => ("Sales Invoices", "INVOICE", InvoiceIcon, $"invoice:{captured.Id}")
					};
					entries.Add(new(captured.Title, captured.Subtitle, group, type, icon, () => _viewModel.OpenSalesQuickItemAsync(captured), key));
				}
			}
			if (!token.IsCancellationRequested) { ApplyEntries(entries); StatusText.Text = $"{entries.Count:N0} results"; }
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { if (!token.IsCancellationRequested) StatusText.Text = exception.Message; }
	}

	private async Task OpenSupplierAsync(Supplier supplier, string query)
	{
		if (!await NavigateTopLevelAsync("Administration")) return;
		await _viewModel.AdministrationViewModel.NavigateToAsync(AdministrationSection.Suppliers);
		if (_viewModel.AdministrationViewModel.CurrentViewModel is SupplierViewModel suppliers)
		{
			suppliers.SearchText = query;
			await suppliers.LoadAsync();
			suppliers.SelectedSupplier = suppliers.Suppliers.FirstOrDefault(candidate => candidate.Id == supplier.Id);
		}
	}

	private async Task<bool> NavigateTopLevelAsync(string name)
	{
		var item = _viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == name);
		if (item is null) return false;
		await _viewModel.NavigateAsync(item);
		return ReferenceEquals(_viewModel.SelectedNavigationItem, item);
	}

	private async Task<bool> NavigateModulePageAsync(string moduleName, string pageName)
	{
		var item = _viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName);
		if (item is null || item.Content is not ShellModuleViewModel module) return false;
		var page = module.Pages.FirstOrDefault(candidate => candidate.Name == pageName);
		if (page is null || !module.SetSelectedPage(page)) return false;
		await _viewModel.NavigateAsync(item);
		return ReferenceEquals(_viewModel.SelectedNavigationItem, item) && ReferenceEquals(module.SelectedPage, page);
	}

	private bool HasModulePage(string moduleName, string pageName)
	{
		var item = _viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName);
		return item?.Content is ShellModuleViewModel module && module.Pages.Any(candidate => candidate.Name == pageName);
	}

	private void Remember(ShellPaletteEntry entry)
	{
		if (entry.RecordKey is null) return;
		for (var index = _recentEntries.Count - 1; index >= 0; index--)
			if (string.Equals(_recentEntries[index].RecordKey, entry.RecordKey, StringComparison.Ordinal)) _recentEntries.RemoveAt(index);
		_recentEntries.Insert(0, entry with { Group = "Recent" });
		while (_recentEntries.Count > 8) _recentEntries.RemoveAt(_recentEntries.Count - 1);
	}

	private static List<ShellPaletteEntry> Filter(IEnumerable<ShellPaletteEntry> entries, string query)
	{
		if (string.IsNullOrWhiteSpace(query)) return entries.ToList();
		var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return entries.Where(entry => terms.All(term => $"{entry.Title} {entry.Subtitle} {entry.Group} {entry.TypeLabel}".Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
	}

	private void ApplyEntries(IReadOnlyList<ShellPaletteEntry> entries)
	{
		_updating = true;
		var view = new ListCollectionView(entries.ToList());
		view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShellPaletteEntry.Group)));
		ResultsList.ItemsSource = view;
		ResultsList.SelectedIndex = entries.Count > 0 ? 0 : -1;
		if (_mode == ShellPaletteMode.Commands) StatusText.Text = $"{entries.Count:N0} commands";
		_updating = false;
	}
}
