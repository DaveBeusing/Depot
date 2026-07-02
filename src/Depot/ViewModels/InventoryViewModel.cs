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
	private string _searchText = string.Empty;

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

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText = value;
			OnPropertyChanged();

			Load();
		}
	}

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
		var selectedInventoryId =
			SelectedItem?.InventoryId;

		Items.Clear();

		foreach (var item in _stockService.SearchInventoryOverview(SearchText))
		{
			Items.Add(
				new InventoryOverviewItemViewModel(
					item));
		}

		if (selectedInventoryId is not null)
		{
			SelectedItem =
				Items.FirstOrDefault(
					x => x.InventoryId == selectedInventoryId.Value);
		}

		if (SelectedItem is null)
		{
			Details.Clear();
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
				SelectedItem.InventoryId);

		Details.Load(
			details);
	}
}
