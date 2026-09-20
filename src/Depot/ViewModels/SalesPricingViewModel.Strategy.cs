// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class SalesPricingViewModel
{
	private PricingStrategyRow? _selectedPricingStrategyRow;
	private string _pricingStrategySummary = "Load pricing to inspect the active cascade.";

	public ObservableCollection<PricingStrategyRow> PricingStrategyRows { get; } = [];

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
