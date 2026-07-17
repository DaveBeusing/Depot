// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Suppliers;

public sealed class SupplierViewModel : BaseViewModel, IDisposable
{
	private readonly SupplierService _supplierService;
	private readonly SupplierItemService _supplierItemService;
	private readonly SupplierCategoryService _categoryService;
	private readonly ItemService _itemService;
	private readonly AsyncDebouncer _supplierSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _supplierItemSearch = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _itemSearch = new(TimeSpan.FromMilliseconds(300));
	private Supplier? _selectedSupplier;
	private SupplierItem? _selectedSupplierItem;
	private Item? _selectedItemOption;
	private Supplier _draft = NewSupplierDraft();
	private SupplierItem _supplierItemDraft = NewSupplierItemDraft();
	private string _searchText = string.Empty;
	private string _supplierItemSearchText = string.Empty;
	private string _itemSearchText = string.Empty;

	public SupplierViewModel(SupplierService supplierService, SupplierItemService supplierItemService, SupplierCategoryService categoryService, ItemService itemService)
	{
		_supplierService = supplierService;
		_supplierItemService = supplierItemService;
		_categoryService = categoryService;
		_itemService = itemService;
		NewSupplierCommand = new RelayCommand(NewSupplier);
		SaveSupplierCommand = new AsyncRelayCommand(SaveSupplierAsync);
		ToggleSupplierCommand = new AsyncRelayCommand(ToggleSupplierAsync, () => SelectedSupplier is not null);
		OpenUrlCommand = new RelayCommand(OpenUrl);
		NewSupplierItemCommand = new RelayCommand(NewSupplierItem, () => SelectedSupplier?.IsActive == true);
		SaveSupplierItemCommand = new AsyncRelayCommand(SaveSupplierItemAsync, () => SelectedSupplier?.IsActive == true);
		ToggleSupplierItemCommand = new AsyncRelayCommand(ToggleSupplierItemAsync, () => SelectedSupplierItem is not null);
	}

	public ObservableCollection<Supplier> Suppliers { get; } = new();
	public ObservableCollection<ItemReferenceData> Categories { get; } = new();
	public ObservableCollection<SupplierItem> SupplierItems { get; } = new();
	public ObservableCollection<Item> ItemOptions { get; } = new();
	public RelayCommand NewSupplierCommand { get; }
	public AsyncRelayCommand SaveSupplierCommand { get; }
	public AsyncRelayCommand ToggleSupplierCommand { get; }
	public RelayCommand OpenUrlCommand { get; }
	public RelayCommand NewSupplierItemCommand { get; }
	public AsyncRelayCommand SaveSupplierItemCommand { get; }
	public AsyncRelayCommand ToggleSupplierItemCommand { get; }

