// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class SubscriptionBillingViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 200;
	private readonly SubscriptionBillingService _service;
	private readonly CustomerService _customers;
	private readonly ItemService _items;
	public BusinessAttachmentPanelViewModel Attachments { get; }
	private SubscriptionContract? _selectedContract;
	private SubscriptionBillingInstance? _selectedBillingInstance;
	private Customer? _selectedCustomer;
	private Item? _selectedItem;
	private SubscriptionLineEditor? _selectedLine;
	private string _searchText = string.Empty;
	private long _version;
	private string _contractNumber = "New subscription";
	private string _legalEntityId = string.Empty;
	private string _currency = "EUR";
	private DateTime _startDate = DateTime.Today;
	private DateTime? _endDate;
	private SubscriptionBillingCadence _cadence = SubscriptionBillingCadence.Monthly;
	private string _notes = string.Empty;
	private int _quantity = 1;
	private decimal _unitPrice;
	private decimal _discountPercent;
	private decimal _taxRate = 19m;
	private SubscriptionPricePolicy _pricePolicy = SubscriptionPricePolicy.FixedContractPrice;
	private bool _disposed;

	public SubscriptionBillingViewModel(SubscriptionBillingService service, CustomerService customers, ItemService items, BusinessAttachmentService attachmentService, IFileDialogService fileDialogs)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		_customers = customers ?? throw new ArgumentNullException(nameof(customers));
		_items = items ?? throw new ArgumentNullException(nameof(items));
		Attachments = new BusinessAttachmentPanelViewModel(attachmentService ?? throw new ArgumentNullException(nameof(attachmentService)), fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs)));
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		NewContractCommand = new RelayCommand(NewContract, () => _service.CanManage);
		SaveCommand = new AsyncRelayCommand(SaveAsync, () => _service.CanManage);
		AddLineCommand = new RelayCommand(AddLine, () => _service.CanManage && SelectedItem is not null);
		RemoveLineCommand = new RelayCommand(RemoveLine, () => _service.CanManage && SelectedLine is not null);
		ActivateCommand = new AsyncRelayCommand(ct => TransitionAsync("activate", ct), () => _service.CanManage && SelectedContract?.Status == SubscriptionContractStatus.Draft);
		PauseCommand = new AsyncRelayCommand(ct => TransitionAsync("pause", ct), () => _service.CanManage && SelectedContract?.Status == SubscriptionContractStatus.Active);
		ResumeCommand = new AsyncRelayCommand(ct => TransitionAsync("resume", ct), () => _service.CanManage && SelectedContract?.Status == SubscriptionContractStatus.Paused);
		CancelCommand = new AsyncRelayCommand(ct => TransitionAsync("cancel", ct), () => _service.CanManage && SelectedContract is { Status: SubscriptionContractStatus.Draft or SubscriptionContractStatus.Active or SubscriptionContractStatus.Paused });
		RefreshDueCommand = new AsyncRelayCommand(RefreshDueAsync, () => _service.CanGenerate);
		GenerateInvoiceCommand = new AsyncRelayCommand(GenerateInvoiceAsync, () => _service.CanGenerate && SelectedBillingInstance is { Status: SubscriptionBillingInstanceStatus.Due or SubscriptionBillingInstanceStatus.Blocked });
	}

	public ObservableCollection<SubscriptionContract> Contracts { get; } = [];
	public ObservableCollection<SubscriptionLineEditor> Lines { get; } = [];
	public ObservableCollection<SubscriptionBillingInstance> BillingHistory { get; } = [];
	public ObservableCollection<SubscriptionBillingInstance> DueBilling { get; } = [];
	public ObservableCollection<Customer> Customers { get; } = [];
	public ObservableCollection<Item> Items { get; } = [];
	public IReadOnlyList<SubscriptionBillingCadence> Cadences { get; } = Enum.GetValues<SubscriptionBillingCadence>();
	public IReadOnlyList<SubscriptionPricePolicy> PricePolicies { get; } = Enum.GetValues<SubscriptionPricePolicy>();

	public AsyncRelayCommand RefreshCommand { get; }
	public RelayCommand NewContractCommand { get; }
	public AsyncRelayCommand SaveCommand { get; }
	public RelayCommand AddLineCommand { get; }
	public RelayCommand RemoveLineCommand { get; }
	public AsyncRelayCommand ActivateCommand { get; }
	public AsyncRelayCommand PauseCommand { get; }
	public AsyncRelayCommand ResumeCommand { get; }
	public AsyncRelayCommand CancelCommand { get; }
	public AsyncRelayCommand RefreshDueCommand { get; }
	public AsyncRelayCommand GenerateInvoiceCommand { get; }

	public bool CanManage => _service.CanManage;
	public bool CanGenerate => _service.CanGenerate;
	public string SearchText { get => _searchText; set => Set(ref _searchText, value ?? string.Empty); }
	public string ContractNumber { get => _contractNumber; private set => Set(ref _contractNumber, value); }
	public string LegalEntityId { get => _legalEntityId; set => Set(ref _legalEntityId, value ?? string.Empty); }
	public string Currency { get => _currency; set => Set(ref _currency, value ?? string.Empty); }
	public DateTime StartDate { get => _startDate; set => Set(ref _startDate, value); }
	public DateTime? EndDate { get => _endDate; set => Set(ref _endDate, value); }
	public SubscriptionBillingCadence Cadence { get => _cadence; set => Set(ref _cadence, value); }
	public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }
	public int Quantity { get => _quantity; set => Set(ref _quantity, value); }
	public decimal UnitPrice { get => _unitPrice; set => Set(ref _unitPrice, value); }
	public decimal DiscountPercent { get => _discountPercent; set => Set(ref _discountPercent, value); }
	public decimal TaxRate { get => _taxRate; set => Set(ref _taxRate, value); }
	public SubscriptionPricePolicy PricePolicy { get => _pricePolicy; set => Set(ref _pricePolicy, value); }

	public Customer? SelectedCustomer
	{
		get => _selectedCustomer;
		set { if (ReferenceEquals(_selectedCustomer, value)) return; _selectedCustomer = value; OnPropertyChanged(); if (value is not null && _version == 0) Currency = value.Currency; RaiseState(); }
	}

	public Item? SelectedItem
	{
		get => _selectedItem;
		set { if (ReferenceEquals(_selectedItem, value)) return; _selectedItem = value; OnPropertyChanged(); RaiseState(); }
	}

	public SubscriptionLineEditor? SelectedLine
	{
		get => _selectedLine;
		set { if (ReferenceEquals(_selectedLine, value)) return; _selectedLine = value; OnPropertyChanged(); RaiseState(); }
	}

	public SubscriptionContract? SelectedContract
	{
		get => _selectedContract;
		set
		{
			if (ReferenceEquals(_selectedContract, value)) return;
			_selectedContract = value;
			OnPropertyChanged();
			if (value is null) ResetEditor(); else Apply(value);
			_ = LoadHistoryAsync(value?.Id ?? 0, CancellationToken.None);
			_ = Attachments.SetTargetAsync(BusinessAttachmentEntityKind.SubscriptionContract, value?.Id);
			RaiseState();
		}
	}

	public SubscriptionBillingInstance? SelectedBillingInstance
	{
		get => _selectedBillingInstance;
		set { if (ReferenceEquals(_selectedBillingInstance, value)) return; _selectedBillingInstance = value; OnPropertyChanged(); RaiseState(); }
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading recurring billing");
		try
		{
			var contractsTask = _service.SearchAsync(SearchText, null, 1, PageSize, cancellationToken);
			var dueTask = _service.ListDueAsync(DateOnly.FromDateTime(DateTime.Today), PageSize, cancellationToken);
			var customersTask = _customers.ListActiveAsync(cancellationToken);
			var itemsTask = _items.SearchItemsAsync(null, true, 1, PageSize, cancellationToken);
			await Task.WhenAll(contractsTask, dueTask, customersTask, itemsTask);
			Replace(Contracts, (await contractsTask).Items);
			Replace(DueBilling, await dueTask);
			Replace(Customers, await customersTask);
			Replace(Items, (await itemsTask).Items);
			if (SelectedContract is { } selected)
			{
				var reloaded = Contracts.FirstOrDefault(value => value.Id == selected.Id);
				if (reloaded is not null) { _selectedContract = reloaded; OnPropertyChanged(nameof(SelectedContract)); Apply(reloaded); await LoadHistoryAsync(reloaded.Id, cancellationToken); }
			}
			CompleteOperation(Contracts.Count == 0, "Recurring billing loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Recurring billing could not be loaded"); }
		RaiseState();
	}

	public async Task OpenBillingInstanceAsync(long billingInstanceId, CancellationToken cancellationToken = default)
	{
		await LoadAsync(cancellationToken);
		SelectedBillingInstance = DueBilling.FirstOrDefault(value => value.Id == billingInstanceId);
		if (SelectedBillingInstance is { } instance)
			SelectedContract = Contracts.FirstOrDefault(value => value.Id == instance.ContractId);
	}

	private void NewContract()
	{
		_selectedContract = null;
		OnPropertyChanged(nameof(SelectedContract));
		ResetEditor();
		RequestEditorFocus();
		RaiseState();
	}

	private void ResetEditor()
	{
		_version = 0; ContractNumber = "New subscription"; SelectedCustomer = null; LegalEntityId = string.Empty; Currency = "EUR";
		StartDate = DateTime.Today; EndDate = null; Cadence = SubscriptionBillingCadence.Monthly; Notes = string.Empty;
		Lines.Clear(); BillingHistory.Clear();
	}

	private void Apply(SubscriptionContract value)
	{
		_version = value.Version; ContractNumber = value.ContractNumber;
		SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == value.CustomerId);
		LegalEntityId = value.LegalEntityId.ToString("D"); Currency = value.Currency;
		StartDate = value.StartDate.ToDateTime(TimeOnly.MinValue); EndDate = value.EndDate?.ToDateTime(TimeOnly.MinValue);
		Cadence = value.Cadence; Notes = value.Notes ?? string.Empty;
		Lines.Clear(); foreach (var line in value.Lines) Lines.Add(SubscriptionLineEditor.From(line));
	}

	private void AddLine()
	{
		if (SelectedItem is not { } item) return;
		Lines.Add(new SubscriptionLineEditor { ItemId = item.Id, PartNumber = item.PartNumber, Description = item.Description, Quantity = Math.Max(1, Quantity), UnitPrice = Math.Max(0m, UnitPrice), DiscountPercent = Math.Clamp(DiscountPercent, 0m, 100m), TaxRate = Math.Max(0m, TaxRate), PricePolicy = PricePolicy });
		SelectedLine = Lines[^1];
	}

	private void RemoveLine() { if (SelectedLine is null) return; Lines.Remove(SelectedLine); SelectedLine = null; }

	private async Task SaveAsync(CancellationToken cancellationToken)
	{
		if (SelectedCustomer is null) { FailOperation(new InvalidOperationException("Select a customer."), "Subscription contract could not be saved"); return; }
		if (!Guid.TryParse(LegalEntityId, out var legalEntityId)) { FailOperation(new InvalidOperationException("Enter a valid legal-entity GUID."), "Subscription contract could not be saved"); return; }
		BeginOperation("Saving subscription contract");
		try
		{
			var source = SelectedContract;
			var saved = await _service.SaveAsync(new SubscriptionContract
			{
				Id = source?.Id ?? 0, Version = _version, CustomerId = SelectedCustomer.Id, LegalEntityId = legalEntityId, Currency = Currency,
				StartDate = DateOnly.FromDateTime(StartDate), EndDate = EndDate is null ? null : DateOnly.FromDateTime(EndDate.Value), Cadence = Cadence,
				Notes = Notes, Lines = Lines.Select((line, index) => line.ToModel(index + 1)).ToArray()
			}, cancellationToken: cancellationToken);
			await LoadAsync(cancellationToken);
			SelectedContract = Contracts.FirstOrDefault(value => value.Id == saved.Id);
			CompleteOperation(false, "Subscription contract saved");
		}
		catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Subscription contract could not be saved"); }
		RaiseState();
	}

	private async Task TransitionAsync(string action, CancellationToken cancellationToken)
	{
		if (SelectedContract is not { } current) return;
		BeginOperation($"{action} subscription contract");
		try
		{
			var date = DateOnly.FromDateTime(DateTime.Today);
			var updated = action switch
			{
				"activate" => await _service.ActivateAsync(current.Id, current.Version, date, cancellationToken),
				"pause" => await _service.PauseAsync(current.Id, current.Version, date, cancellationToken),
				"resume" => await _service.ResumeAsync(current.Id, current.Version, date, cancellationToken),
				"cancel" => await _service.CancelAsync(current.Id, current.Version, date, cancellationToken),
				_ => throw new InvalidOperationException("Unsupported subscription lifecycle action.")
			};
			await LoadAsync(cancellationToken);
			SelectedContract = Contracts.FirstOrDefault(value => value.Id == updated.Id);
			CompleteOperation(false, $"Subscription contract {action} completed");
		}
		catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, $"Subscription contract {action} failed"); }
		RaiseState();
	}

	private async Task RefreshDueAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Calculating due billing periods");
		try
		{
			Replace(DueBilling, await _service.EnsureDueInstancesAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken));
			CompleteOperation(DueBilling.Count == 0, DueBilling.Count == 0 ? "No billing periods are due" : $"{DueBilling.Count:N0} billing period(s) due");
		}
		catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Due billing periods could not be calculated"); }
		RaiseState();
	}

	private async Task GenerateInvoiceAsync(CancellationToken cancellationToken)
	{
		if (SelectedBillingInstance is null) return;
		BeginOperation("Generating sales invoice draft");
		try
		{
			var result = await _service.GenerateInvoiceDraftAsync(SelectedBillingInstance.Id, cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(false, result.Invoice is null ? "Billing remains blocked" : $"Invoice {result.Invoice.InvoiceNumber} created as draft");
		}
		catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Sales invoice draft could not be generated"); }
		RaiseState();
	}

	private async Task LoadHistoryAsync(long contractId, CancellationToken cancellationToken)
	{
		BillingHistory.Clear(); if (contractId <= 0) return;
		try { Replace(BillingHistory, await _service.ListHistoryAsync(contractId, PageSize, cancellationToken)); }
		catch (OperationCanceledException) when(cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Billing history could not be loaded"); }
	}

	private void RaiseState()
	{
		NewContractCommand.RaiseCanExecuteChanged(); SaveCommand.RaiseCanExecuteChanged(); AddLineCommand.RaiseCanExecuteChanged(); RemoveLineCommand.RaiseCanExecuteChanged();
		ActivateCommand.RaiseCanExecuteChanged(); PauseCommand.RaiseCanExecuteChanged(); ResumeCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged();
		RefreshDueCommand.RaiseCanExecuteChanged(); GenerateInvoiceCommand.RaiseCanExecuteChanged();
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var value in source) target.Add(value); }

	public void Dispose()
	{
		if (_disposed) return; _disposed = true;
		Attachments.Dispose();
		RefreshCommand.Dispose(); SaveCommand.Dispose(); ActivateCommand.Dispose(); PauseCommand.Dispose(); ResumeCommand.Dispose(); CancelCommand.Dispose(); RefreshDueCommand.Dispose(); GenerateInvoiceCommand.Dispose();
	}
}

