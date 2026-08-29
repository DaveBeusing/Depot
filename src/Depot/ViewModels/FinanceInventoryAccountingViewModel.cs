// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class FinanceInventoryAccountingViewModel : BaseViewModel, IDisposable
{
	private readonly FinanceInventoryAccountingService _accounting;
	private readonly FinanceInventoryCostingService _costing;
	private readonly FinanceInventoryMovementAccountingService _movementAccounting;
	private long _configurationId;
	private long _configurationVersion = 1;
	private long _policyId;
	private long _policyVersion = 1;
	private string _legalEntityId = string.Empty;
	private string _fiscalCalendarId = string.Empty;
	private string _currency = string.Empty;
	private string _goodsReceiptProfileId = string.Empty;
	private string _salesIssueProfileId = string.Empty;
	private bool _configurationActive = true;
	private string _inventoryControlAccountId = string.Empty;
	private string _adjustmentProfileId = string.Empty;
	private string _purchaseVarianceProfileId = string.Empty;
	private string _landedCostProfileId = string.Empty;
	private bool _policyActive = true;
	private string _inventoryCountReference = string.Empty;
	private string _supplierDocumentId = string.Empty;
	private DateTime _varianceReversalDate = DateTime.Today;
	private string _varianceReversalReason = string.Empty;
	private string _landedCostLayerIds = string.Empty;
	private string _landedCostCurrency = string.Empty;
	private string _landedCostAmount = string.Empty;
	private string _landedCostReference = string.Empty;
	private DateTime _landedCostDate = DateTime.Today;
	private FinanceLandedCostAllocationMethod _landedCostMethod = FinanceLandedCostAllocationMethod.ExistingValue;
	private string _landedCostReversalId = string.Empty;
	private DateTime _landedCostReversalDate = DateTime.Today;
	private string _landedCostReversalReason = string.Empty;
	private DateTime _reconciliationDate = DateTime.Today;
	private decimal _totalValuation;
	private int _totalQuantity;
	private FinanceInventoryReconciliationRun? _selectedReconciliation;
	private bool _disposed;

	public FinanceInventoryAccountingViewModel(
		FinanceInventoryAccountingService accounting,
		FinanceInventoryCostingService costing,
		FinanceInventoryMovementAccountingService movementAccounting)
	{
		_accounting = accounting;
		_costing = costing;
		_movementAccounting = movementAccounting;
		RefreshCommand = new AsyncRelayCommand(LoadAsync);
		SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync);
		SavePolicyCommand = new AsyncRelayCommand(SavePolicyAsync);
		ProcessInventoryCountCommand = new AsyncRelayCommand(ProcessInventoryCountAsync);
		ProcessPurchaseVarianceCommand = new AsyncRelayCommand(ProcessPurchaseVarianceAsync);
		ReversePurchaseVarianceCommand = new AsyncRelayCommand(ReversePurchaseVarianceAsync);
		AllocateLandedCostCommand = new AsyncRelayCommand(AllocateLandedCostAsync);
		ReverseLandedCostCommand = new AsyncRelayCommand(ReverseLandedCostAsync);
		ReconcileCommand = new AsyncRelayCommand(ReconcileAsync);
	}

	public ObservableCollection<FinanceInventoryValuationSummary> Valuation { get; } = [];
	public ObservableCollection<FinanceInventoryReconciliationRun> Reconciliations { get; } = [];
	public IReadOnlyList<FinanceInventoryValuationMethod> ValuationMethods { get; } = Enum.GetValues<FinanceInventoryValuationMethod>();
	public IReadOnlyList<FinanceLandedCostAllocationMethod> LandedCostMethods { get; } = Enum.GetValues<FinanceLandedCostAllocationMethod>();

	public AsyncRelayCommand RefreshCommand { get; }
	public AsyncRelayCommand SaveConfigurationCommand { get; }
	public AsyncRelayCommand SavePolicyCommand { get; }
	public AsyncRelayCommand ProcessInventoryCountCommand { get; }
	public AsyncRelayCommand ProcessPurchaseVarianceCommand { get; }
	public AsyncRelayCommand ReversePurchaseVarianceCommand { get; }
	public AsyncRelayCommand AllocateLandedCostCommand { get; }
	public AsyncRelayCommand ReverseLandedCostCommand { get; }
	public AsyncRelayCommand ReconcileCommand { get; }

	public string LegalEntityId { get => _legalEntityId; set => SetString(ref _legalEntityId, value); }
	public string FiscalCalendarId { get => _fiscalCalendarId; set => SetString(ref _fiscalCalendarId, value); }
	public string Currency { get => _currency; set => SetString(ref _currency, value); }
	public FinanceInventoryValuationMethod ValuationMethod { get; set; } = FinanceInventoryValuationMethod.Fifo;
	public string GoodsReceiptProfileId { get => _goodsReceiptProfileId; set => SetString(ref _goodsReceiptProfileId, value); }
	public string SalesIssueProfileId { get => _salesIssueProfileId; set => SetString(ref _salesIssueProfileId, value); }
	public bool ConfigurationActive { get => _configurationActive; set => SetBool(ref _configurationActive, value); }
	public string InventoryControlAccountId { get => _inventoryControlAccountId; set => SetString(ref _inventoryControlAccountId, value); }
	public string AdjustmentProfileId { get => _adjustmentProfileId; set => SetString(ref _adjustmentProfileId, value); }
	public string PurchaseVarianceProfileId { get => _purchaseVarianceProfileId; set => SetString(ref _purchaseVarianceProfileId, value); }
	public string LandedCostProfileId { get => _landedCostProfileId; set => SetString(ref _landedCostProfileId, value); }
	public bool PolicyActive { get => _policyActive; set => SetBool(ref _policyActive, value); }
	public string InventoryCountReference { get => _inventoryCountReference; set => SetString(ref _inventoryCountReference, value); }
	public string SupplierDocumentId { get => _supplierDocumentId; set => SetString(ref _supplierDocumentId, value); }
	public DateTime VarianceReversalDate { get => _varianceReversalDate; set => SetDate(ref _varianceReversalDate, value); }
	public string VarianceReversalReason { get => _varianceReversalReason; set => SetString(ref _varianceReversalReason, value); }
	public string LandedCostLayerIds { get => _landedCostLayerIds; set => SetString(ref _landedCostLayerIds, value); }
	public string LandedCostCurrency { get => _landedCostCurrency; set => SetString(ref _landedCostCurrency, value); }
	public string LandedCostAmount { get => _landedCostAmount; set => SetString(ref _landedCostAmount, value); }
	public string LandedCostReference { get => _landedCostReference; set => SetString(ref _landedCostReference, value); }
	public DateTime LandedCostDate { get => _landedCostDate; set => SetDate(ref _landedCostDate, value); }
	public FinanceLandedCostAllocationMethod LandedCostMethod { get => _landedCostMethod; set { if (_landedCostMethod == value) return; _landedCostMethod = value; OnPropertyChanged(); } }
	public string LandedCostReversalId { get => _landedCostReversalId; set => SetString(ref _landedCostReversalId, value); }
	public DateTime LandedCostReversalDate { get => _landedCostReversalDate; set => SetDate(ref _landedCostReversalDate, value); }
	public string LandedCostReversalReason { get => _landedCostReversalReason; set => SetString(ref _landedCostReversalReason, value); }
	public DateTime ReconciliationDate { get => _reconciliationDate; set => SetDate(ref _reconciliationDate, value); }
	public decimal TotalValuation { get => _totalValuation; private set { if (_totalValuation == value) return; _totalValuation = value; OnPropertyChanged(); } }
	public int TotalQuantity { get => _totalQuantity; private set { if (_totalQuantity == value) return; _totalQuantity = value; OnPropertyChanged(); } }
	public FinanceInventoryReconciliationRun? SelectedReconciliation { get => _selectedReconciliation; set { if (ReferenceEquals(_selectedReconciliation, value)) return; _selectedReconciliation = value; OnPropertyChanged(); } }

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading Inventory Accounting...");
		try
		{
			var configuration = await _accounting.GetConfigurationAsync(cancellationToken);
			if (configuration is not null) Apply(configuration);
			var policy = await _costing.GetPolicyAsync(cancellationToken);
			if (policy is not null) Apply(policy);
			Replace(Valuation, await _costing.GetValuationSummaryAsync(cancellationToken));
			Replace(Reconciliations, await _costing.GetRecentReconciliationsAsync(cancellationToken));
			TotalQuantity = Valuation.Sum(value => value.Quantity);
			TotalValuation = Valuation.Sum(value => value.TransactionValue);
			CompleteOperation(Valuation.Count == 0 && configuration is null, "Inventory Accounting loaded.");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Inventory Accounting could not be loaded."); }
	}

	private async Task SaveConfigurationAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving Inventory Accounting configuration...");
		try
		{
			var value = new FinanceInventoryAccountingConfiguration
			{
				Id = _configurationId,
				Version = _configurationVersion,
				LegalEntityId = ParseGuid(LegalEntityId, "legal entity"),
				FiscalCalendarId = ParseGuid(FiscalCalendarId, "fiscal calendar"),
				PurchaseOrderPriceCurrency = new CurrencyCode(Currency),
				ValuationMethod = ValuationMethod,
				GoodsReceiptPostingProfileId = ParseLong(GoodsReceiptProfileId, "goods-receipt posting profile"),
				SalesIssuePostingProfileId = ParseLong(SalesIssueProfileId, "sales-issue posting profile"),
				IsActive = ConfigurationActive
			};
			Apply(await _accounting.SaveConfigurationAsync(value, cancellationToken));
			CompleteOperation(false, "Inventory Accounting configuration saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Inventory Accounting configuration could not be saved."); }
	}

	private async Task SavePolicyAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving Inventory Accounting policy...");
		try
		{
			var value = new FinanceInventoryAccountingPolicy
			{
				Id = _policyId,
				Version = _policyVersion,
				InventoryControlAccountId = ParseGuid(InventoryControlAccountId, "inventory control account"),
				InventoryAdjustmentPostingProfileId = ParseLong(AdjustmentProfileId, "adjustment posting profile"),
				PurchaseVariancePostingProfileId = ParseLong(PurchaseVarianceProfileId, "purchase-variance posting profile"),
				LandedCostPostingProfileId = ParseLong(LandedCostProfileId, "landed-cost posting profile"),
				IsActive = PolicyActive
			};
			Apply(await _costing.SavePolicyAsync(value, cancellationToken));
			CompleteOperation(false, "Inventory Accounting policy saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Inventory Accounting policy could not be saved."); }
	}

	private async Task ProcessInventoryCountAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Processing inventory-count valuation...");
		try
		{
			var processed = await _movementAccounting.ProcessInventoryCountAsync(InventoryCountReference.Trim(), cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(false, $"Inventory-count valuation processed ({processed} accounting event(s)).");
		}
		catch (Exception exception) { FailOperation(exception, "Inventory-count valuation failed."); }
	}

	private async Task ProcessPurchaseVarianceAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Processing purchase-price variance...");
		try
		{
			var result = await _costing.ProcessPurchaseVarianceAsync(ParseLong(SupplierDocumentId, "supplier document"), cancellationToken);
			CompleteOperation(false, result is null ? "No purchase-price variance was required." : $"Purchase-price variance {result.SignedVarianceAmount:N2} {result.Currency} posted.");
		}
		catch (Exception exception) { FailOperation(exception, "Purchase-price variance failed."); }
	}

	private async Task ReversePurchaseVarianceAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Reversing purchase-price variance...");
		try
		{
			await _costing.ReversePurchaseVarianceAsync(ParseLong(SupplierDocumentId, "supplier document"), Guid.NewGuid(), DateOnly.FromDateTime(VarianceReversalDate), VarianceReversalReason, cancellationToken);
			CompleteOperation(false, "Purchase-price variance reversed.");
		}
		catch (Exception exception) { FailOperation(exception, "Purchase-price variance reversal failed."); }
	}

	private async Task AllocateLandedCostAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Allocating landed cost...");
		try
		{
			var layerIds = ParseIds(LandedCostLayerIds);
			var amount = ParseDecimal(LandedCostAmount, "landed-cost amount");
			var result = await _costing.AllocateLandedCostAsync(new FinanceInventoryLandedCostRequest
			{
				OperationId = Guid.NewGuid(),
				PostingDate = DateOnly.FromDateTime(LandedCostDate),
				Currency = new CurrencyCode(LandedCostCurrency),
				Amount = amount,
				AllocationMethod = LandedCostMethod,
				LayerIds = layerIds,
				Reference = LandedCostReference
			}, cancellationToken);
			LandedCostReversalId = result.Id.ToString(CultureInfo.InvariantCulture);
			await LoadAsync(cancellationToken);
			CompleteOperation(false, $"Landed cost {result.Amount:N2} {result.Currency} allocated.");
		}
		catch (Exception exception) { FailOperation(exception, "Landed-cost allocation failed."); }
	}

	private async Task ReverseLandedCostAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Reversing landed cost...");
		try
		{
			await _costing.ReverseLandedCostAsync(ParseLong(LandedCostReversalId, "landed-cost operation"), Guid.NewGuid(), DateOnly.FromDateTime(LandedCostReversalDate), LandedCostReversalReason, cancellationToken);
			await LoadAsync(cancellationToken);
			CompleteOperation(false, "Landed-cost operation reversed.");
		}
		catch (Exception exception) { FailOperation(exception, "Landed-cost reversal failed."); }
	}

	private async Task ReconcileAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Reconciling inventory valuation to General Ledger...");
		try
		{
			var run = await _costing.ReconcileAsync(new FinanceInventoryReconciliationRequest { OperationId = Guid.NewGuid(), AsOfDate = DateOnly.FromDateTime(ReconciliationDate) }, cancellationToken);
			SelectedReconciliation = run;
			await LoadAsync(cancellationToken);
			SelectedReconciliation = Reconciliations.FirstOrDefault(value => value.Id == run.Id) ?? run;
			CompleteOperation(false, $"Reconciliation complete. Difference: {run.Difference:N2} {run.ReportingCurrency}.");
		}
		catch (Exception exception) { FailOperation(exception, "Inventory reconciliation failed."); }
	}

	private void Apply(FinanceInventoryAccountingConfiguration value)
	{
		_configurationId = value.Id;
		_configurationVersion = value.Version;
		LegalEntityId = value.LegalEntityId.ToString("D");
		FiscalCalendarId = value.FiscalCalendarId.ToString("D");
		Currency = value.PurchaseOrderPriceCurrency.Value;
		ValuationMethod = value.ValuationMethod;
		OnPropertyChanged(nameof(ValuationMethod));
		GoodsReceiptProfileId = value.GoodsReceiptPostingProfileId.ToString(CultureInfo.InvariantCulture);
		SalesIssueProfileId = value.SalesIssuePostingProfileId.ToString(CultureInfo.InvariantCulture);
		ConfigurationActive = value.IsActive;
		if (string.IsNullOrWhiteSpace(LandedCostCurrency)) LandedCostCurrency = Currency;
	}

	private void Apply(FinanceInventoryAccountingPolicy value)
	{
		_policyId = value.Id;
		_policyVersion = value.Version;
		InventoryControlAccountId = value.InventoryControlAccountId.ToString("D");
		AdjustmentProfileId = value.InventoryAdjustmentPostingProfileId.ToString(CultureInfo.InvariantCulture);
		PurchaseVarianceProfileId = value.PurchaseVariancePostingProfileId.ToString(CultureInfo.InvariantCulture);
		LandedCostProfileId = value.LandedCostPostingProfileId.ToString(CultureInfo.InvariantCulture);
		PolicyActive = value.IsActive;
	}

	private static long ParseLong(string value, string name) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0 ? result : throw new ArgumentException($"A valid {name} ID is required.");
	private static Guid ParseGuid(string value, string name) => Guid.TryParse(value, out var result) && result != Guid.Empty ? result : throw new ArgumentException($"A valid {name} ID is required.");
	private static decimal ParseDecimal(string value, string name) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var result) && result > 0m ? result : throw new ArgumentException($"A positive {name} is required.");
	private static IReadOnlyList<long> ParseIds(string value)
	{
		var ids = value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(token => ParseLong(token, "valuation layer")).Distinct().OrderBy(id => id).ToArray();
		if (ids.Length == 0) throw new ArgumentException("At least one valuation-layer ID is required.");
		return ids;
	}

	private void SetString(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { value ??= string.Empty; if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private void SetBool(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private void SetDate(ref DateTime field, DateTime value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) { if (field == value) return; field = value; OnPropertyChanged(propertyName); }
	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RefreshCommand.Dispose();
		SaveConfigurationCommand.Dispose();
		SavePolicyCommand.Dispose();
		ProcessInventoryCountCommand.Dispose();
		ProcessPurchaseVarianceCommand.Dispose();
		ReversePurchaseVarianceCommand.Dispose();
		AllocateLandedCostCommand.Dispose();
		ReverseLandedCostCommand.Dispose();
		ReconcileCommand.Dispose();
	}
}
