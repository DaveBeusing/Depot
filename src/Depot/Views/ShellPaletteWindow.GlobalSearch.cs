// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.Services;
using Depot.Services.Help;
using Depot.ViewModels;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Suppliers;

namespace Depot.Views;

public partial class ShellPaletteWindow
{
	private const string JournalIcon = "M4,2 L16,2 L16,18 L4,18 Z M7,6 L13,6 M7,9 L13,9 M7,12 L11,12";

	private GlobalSearchService? _globalSearch;
	private IHelpService? _globalSearchHelp;

	public ShellPaletteWindow(
		MainViewModel viewModel,
		Func<Task> openNotifications,
		Func<Task> openHelp,
		Func<Task> openUser,
		IList<ShellPaletteEntry> recentEntries,
		GlobalSearchService globalSearch,
		IHelpService helpService)
	{
		_viewModel = viewModel;
		_mode = ShellPaletteMode.QuickOpen;
		_openNotifications = openNotifications;
		_openHelp = openHelp;
		_openUser = openUser;
		_recentEntries = recentEntries;
		_globalSearch = globalSearch ?? throw new ArgumentNullException(nameof(globalSearch));
		_globalSearchHelp = helpService ?? throw new ArgumentNullException(nameof(helpService));

		InitializeComponent();
		SearchBox.TextChanged -= OnSearchTextChanged;
		SearchBox.TextChanged += OnGlobalSearchTextChanged;
		Loaded += OnGlobalSearchLoaded;
		Closed += (_, _) =>
		{
			_searchCancellation?.Cancel();
			_searchCancellation?.Dispose();
		};
	}

	private async void OnGlobalSearchLoaded(object sender, RoutedEventArgs e)
	{
		SearchBox.Focus();
		Keyboard.Focus(SearchBox);
		ShortcutHint.Text = "Ctrl+K";
		PromptGlyph.Text = "⌕";
		SearchBox.ToolTip = "Search commands, workspaces, help and records";
		await RefreshGlobalSearchAsync(string.Empty);
	}

