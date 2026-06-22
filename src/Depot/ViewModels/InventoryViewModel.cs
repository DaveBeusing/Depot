// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class InventoryViewModel
	: BaseViewModel
{
	private readonly StockService _stockService;

	private InventoryOverviewItemViewModel? _selectedItem;

	public InventoryViewModel(
		StockService stockService)
	{
		_stockService = stockService;

		Details =
			new InventoryDetailsViewModel();

		Load();
	}

	public ObservableCollection<InventoryOverviewItemViewModel> Items { get; }
		= new();

	public InventoryDetailsViewModel Details { get; }

	public InventoryOverviewItemViewModel? SelectedItem
	{
		get => _selectedItem;

		set
		{
			_selectedItem = value;

			OnPropertyChanged();

			LoadSelectedDetails();
		}
	}

	public void Load()
	{
		Items.Clear();

		foreach (var item in _stockService.GetInventoryOverview())
		{
			Items.Add(
				new InventoryOverviewItemViewModel(
					item));
		}

		if (SelectedItem is not null)
		{
			var matchingItem =
				Items.FirstOrDefault(
					x => x.ItemId == SelectedItem.ItemId);

			SelectedItem =
				matchingItem;
		}
	}

	private void LoadSelectedDetails()
	{
		if (SelectedItem is null)
		{
			Details.Clear();
			return;
		}

		var details =
			_stockService.GetInventoryDetails(
				SelectedItem.ItemId);

		Details.Load(
			details);
	}
}