// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class ReportsViewModel
	: BaseViewModel
{
	private readonly ReportService _reportService;

	private string _searchText = string.Empty;
	private int _totalInventoryRows;
	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;

	public ReportsViewModel(
		ReportService reportService)
	{
		_reportService =
			reportService;

		Load();
	}

	public ObservableCollection<InventoryValueReportItemViewModel> Items { get; }
		= new();

	public string SearchText
	{
		get => _searchText;

		set
		{
			_searchText =
				value;

			OnPropertyChanged();

			Load();
		}
	}

	public int TotalInventoryRows
	{
		get => _totalInventoryRows;

		private set
		{
			_totalInventoryRows =
				value;

			OnPropertyChanged();
		}
	}

	public int TotalItems
	{
		get => _totalItems;

		private set
		{
			_totalItems =
				value;

			OnPropertyChanged();
		}
	}

	public int TotalStockQuantity
	{
		get => _totalStockQuantity;

		private set
		{
			_totalStockQuantity =
				value;

			OnPropertyChanged();
		}
	}

	public decimal TotalInventoryValue
	{
		get => _totalInventoryValue;

		private set
		{
			_totalInventoryValue =
				value;

			OnPropertyChanged();
		}
	}

	public void Load()
	{
		var report =
			_reportService.GetInventoryValueReport(
				SearchText);

		TotalInventoryRows =
			report.TotalInventoryRows;

		TotalItems =
			report.TotalItems;

		TotalStockQuantity =
			report.TotalStockQuantity;

		TotalInventoryValue =
			report.TotalInventoryValue;

		Items.Clear();

		foreach (var item in report.Items)
		{
			Items.Add(
				new InventoryValueReportItemViewModel(
					item));
		}
	}
}
