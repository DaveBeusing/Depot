// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using Depot.Commands;
using Depot.Models;

namespace Depot.ViewModels;

public sealed partial class SalesPricingViewModel
{
	private SalesRegion? _selectedPriceListRegion;
	private SalesRegion? _selectedRegionDefinition;
	private SalesRegion _regionDraft = NewRegion();
	private string _customerPricingStatus = "Automatic · Global";

	public ObservableCollection<SalesRegion> Regions { get; } = [];
	public IReadOnlyList<SalesPriceListScope> Scopes { get; } = Enum.GetValues<SalesPriceListScope>();
	public RelayCommand NewRegionCommand { get; private set; } = null!;
	public AsyncRelayCommand SaveRegionCommand { get; private set; } = null!;
	public AsyncRelayCommand ClearCustomerAssignmentCommand { get; private set; } = null!;

	public SalesPriceListScope SelectedScope
	{
		get => Draft.Scope;
		set
		{
			if (Draft.Scope == value) return;
			Draft.Scope = value;
			if (value != SalesPriceListScope.Region) SelectedPriceListRegion = null;
			OnPropertyChanged(); OnPropertyChanged(nameof(IsRegionScope)); OnPropertyChanged(nameof(IsCustomerScope));
			SavePriceListCommand.RaiseCanExecuteChanged(); AssignCustomerCommand.RaiseCanExecuteChanged();
		}
	}
	public bool IsRegionScope => SelectedScope == SalesPriceListScope.Region;
	public bool IsCustomerScope => SelectedScope == SalesPriceListScope.Customer;
	public SalesRegion? SelectedPriceListRegion
	{
		get => _selectedPriceListRegion;
		set
		{
			if (_selectedPriceListRegion == value) return;
			_selectedPriceListRegion = value;
			Draft.RegionId = value?.Id;
			Draft.RegionName = value?.Name;
			OnPropertyChanged(); SavePriceListCommand.RaiseCanExecuteChanged();
		}
	}
	public SalesRegion? SelectedRegionDefinition
	{
		get => _selectedRegionDefinition;
		set { if (_selectedRegionDefinition == value) return; _selectedRegionDefinition = value; RegionDraft = value is null ? NewRegion() : CopyRegion(value); OnPropertyChanged(); }
	}
	public SalesRegion RegionDraft { get => _regionDraft; private set { _regionDraft = value; OnPropertyChanged(); SaveRegionCommand.RaiseCanExecuteChanged(); } }
	public string CustomerPricingStatus { get => _customerPricingStatus; private set { if (_customerPricingStatus == value) return; _customerPricingStatus = value; OnPropertyChanged(); } }

	private void InitializeScopedPricing()
	{
		NewRegionCommand = new RelayCommand(() => SelectedRegionDefinition = null, () => _pricing.CanManage);
		SaveRegionCommand = new AsyncRelayCommand(SaveRegionAsync, () => _pricing.CanManage);
		ClearCustomerAssignmentCommand = new AsyncRelayCommand(ClearCustomerAssignmentAsync, () => _pricing.CanManage && SelectedCustomer is not null);
	}

	private async Task LoadScopedPricingAsync(CancellationToken token)
	{
		var selectedId = SelectedRegionDefinition?.Id;
		Replace(Regions, await _pricing.ListRegionsAsync(token));
		SelectedRegionDefinition = selectedId is null ? null : Regions.FirstOrDefault(value => value.Id == selectedId);
		ApplyScopedPriceList(SelectedPriceList);
	}

	private void ApplyScopedPriceList(SalesPriceList? value)
	{
		_selectedPriceListRegion = value?.RegionId is null ? null : Regions.FirstOrDefault(region => region.Id == value.RegionId);
		OnPropertyChanged(nameof(SelectedPriceListRegion));
		OnPropertyChanged(nameof(SelectedScope)); OnPropertyChanged(nameof(IsRegionScope)); OnPropertyChanged(nameof(IsCustomerScope));
	}

	private async Task SaveRegionAsync(CancellationToken token)
	{
		var saved = await _pricing.SaveRegionAsync(RegionDraft, token);
		await LoadScopedPricingAsync(token);
		SelectedRegionDefinition = Regions.FirstOrDefault(value => value.Id == saved.Id);
		CompleteOperation(false, $"Sales region {saved.Name} saved");
	}

	private async Task ClearCustomerAssignmentAsync(CancellationToken token)
	{
		if (SelectedCustomer is null) return;
		await _pricing.AssignCustomerAsync(SelectedCustomer.Id, null, token);
		await LoadCustomerAssignmentAsync(token);
		CompleteOperation(false, $"Automatic pricing enabled for {SelectedCustomer.Name}");
	}

	private async Task LoadCustomerAssignmentAsync(CancellationToken token = default)
	{
		if (SelectedCustomer is null) { CustomerPricingStatus = "Automatic · Global"; return; }
		var assignment = await _pricing.GetCustomerAssignmentAsync(SelectedCustomer.Id, token);
		var automatic = SelectedCustomer.SalesRegionId is null ? "Automatic · Global" : $"Automatic · {SelectedCustomer.SalesRegionName ?? "Region"} → Global";
		CustomerPricingStatus = assignment switch
		{
			null => automatic,
			{ IsActive: false } => $"{automatic} · staged list {assignment.PriceListName} is inactive",
			_ => $"Customer-specific · {assignment.PriceListName} → Region → Global"
		};
	}

	private static SalesRegion NewRegion() => new() { IsActive = true };
	private static SalesRegion CopyRegion(SalesRegion value) => new() { Id=value.Id,Code=value.Code,Name=value.Name,IsActive=value.IsActive,Version=value.Version };
	private void DisposeScopedPricing() { SaveRegionCommand.Dispose(); ClearCustomerAssignmentCommand.Dispose(); }
}
