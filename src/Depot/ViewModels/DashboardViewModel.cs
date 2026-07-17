// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Services;

namespace Depot.ViewModels;

public sealed class DashboardViewModel
	: BaseViewModel
{
	private readonly StockService _stockService;

	private int _totalItems;
	private int _totalStockQuantity;
	private decimal _totalInventoryValue;
	private int _totalMovements;

	public DashboardViewModel(
		StockService stockService)
	{
		_stockService = stockService;
	}

	public int TotalItems
	{
		get => _totalItems;

		private set
		{
			_totalItems = value;
			OnPropertyChanged();
		}
	}

	public int TotalStockQuantity
	{
		get => _totalStockQuantity;

		private set
		{
			_totalStockQuantity = value;
			OnPropertyChanged();
		}
	}

	public decimal TotalInventoryValue
	{
		get => _totalInventoryValue;

		private set
		{
			_totalInventoryValue = value;
			OnPropertyChanged();
		}
	}

	public int TotalMovements
	{
		get => _totalMovements;

		private set
		{
			_totalMovements = value;
			OnPropertyChanged();
		}
	}

	public ObservableCollection<DashboardRecentMovementViewModel> RecentMovements { get; }
		= new();

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading dashboard");
		try
		{
			var data = await _stockService.GetDashboardDataAsync(cancellationToken);
			var summary = data.Summary;

		TotalItems =
			summary.TotalItems;

		TotalStockQuantity =
			summary.TotalStockQuantity;

		TotalInventoryValue =
			summary.TotalInventoryValue;

		TotalMovements =
			summary.TotalMovements;

			RecentMovements.Clear();

			foreach (var movement in data.RecentMovements)
			{
				RecentMovements.Add(new DashboardRecentMovementViewModel(movement));
			}
			CompleteOperation(RecentMovements.Count == 0, "Dashboard loaded");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(RecentMovements.Count == 0);
		}
		catch (Exception exception)
		{
			FailOperation(exception, "Dashboard could not be loaded");
		}
	}
}