public sealed class SubscriptionLineEditor : BaseViewModel
{
	private long _itemId; private string _partNumber = string.Empty; private string _description = string.Empty; private int _quantity = 1;
	private decimal _unitPrice; private decimal _discountPercent; private decimal _taxRate = 19m; private SubscriptionPricePolicy _pricePolicy = SubscriptionPricePolicy.FixedContractPrice;
	public long ItemId { get => _itemId; set => Set(ref _itemId, value); }
	public string PartNumber { get => _partNumber; set => Set(ref _partNumber, value ?? string.Empty); }
	public string Description { get => _description; set => Set(ref _description, value ?? string.Empty); }
	public int Quantity { get => _quantity; set => Set(ref _quantity, value); }
	public decimal UnitPrice { get => _unitPrice; set => Set(ref _unitPrice, value); }
	public decimal DiscountPercent { get => _discountPercent; set => Set(ref _discountPercent, value); }
	public decimal TaxRate { get => _taxRate; set => Set(ref _taxRate, value); }
	public SubscriptionPricePolicy PricePolicy { get => _pricePolicy; set => Set(ref _pricePolicy, value); }
	public SubscriptionContractLine ToModel(int lineNumber) => new() { ItemId = ItemId, LineNumber = lineNumber, PartNumber = PartNumber, Description = Description, Quantity = Quantity, UnitPrice = UnitPrice, DiscountPercent = DiscountPercent, TaxRate = TaxRate, PricePolicy = PricePolicy, PriceSourceName = PricePolicy == SubscriptionPricePolicy.FixedContractPrice ? "Contract snapshot" : null };
	public static SubscriptionLineEditor From(SubscriptionContractLine line) => new() { ItemId = line.ItemId, PartNumber = line.PartNumber, Description = line.Description, Quantity = line.Quantity, UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent, TaxRate = line.TaxRate, PricePolicy = line.PricePolicy };
}
