// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class SalesPricingViewModel
{
	private PricingStrategyRow? _selectedPricingStrategyRow;
	private string _pricingStrategySummary = "Load pricing to inspect the active cascade.";
	private Customer? _resolutionCustomer;
	private Item? _resolutionItem;
	private DateTime _resolutionDate = DateTime.Today;
	private string _resolutionCurrency = "EUR";
	private string _resolutionSummary = "Select an item and calculate a preview.";
	private SalesPriceList? _designerAssignmentTarget;
	private int _pricingWorkspaceModeIndex;

	public ObservableCollection<PricingStrategyRow> PricingStrategyRows { get; } = [];
	public ObservableCollection<PricingResolutionPreviewRow> ResolutionPreviewRows { get; } = [];
	public ObservableCollection<SalesPriceList> DesignerCustomerPriceLists { get; } = [];
	public AsyncRelayCommand? CalculateResolutionPreviewCommand { get; private set; }
	public RelayCommand? OpenStrategySelectionCommand { get; private set; }
	public AsyncRelayCommand? ApplyDesignerAssignmentCommand { get; private set; }
	public AsyncRelayCommand? ClearDesignerAssignmentCommand { get; private set; }

	public PricingStrategyRow? SelectedPricingStrategyRow
	{
		get => _selectedPricingStrategyRow;
		set
		{
			if (_selectedPricingStrategyRow == value) return;
			_selectedPricingStrategyRow = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(PricingStrategySelectionTitle));
			OnPropertyChanged(nameof(PricingStrategySelectionSubtitle));
			OnPropertyChanged(nameof(PricingStrategySelectionIdentity));
			OnPropertyChanged(nameof(HasDesignerCustomerSelection));
			DesignerAssignmentTarget = value?.PriceListId is long listId
				? DesignerCustomerPriceLists.FirstOrDefault(priceList => priceList.Id == listId)
				: null;
			OpenStrategySelectionCommand?.RaiseCanExecuteChanged();
			ApplyDesignerAssignmentCommand?.RaiseCanExecuteChanged();
			ClearDesignerAssignmentCommand?.RaiseCanExecuteChanged();
		}
	}

	public string PricingStrategySummary
	{
		get => _pricingStrategySummary;
		private set
		{
			if (_pricingStrategySummary == value) return;
			_pricingStrategySummary = value;
			OnPropertyChanged();
		}
	}

	public string PricingStrategySelectionTitle => SelectedPricingStrategyRow?.Title ?? "Select a strategy node";
	public string PricingStrategySelectionSubtitle => SelectedPricingStrategyRow?.Subtitle ?? "Inspect global, regional and customer-specific pricing.";
	public string PricingStrategySelectionIdentity => SelectedPricingStrategyRow is null
		? "No selection"
		: $"{SelectedPricingStrategyRow.Kind} · {SelectedPricingStrategyRow.State}";
	public bool HasDesignerCustomerSelection => SelectedPricingStrategyRow?.CustomerId is > 0;
	public bool CanManagePricingStrategy => _pricing.CanManage;

	public SalesPriceList? DesignerAssignmentTarget
	{
		get => _designerAssignmentTarget;
		set
		{
			if (_designerAssignmentTarget == value) return;
			_designerAssignmentTarget = value;
			OnPropertyChanged();
			ApplyDesignerAssignmentCommand?.RaiseCanExecuteChanged();
		}
	}

	public int PricingWorkspaceModeIndex
	{
		get => _pricingWorkspaceModeIndex;
		set { if (_pricingWorkspaceModeIndex == value) return; _pricingWorkspaceModeIndex = value; OnPropertyChanged(); }
	}


	public Customer? ResolutionCustomer
	{
		get => _resolutionCustomer;
		set
		{
			if (_resolutionCustomer == value) return;
			_resolutionCustomer = value;
			if (value is not null) ResolutionCurrency = value.Currency;
			OnPropertyChanged();
			InvalidateResolutionPreview();
		}
	}

	public Item? ResolutionItem
	{
		get => _resolutionItem;
		set { if (_resolutionItem == value) return; _resolutionItem = value; OnPropertyChanged(); InvalidateResolutionPreview(); }
	}

	public DateTime ResolutionDate
	{
		get => _resolutionDate;
		set { if (_resolutionDate.Date == value.Date) return; _resolutionDate = value.Date; OnPropertyChanged(); InvalidateResolutionPreview(); }
	}

	public string ResolutionCurrency
	{
		get => _resolutionCurrency;
		set { var normalized = value ?? string.Empty; if (_resolutionCurrency == normalized) return; _resolutionCurrency = normalized; OnPropertyChanged(); InvalidateResolutionPreview(); }
	}

	public string ResolutionSummary
	{
		get => _resolutionSummary;
		private set { if (_resolutionSummary == value) return; _resolutionSummary = value; OnPropertyChanged(); }
	}

	private void InitializePricingStrategyDesigner()
	{
		CalculateResolutionPreviewCommand = new AsyncRelayCommand(CalculateResolutionPreviewAsync, CanCalculateResolutionPreview);
		OpenStrategySelectionCommand = new RelayCommand(OpenStrategySelection, CanOpenStrategySelection);
		ApplyDesignerAssignmentCommand = new AsyncRelayCommand(ApplyDesignerAssignmentAsync, CanApplyDesignerAssignment);
		ClearDesignerAssignmentCommand = new AsyncRelayCommand(ClearDesignerAssignmentAsync, () => _pricing.CanManage && HasDesignerCustomerSelection);
	}

	private bool CanCalculateResolutionPreview() =>
		_pricing.CanView && _priceListGeneration?.CanPreview == true && ResolutionItem is not null && ResolutionCurrency.Trim().Length == 3;

	private async Task CalculateResolutionPreviewAsync(CancellationToken token)
	{
		if (_priceListGeneration is null || ResolutionItem is null) return;
		var currency = ResolutionCurrency.Trim().ToUpperInvariant();
		var costTask = _priceListGeneration.PreviewCostAsync(ResolutionItem.Id, ResolutionDate, token);
		var priceTask = _pricing.PreviewResolutionAsync(ResolutionCustomer?.Id, ResolutionItem.Id, ResolutionDate, currency, token);
		await Task.WhenAll(costTask, priceTask);
		var cost = await costTask;
		var price = await priceTask;
		var rows = new List<PricingResolutionPreviewRow>
		{
			new("Base cost", cost.BaseCostSource.ToString(), cost.IsSuccess ? cost.BaseCost : null, cost.Currency, cost.IsSuccess ? cost.BaseCostEvidenceType : cost.Error ?? "Not calculable", cost.IsSuccess, false),
			new("Calculated cost", "Cost components", cost.IsSuccess ? cost.CalculatedCost : null, cost.Currency, cost.IsSuccess ? $"{cost.Components.Count} active cost component{(cost.Components.Count == 1 ? string.Empty : "s")}" : cost.Error ?? "Not calculable", cost.IsSuccess, false)
		};
		AddResolutionPriceRow(rows, "Global", price.Global, price.Effective);
		AddResolutionPriceRow(rows, "Region", price.Region, price.Effective);
		AddResolutionPriceRow(rows, "Customer", price.Customer, price.Effective);
		Replace(ResolutionPreviewRows, rows);
		ResolutionSummary = price.Effective is null
			? $"No effective {currency} sales price on {ResolutionDate:yyyy-MM-dd}."
			: $"Effective price: {price.Effective.Currency} {price.Effective.UnitPrice:N2} · {price.Effective.PriceListName} ({price.Effective.Scope})";
	}

	private static void AddResolutionPriceRow(ICollection<PricingResolutionPreviewRow> rows, string layer, SalesPriceResult? value, SalesPriceResult? effective)
	{
		rows.Add(value is null
			? new(layer, "No valid candidate", null, string.Empty, "Inactive, unavailable, out of validity, unassigned or missing item price.", false, false)
			: new(layer, value.PriceListName, value.UnitPrice, value.Currency, value.DiscountPercent == 0m ? "No discount" : $"{value.DiscountPercent:N2}% discount", true, effective?.PriceListId == value.PriceListId));
	}

	private void InvalidateResolutionPreview()
	{
		ResolutionPreviewRows.Clear();
		ResolutionSummary = "Select an item and calculate a preview.";
		CalculateResolutionPreviewCommand?.RaiseCanExecuteChanged();
	}

	private bool CanOpenStrategySelection() =>
		SelectedPricingStrategyRow is { PriceListId: > 0 } or { RegionId: > 0 } or { CustomerId: > 0 };

	private void OpenStrategySelection()
	{
		var selection = SelectedPricingStrategyRow;
		if (selection is null) return;
		if (selection.PriceListId is long priceListId) SelectedPriceList = PriceLists.FirstOrDefault(value => value.Id == priceListId);
		if (selection.RegionId is long regionId) SelectedRegionDefinition = Regions.FirstOrDefault(value => value.Id == regionId);
		if (selection.CustomerId is long customerId) SelectedCustomer = Customers.FirstOrDefault(value => value.Id == customerId);
		PricingWorkspaceModeIndex = 1;
	}

	private bool CanApplyDesignerAssignment() =>
		_pricing.CanManage &&
		SelectedPricingStrategyRow?.CustomerId is > 0 &&
		DesignerAssignmentTarget is { Scope: SalesPriceListScope.Customer };

	private async Task ApplyDesignerAssignmentAsync(CancellationToken token)
	{
		if (SelectedPricingStrategyRow?.CustomerId is not long customerId || DesignerAssignmentTarget is null) return;
		var targetId = DesignerAssignmentTarget.Id;
		await _pricing.AssignCustomerAsync(customerId, targetId, token);
		await LoadAsync(token);
		SelectedPricingStrategyRow = PricingStrategyRows.FirstOrDefault(row => row.CustomerId == customerId && row.PriceListId == targetId)
			?? PricingStrategyRows.FirstOrDefault(row => row.CustomerId == customerId);
		CompleteOperation(false, "Customer pricing assignment updated");
	}

	private async Task ClearDesignerAssignmentAsync(CancellationToken token)
	{
		if (SelectedPricingStrategyRow?.CustomerId is not long customerId) return;
		await _pricing.AssignCustomerAsync(customerId, null, token);
		await LoadAsync(token);
		CompleteOperation(false, "Customer-specific pricing cleared");
	}

	private void DisposePricingStrategyDesigner()
	{
		CalculateResolutionPreviewCommand?.Dispose();
		ApplyDesignerAssignmentCommand?.Dispose();
		ClearDesignerAssignmentCommand?.Dispose();
	}


	private async Task LoadPricingStrategyAsync(CancellationToken token)
	{
		var selectedKey = SelectedPricingStrategyRow?.Key;
		var assignments = await _pricing.ListCustomerAssignmentsAsync(token);
		Replace(DesignerCustomerPriceLists, PriceLists.Where(value => value.Scope == SalesPriceListScope.Customer).OrderBy(value => value.Name));
		var projection = PricingStrategyProjector.Project(PriceLists, Regions, Customers, assignments);
		Replace(PricingStrategyRows, projection.Rows);
		SelectedPricingStrategyRow = selectedKey is null
			? PricingStrategyRows.FirstOrDefault()
			: PricingStrategyRows.FirstOrDefault(row => string.Equals(row.Key, selectedKey, StringComparison.Ordinal)) ?? PricingStrategyRows.FirstOrDefault();

		var globalCount = PriceLists.Count(value => value.Scope == SalesPriceListScope.Global);
		var regionalCount = PriceLists.Count(value => value.Scope == SalesPriceListScope.Region);
		var customerCount = PriceLists.Count(value => value.Scope == SalesPriceListScope.Customer);
		PricingStrategySummary = $"{globalCount} global · {regionalCount} regional · {customerCount} customer price lists · {assignments.Count} explicit assignments";
	}
}