	public Supplier Draft { get => _draft; private set { _draft = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccountNumberText)); OnPropertyChanged(nameof(SupplierEditorTitle)); OpenUrlCommand.RaiseCanExecuteChanged(); } }
	public string AccountNumberText => Draft.AccountNumber == 0 ? string.Empty : Draft.AccountNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
	public SupplierItem SupplierItemDraft { get => _supplierItemDraft; private set { _supplierItemDraft = value; OnPropertyChanged(); OnPropertyChanged(nameof(SupplierItemEditorTitle)); } }
	public string SupplierEditorTitle => Draft.Id == 0 ? "New Supplier" : "Supplier Details";
	public string SupplierItemEditorTitle => SupplierItemDraft.Id == 0 ? "New Supplier Item" : "Edit Supplier Item";
	public string SupplierActionText => SelectedSupplier?.IsActive == true ? "Deactivate" : "Activate";
	public string SupplierItemActionText => SelectedSupplierItem?.IsActive == true ? "Deactivate" : "Activate";
	public bool HasSelectedSupplier => SelectedSupplier is not null;

	public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); _ = _supplierSearch.DebounceAsync(LoadSuppliersAsync); } }
	public string SupplierItemSearchText { get => _supplierItemSearchText; set { if (_supplierItemSearchText == value) return; _supplierItemSearchText = value; OnPropertyChanged(); _ = _supplierItemSearch.DebounceAsync(LoadSupplierItemsAsync); } }
	public string ItemSearchText { get => _itemSearchText; set { if (_itemSearchText == value) return; _itemSearchText = value; OnPropertyChanged(); _ = _itemSearch.DebounceAsync(LoadItemOptionsAsync); } }

	public Supplier? SelectedSupplier
	{
		get => _selectedSupplier;
		set
		{
			if (_selectedSupplier == value) return;
			_selectedSupplier = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedSupplier)); OnPropertyChanged(nameof(SupplierActionText));
			Draft = value is null ? NewSupplierDraft() : Copy(value);
			ToggleSupplierCommand.RaiseCanExecuteChanged(); NewSupplierItemCommand.RaiseCanExecuteChanged(); SaveSupplierItemCommand.RaiseCanExecuteChanged();
			NewSupplierItem(); _ = LoadSupplierItemsAsync();
		}
	}

	public SupplierItem? SelectedSupplierItem
	{
		get => _selectedSupplierItem;
		set
		{
			if (_selectedSupplierItem == value) return;
			_selectedSupplierItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(SupplierItemActionText));
			SupplierItemDraft = value is null ? NewSupplierItemDraft() : Copy(value);
			if (value is null) SelectedItemOption = null;
			else
			{
				var option = ItemOptions.FirstOrDefault(item => item.Id == value.ItemId);
				if (option is null) { option = new Item { Id = value.ItemId, PartNumber = value.ItemPartNumber, Description = value.ItemDescription }; ItemOptions.Insert(0, option); }
				SelectedItemOption = option;
			}
			ToggleSupplierItemCommand.RaiseCanExecuteChanged();
		}
	}

	public Item? SelectedItemOption { get => _selectedItemOption; set { if (_selectedItemOption == value) return; _selectedItemOption = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading suppliers");
		try
		{
			var categoryTask = _categoryService.GetActiveAsync(cancellationToken);
			var supplierTask = _supplierService.SearchAsync(SearchText, cancellationToken);
			await Task.WhenAll(categoryTask, supplierTask);
			Categories.Clear(); foreach (var category in await categoryTask) Categories.Add(category);
			ReplaceSuppliers(await supplierTask);
			await LoadItemOptionsAsync(cancellationToken);
			CompleteOperation(Suppliers.Count == 0, $"{Suppliers.Count:N0} suppliers");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Suppliers could not be loaded"); }
	}

	private async Task LoadSuppliersAsync(CancellationToken cancellationToken)
	{
		try { ReplaceSuppliers(await _supplierService.SearchAsync(SearchText, cancellationToken)); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Suppliers could not be loaded"); }
	}

	private async Task LoadSupplierItemsAsync(CancellationToken cancellationToken = default)
	{
		SupplierItems.Clear();
		if (SelectedSupplier is null) return;
		try { foreach (var value in await _supplierItemService.SearchAsync(SelectedSupplier.Id, SupplierItemSearchText, cancellationToken)) SupplierItems.Add(value); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier items could not be loaded"); }
	}

	private async Task LoadItemOptionsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var page = await _itemService.SearchItemsAsync(ItemSearchText, 1, 50, cancellationToken);
			ItemOptions.Clear(); foreach (var item in page.Items) ItemOptions.Add(item);
			if (SupplierItemDraft.ItemId > 0) SelectedItemOption = ItemOptions.FirstOrDefault(item => item.Id == SupplierItemDraft.ItemId);
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Items could not be loaded"); }
	}

	private void NewSupplier() { SelectedSupplier = null; Draft = NewSupplierDraft(); }
	private void NewSupplierItem() { SelectedSupplierItem = null; SupplierItemDraft = NewSupplierItemDraft(); SelectedItemOption = null; }

	private async Task SaveSupplierAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving supplier");
		try { var saved = await _supplierService.SaveAsync(Copy(Draft), cancellationToken); ReplaceSupplier(saved); SelectedSupplier = saved; CompleteOperation(false, "Supplier saved"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier could not be saved"); }
	}

	private async Task ToggleSupplierAsync(CancellationToken cancellationToken)
	{
		if (SelectedSupplier is null) return;
		BeginOperation("Updating supplier status");
		try { var saved = await _supplierService.SetActiveAsync(SelectedSupplier.Id, SelectedSupplier.Version, !SelectedSupplier.IsActive, cancellationToken); ReplaceSupplier(saved); SelectedSupplier = saved; CompleteOperation(false, saved.IsActive ? "Supplier activated" : "Supplier deactivated"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier status could not be changed"); }
	}

	private async Task SaveSupplierItemAsync(CancellationToken cancellationToken)
	{
		if (SelectedSupplier is null) return;
		BeginOperation("Saving supplier item");
		try
		{
			var draft = Copy(SupplierItemDraft); draft.SupplierId = SelectedSupplier.Id; draft.ItemId = SelectedItemOption?.Id ?? 0;
			var saved = await _supplierItemService.SaveAsync(draft, cancellationToken);
			await LoadSupplierItemsAsync(cancellationToken); SelectedSupplierItem = SupplierItems.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, "Supplier item saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier item could not be saved"); }
	}

	private async Task ToggleSupplierItemAsync(CancellationToken cancellationToken)
	{
		if (SelectedSupplierItem is null) return;
		BeginOperation("Updating supplier item status");
		try { await _supplierItemService.SetActiveAsync(SelectedSupplierItem.Id, SelectedSupplierItem.Version, !SelectedSupplierItem.IsActive, cancellationToken); await LoadSupplierItemsAsync(cancellationToken); CompleteOperation(false, "Supplier item status updated"); }
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Supplier item status could not be changed"); }
	}

	private void OpenUrl()
	{
		if (!Uri.TryCreate(Draft.Url, UriKind.Absolute, out var uri)) return;
		try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
		catch (Exception exception) { FailOperation(exception, "Supplier URL could not be opened"); }
	}
	private void ReplaceSuppliers(IReadOnlyList<Supplier> values) { var selectedId = SelectedSupplier?.Id; Suppliers.Clear(); foreach (var value in values) Suppliers.Add(value); SelectedSupplier = Suppliers.FirstOrDefault(value => value.Id == selectedId); }
	private void ReplaceSupplier(Supplier value) { var existing = Suppliers.FirstOrDefault(item => item.Id == value.Id); if (existing is null) Suppliers.Add(value); else Suppliers[Suppliers.IndexOf(existing)] = value; }

	private static Supplier NewSupplierDraft() => new();
	private static SupplierItem NewSupplierItemDraft() => new() { MinimumOrderQuantity = 1 };
	private static Supplier Copy(Supplier s) => new() { Id=s.Id, AccountNumber=s.AccountNumber, CustomerNumber=s.CustomerNumber, Name=s.Name, Contact=s.Contact, Email=s.Email, Phone=s.Phone, Address=s.Address, RmaTerms=s.RmaTerms, Url=s.Url, PaymentTerm=s.PaymentTerm, Iban=s.Iban, AccountName=s.AccountName, SepaMandate=s.SepaMandate, VatNumber=s.VatNumber, SupplierCategoryId=s.SupplierCategoryId, SupplierCategoryName=s.SupplierCategoryName, Loyalty=s.Loyalty, Quality=s.Quality, Notes=s.Notes, IsActive=s.IsActive, Version=s.Version };
	private static SupplierItem Copy(SupplierItem s) => new() { Id=s.Id, SupplierId=s.SupplierId, ItemId=s.ItemId, ItemPartNumber=s.ItemPartNumber, ItemDescription=s.ItemDescription, SupplierPartNumber=s.SupplierPartNumber, PurchasePrice=s.PurchasePrice, LeadTimeDays=s.LeadTimeDays, MinimumOrderQuantity=s.MinimumOrderQuantity, IsPreferredSupplier=s.IsPreferredSupplier, IsActive=s.IsActive, Version=s.Version };

	public void Dispose() { _supplierSearch.Dispose(); _supplierItemSearch.Dispose(); _itemSearch.Dispose(); SaveSupplierCommand.Dispose(); ToggleSupplierCommand.Dispose(); SaveSupplierItemCommand.Dispose(); ToggleSupplierItemCommand.Dispose(); }
}