	private async void OnGlobalSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating) return;
		await RefreshGlobalSearchAsync(SearchBox.Text);
	}

	private async Task RefreshGlobalSearchAsync(string query)
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = new CancellationTokenSource();
		var token = _searchCancellation.Token;
		var text = query.Trim();
		var immediateEntries = BuildImmediateEntries(text);
		ApplyEntries(immediateEntries);

		if (text.Length == 0)
		{
			StatusText.Text = _recentEntries.Count > 0
				? "Suggested · commands · workspaces · records · recent"
				: "Suggested · commands · workspaces · type to search records and help";
			return;
		}

		if (text.Length < GlobalSearchService.MinimumQueryLength)
		{
			StatusText.Text = "Commands and workspaces are ready · type at least 2 characters for records";
			return;
		}

		StatusText.Text = "Searching records and help…";
		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(120), token);
			var entityTask = _globalSearch!.SearchAsync(text, 24, token);
			var helpTask = _globalSearchHelp!.SearchAsync(text, null, token);
			await Task.WhenAll(entityTask, helpTask);
			token.ThrowIfCancellationRequested();

			var entries = new List<ShellPaletteEntry>(immediateEntries);
			entries.AddRange(helpTask.Result.Take(6).Select(topic =>
				new ShellPaletteEntry(
					topic.Definition.Title,
					topic.Definition.Category,
					"Suggested",
					"HELP",
					HelpIcon,
					() => _viewModel.OpenHelpAsync(topic.Definition.Id),
					$"help:{topic.Definition.Id}")));
			entries.AddRange(entityTask.Result.Select(CreateEntityEntry));

			var finalEntries = DistinctEntries(entries, 42);
			ApplyEntries(finalEntries);
			StatusText.Text = finalEntries.Count == 0 ? "No matching commands, workspaces, help or records" : $"{finalEntries.Count:N0} results";
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			if (!token.IsCancellationRequested) StatusText.Text = $"Record search unavailable: {exception.Message}";
		}
	}

	private List<ShellPaletteEntry> BuildImmediateEntries(string query)
	{
		var entries = new List<ShellPaletteEntry>();
		if (query.Length == 0) entries.AddRange(_recentEntries.Take(8));
		entries.AddRange(Filter(BuildNavigationEntries(), query).Take(12));
		entries.AddRange(BuildCommandEntries(query).Take(query.Length == 0 ? 6 : 10));
		return DistinctEntries(entries, 24);
	}

	private ShellPaletteEntry CreateEntityEntry(GlobalSearchResult result)
	{
		var icon = result.Kind switch
		{
			GlobalSearchResultKind.Item => ItemIcon,
			GlobalSearchResultKind.Customer => CustomerIcon,
			GlobalSearchResultKind.Supplier => SupplierIcon,
			GlobalSearchResultKind.SalesOrder => SalesOrderIcon,
			GlobalSearchResultKind.PurchaseOrder => PurchaseOrderIcon,
			GlobalSearchResultKind.Invoice => InvoiceIcon,
			GlobalSearchResultKind.JournalEntry => JournalIcon,
			GlobalSearchResultKind.Lead or GlobalSearchResultKind.Opportunity => SalesOrderIcon,
			_ => WorkspaceIcon
		};
		return new ShellPaletteEntry(
			result.Title,
			result.Subtitle,
			"Records",
			result.TypeLabel,
			icon,
			() => OpenGlobalResultAsync(result),
			result.StableId);
	}

	private async Task OpenGlobalResultAsync(GlobalSearchResult result)
	{
		switch (result.Kind)
		{
			case GlobalSearchResultKind.Item:
				await _viewModel.NavigateToRouteAsync(ShellRoutes.Inventory.Items);
				var items = _viewModel.ItemsViewModel;
				var selectedItem = items.Items.FirstOrDefault(item => item.Id == result.EntityId);
				if (selectedItem is null)
				{
					items.SearchText = result.Title;
					await items.LoadItemsAsync();
					selectedItem = items.Items.FirstOrDefault(item => item.Id == result.EntityId);
				}
				items.SelectedItem = selectedItem;
				break;

			case GlobalSearchResultKind.Supplier:
				await OpenSupplierResultAsync(result);
				break;

			case GlobalSearchResultKind.PurchaseOrder:
				await _viewModel.NavigateToRouteAsync(ShellRoutes.Purchasing.PurchaseOrders);
				await _viewModel.ProcurementViewModel.OpenOrderAsync(result.EntityId);
				break;

			case GlobalSearchResultKind.Customer:
				await _viewModel.OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Customer, result.EntityId, result.Title, result.Subtitle));
				break;

			case GlobalSearchResultKind.SalesOrder:
				await _viewModel.OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.SalesOrder, result.EntityId, result.Title, result.Subtitle));
				break;

			case GlobalSearchResultKind.Invoice:
				await _viewModel.OpenSalesQuickItemAsync(new SalesQuickOpenItem(SalesQuickOpenKind.Invoice, result.EntityId, result.Title, result.Subtitle));
				break;

			case GlobalSearchResultKind.JournalEntry:
				await _viewModel.NavigateToRouteAsync(new ShellRoute("finance.reporting"));
				break;

			case GlobalSearchResultKind.Lead:
				await _viewModel.NavigateToRouteAsync(ShellRoutes.Sales.Leads);
				await _viewModel.SalesLeadsViewModel.OpenAsync(result.EntityId);
				break;

			case GlobalSearchResultKind.Opportunity:
				await _viewModel.NavigateToRouteAsync(ShellRoutes.Sales.Opportunities);
				await _viewModel.SalesOpportunitiesViewModel.OpenAsync(result.EntityId);
				break;
		}
	}

	private async Task OpenSupplierResultAsync(GlobalSearchResult result)
	{
		await _viewModel.NavigateToRouteAsync(ShellRoutes.Administration);
		await _viewModel.AdministrationViewModel.NavigateToAsync(AdministrationSection.Suppliers);
		if (_viewModel.AdministrationViewModel.CurrentViewModel is not SupplierViewModel suppliers) return;
		suppliers.SearchText = result.Title;
		await suppliers.LoadAsync();
		suppliers.SelectedSupplier = suppliers.Suppliers.FirstOrDefault(candidate => candidate.Id == result.EntityId);
	}

	private static List<ShellPaletteEntry> DistinctEntries(IEnumerable<ShellPaletteEntry> entries, int maximum)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<ShellPaletteEntry>();
		foreach (var entry in entries)
		{
			var key = entry.RecordKey ?? $"{entry.TypeLabel}|{entry.Group}|{entry.Title}";
			if (!seen.Add(key)) continue;
			result.Add(entry);
			if (result.Count >= maximum) break;
		}
		return result;
	}
}
