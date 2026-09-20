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

	public ObservableCollection<PricingStrategyRow> PricingStrategyRows { get; } = [];
	public ObservableCollection<PricingResolutionPreviewRow> ResolutionPreviewRows { get; } = [];
	public AsyncRelayCommand? CalculateResolutionPreviewCommand { get; private set; }

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

	private void DisposePricingStrategyDesigner() => CalculateResolutionPreviewCommand?.Dispose();

	private async Task LoadPricingStrategyAsync(CancellationToken token)
	{
		var selectedKey = SelectedPricingStrategyRow?.Key;
		var assignments = await _pricing.ListCustomerAssignmentsAsync(token);
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
