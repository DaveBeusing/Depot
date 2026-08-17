// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Depot.Models;
using Depot.ViewModels;
using Depot.ViewModels.Administration;
using Depot.ViewModels.Suppliers;

namespace Depot.Views;

public enum ShellPaletteMode { Commands, QuickOpen }

public sealed record ShellPaletteEntry(string Title, string Subtitle, string Category, Func<Task> ExecuteAsync);

public partial class ShellPaletteWindow : Window
{
	private readonly MainViewModel _viewModel;
	private readonly ShellPaletteMode _mode;
	private readonly Func<Task> _openNotifications;
	private readonly Func<Task> _openHelp;
	private readonly Func<Task> _openUser;
	private CancellationTokenSource? _searchCancellation;
	private bool _updating;

	public ShellPaletteWindow(MainViewModel viewModel, ShellPaletteMode mode, Func<Task> openNotifications, Func<Task> openHelp, Func<Task> openUser)
	{
		_viewModel = viewModel;
		_mode = mode;
		_openNotifications = openNotifications;
		_openHelp = openHelp;
		_openUser = openUser;
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
		if (_mode == ShellPaletteMode.Commands) ApplyEntries(BuildCommandEntries(string.Empty));
		else await RefreshQuickOpenAsync(string.Empty);
	}

	private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
	{
		if (_updating) return;
		if (_mode == ShellPaletteMode.Commands) ApplyEntries(BuildCommandEntries(SearchBox.Text));
		else await RefreshQuickOpenAsync(SearchBox.Text);
	}

