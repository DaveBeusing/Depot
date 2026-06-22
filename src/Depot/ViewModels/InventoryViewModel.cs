// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class InventoryViewModel
	: BaseViewModel
{
	private readonly StockService _stockService;

	public InventoryViewModel(
		StockService stockService)
	{
		_stockService = stockService;

		Load();
	}

	public ObservableCollection<InventoryOverviewItemViewModel> Items { get; }
		= new();

	public void Load()
	{
		Items.Clear();

		foreach (var item in _stockService.GetInventoryOverview())
		{
			Items.Add(
				new InventoryOverviewItemViewModel(
					item));
		}
	}
}