	private async void OnSearchKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
		if (e.Key == Key.Down) { if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = Math.Min(ResultsList.Items.Count - 1, ResultsList.SelectedIndex + 1); e.Handled = true; return; }
		if (e.Key == Key.Up) { if (ResultsList.Items.Count > 0) ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1); e.Handled = true; return; }
		if (e.Key == Key.Enter) { await ExecuteSelectedAsync(); e.Handled = true; }
	}

	private async void OnResultsDoubleClick(object sender, MouseButtonEventArgs e) => await ExecuteSelectedAsync();

	private async Task ExecuteSelectedAsync()
	{
		if (ResultsList.SelectedItem is not ShellPaletteEntry entry) return;
		Close();
		await entry.ExecuteAsync();
	}

	private IReadOnlyList<ShellPaletteEntry> BuildCommandEntries(string query)
	{
		var entries = BuildNavigationEntries();
		entries.Add(new("Open Notifications", "Open the notification center workspace", "Shell", _openNotifications));
		entries.Add(new("Open Help", "Open Depot help", "Shell", _openHelp));
		entries.Add(new("Open User", "Open the signed-in user workspace", "Shell", _openUser));
		entries.Add(new("Create Purchase Order", "Open Purchasing and start a new purchase order", "Action", async () => { await NavigateModulePageAsync("Purchasing", "Purchase Orders"); _viewModel.ProcurementViewModel.NewOrderCommand.Execute(null); }));
		entries.Add(new("Sign Out", "End the current Depot session", "Account", () => { _viewModel.LogoutCommand.Execute(null); return Task.CompletedTask; }));
		return Filter(entries, query);
	}

	private List<ShellPaletteEntry> BuildNavigationEntries()
	{
		var entries = new List<ShellPaletteEntry>();
		foreach (var item in _viewModel.NavigationItems)
		{
			entries.Add(new(item.Name, "Open workspace", "Workspace", () => _viewModel.NavigateAsync(item)));
			if (item.Content is ShellModuleViewModel module)
			{
				foreach (var page in module.Pages)
				{
					var capturedPage = page;
					entries.Add(new($"{item.Name}: {page.Name}", "Open workspace section", item.Name, async () => { module.SetSelectedPage(capturedPage); await _viewModel.NavigateAsync(item); }));
				}
			}
		}
		foreach (var adminItem in _viewModel.AdministrationViewModel.NavigationItems)
		{
			if (adminItem.Section is not AdministrationSection section) continue;
			entries.Add(new($"Administration: {adminItem.Name}", "Open administration section", "Administration", async () => { await NavigateTopLevelAsync("Administration"); await _viewModel.AdministrationViewModel.NavigateToAsync(section); }));
		}
		return entries;
	}

	private async Task RefreshQuickOpenAsync(string query)
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = new CancellationTokenSource();
		var token = _searchCancellation.Token;
		var entries = Filter(BuildNavigationEntries(), query).Take(10).ToList();
		if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
		{
			ApplyEntries(entries);
			StatusText.Text = "Type at least 2 characters to search records";
			return;
		}

		StatusText.Text = "Searching records…";
		try
		{
			var text = query.Trim();
			_viewModel.ItemsViewModel.SearchText = text;
			await _viewModel.ItemsViewModel.LoadItemsAsync(token);
			foreach (var item in _viewModel.ItemsViewModel.Items.Take(8))
			{
				var captured = item;
				entries.Add(new(captured.PartNumber, captured.Description, "Item", async () => { await NavigateModulePageAsync("Inventory", "Items"); _viewModel.ItemsViewModel.SelectedItem = captured; }));
			}

			_viewModel.ProcurementViewModel.Section = ProcurementSection.PurchaseOrders;
			_viewModel.ProcurementViewModel.SearchText = text;
			_viewModel.ProcurementViewModel.SupplierSearchText = text;
			await _viewModel.ProcurementViewModel.LoadAsync(token);
			foreach (var order in _viewModel.ProcurementViewModel.Orders.Take(8))
			{
				var captured = order;
				entries.Add(new(captured.OrderNumber, captured.SupplierName ?? "Purchase order", "Purchase Order", async () => { await NavigateModulePageAsync("Purchasing", "Purchase Orders"); await _viewModel.ProcurementViewModel.OpenOrderAsync(captured.Id); }));
			}
			foreach (var supplier in _viewModel.ProcurementViewModel.Suppliers.Take(6))
			{
				var captured = supplier;
				entries.Add(new(captured.Name, captured.AccountNumber == 0 ? "Supplier" : $"Account {captured.AccountNumber}", "Supplier", () => OpenSupplierAsync(captured, text)));
			}
			if (!token.IsCancellationRequested) { ApplyEntries(entries); StatusText.Text = $"{entries.Count:N0} results"; }
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested) { }
		catch (Exception exception) { if (!token.IsCancellationRequested) StatusText.Text = exception.Message; }
	}

	private async Task OpenSupplierAsync(Supplier supplier, string query)
	{
		await NavigateTopLevelAsync("Administration");
		await _viewModel.AdministrationViewModel.NavigateToAsync(AdministrationSection.Suppliers);
		if (_viewModel.AdministrationViewModel.CurrentViewModel is SupplierViewModel suppliers)
		{
			suppliers.SearchText = query;
			await suppliers.LoadAsync();
			suppliers.SelectedSupplier = suppliers.Suppliers.FirstOrDefault(candidate => candidate.Id == supplier.Id);
		}
	}

	private async Task NavigateTopLevelAsync(string name)
	{
		var item = _viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == name);
		if (item is not null) await _viewModel.NavigateAsync(item);
	}

	private async Task NavigateModulePageAsync(string moduleName, string pageName)
	{
		var item = _viewModel.NavigationItems.FirstOrDefault(candidate => candidate.Name == moduleName);
		if (item is null) return;
		if (item.Content is ShellModuleViewModel module && module.Pages.FirstOrDefault(candidate => candidate.Name == pageName) is { } page) module.SetSelectedPage(page);
		await _viewModel.NavigateAsync(item);
	}

	private static List<ShellPaletteEntry> Filter(IEnumerable<ShellPaletteEntry> entries, string query)
	{
		if (string.IsNullOrWhiteSpace(query)) return entries.ToList();
		var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return entries.Where(entry => terms.All(term => $"{entry.Title} {entry.Subtitle} {entry.Category}".Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
	}

	private void ApplyEntries(IReadOnlyList<ShellPaletteEntry> entries)
	{
		_updating = true;
		ResultsList.ItemsSource = entries;
		ResultsList.SelectedIndex = entries.Count > 0 ? 0 : -1;
		if (_mode == ShellPaletteMode.Commands) StatusText.Text = $"{entries.Count:N0} commands";
		_updating = false;
	}
}
