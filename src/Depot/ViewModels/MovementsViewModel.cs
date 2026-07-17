// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels;

public sealed class MovementsViewModel : BaseViewModel, IDisposable
{
	private const int PageSize = 100;
	private readonly MovementService _movementService;
	private readonly ReasonCodeService _reasonCodeService;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private InventoryLookupViewModel? _selectedInventory;
	private string? _errorMessage;
	private string _searchText = string.Empty;
	private int _pageNumber = 1;
	private long _totalCount;

	public MovementsViewModel(MovementService movementService, ReasonCodeService reasonCodeService)
	{
		_movementService = movementService;
		_reasonCodeService = reasonCodeService;
		Editor = new MovementEditorViewModel();
		CreateMovementCommand = new AsyncRelayCommand(CreateMovementAsync);
		PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => PageNumber > 1);
		NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => HasNextPage);
	}

	public ObservableCollection<InventoryLookupViewModel> AvailableInventories { get; } = new();
	public ObservableCollection<MovementOverviewItemViewModel> Items { get; } = new();
	public ObservableCollection<ReasonCodeOptionViewModel> ReasonCodes { get; } = new();
	public IReadOnlyList<StockMovementType> MovementTypes { get; } =
	[
		StockMovementType.Purchase,
		StockMovementType.Withdrawal,
		StockMovementType.Correction
	];
	public bool HasItems => Items.Count > 0;
	public bool HasNoItems => !HasItems;
	public bool HasNextPage => (long)PageNumber * PageSize < TotalCount;
	public MovementEditorViewModel Editor { get; }
	public AsyncRelayCommand CreateMovementCommand { get; }
	public AsyncRelayCommand PreviousPageCommand { get; }
	public AsyncRelayCommand NextPageCommand { get; }

	public int PageNumber
	{
		get => _pageNumber;
		private set
		{
			if (_pageNumber == value) return;
			_pageNumber = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public long TotalCount
	{
		get => _totalCount;
		private set
		{
			if (_totalCount == value) return;
			_totalCount = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasNextPage));
			RaisePagingCommands();
		}
	}

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			PageNumber = 1;
			_ = _searchDebouncer.DebounceAsync(LoadMovementsAsync);
		}
	}

	public InventoryLookupViewModel? SelectedInventory
	{
		get => _selectedInventory;
		set
		{
			if (_selectedInventory == value) return;
			_selectedInventory = value;
			OnPropertyChanged();
			Editor.InventoryId = value?.Id ?? 0;
		}
	}

	public string? ErrorMessage
	{
		get => _errorMessage;
		private set
		{
			if (_errorMessage == value) return;
			_errorMessage = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasErrorMessage));
		}
	}

	public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading movements");
		try
		{
			var inventoriesTask = _movementService.SearchAvailableInventoriesAsync(null, 100, cancellationToken);
			var reasonCodesTask = _reasonCodeService.GetActiveAsync(cancellationToken);
			var movementsTask = LoadMovementPageAsync(cancellationToken);
			await Task.WhenAll(inventoriesTask, reasonCodesTask, movementsTask);
			AvailableInventories.Clear();
			foreach (var inventory in await inventoriesTask)
			{
				AvailableInventories.Add(new InventoryLookupViewModel
				{
					Id = inventory.Id,
					ItemId = inventory.ItemId,
					PartNumber = inventory.PartNumber,
					Description = inventory.Description,
					PurposeName = inventory.PurposeName,
					WarehouseName = inventory.WarehouseName,
					LocationName = inventory.LocationName
				});
			}
			ReasonCodes.Clear();
			ReasonCodes.Add(new ReasonCodeOptionViewModel(null, "No reason specified"));
			foreach (var reasonCode in await reasonCodesTask)
			{
				ReasonCodes.Add(new ReasonCodeOptionViewModel(reasonCode.Id, reasonCode.Name));
			}
			CompleteOperation(Items.Count == 0, $"{TotalCount:N0} movements");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Items.Count == 0);
		}
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Movements could not be loaded");
		}
	}

	private async Task LoadMovementsAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Searching movements");
		try
		{
			await LoadMovementPageAsync(cancellationToken);
			CompleteOperation(Items.Count == 0, $"{TotalCount:N0} movements");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			CompleteOperation(Items.Count == 0);
		}
		catch (Exception exception)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Movements could not be loaded");
		}
	}

	private async Task LoadMovementPageAsync(CancellationToken cancellationToken)
	{
		var page = await _movementService.SearchAsync(
			SearchText,
			PageNumber,
			PageSize,
			cancellationToken);
		Items.Clear();
		foreach (var movement in page.Items) Items.Add(new MovementOverviewItemViewModel(movement));
		TotalCount = page.TotalCount;
		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(HasNoItems));
	}

	private async Task CreateMovementAsync(CancellationToken cancellationToken)
	{
		ErrorMessage = null;
		BeginOperation("Saving movement");
		try
		{
			var movement = Editor.MovementType switch
			{
				StockMovementType.Purchase => await _movementService.AddPurchaseAsync(
					Editor.InventoryId, Editor.Quantity, Editor.UnitPrice, Editor.ReasonCodeId, Editor.Reference, Editor.Notes, cancellationToken),
				StockMovementType.Withdrawal => await _movementService.AddWithdrawalAsync(
					Editor.InventoryId, Editor.Quantity, Editor.ReasonCodeId, Editor.Reference, Editor.Notes, cancellationToken),
				StockMovementType.Correction => await _movementService.AddCorrectionAsync(
					Editor.InventoryId, Editor.Quantity, Editor.ReasonCodeId, Editor.Reference, Editor.Notes, cancellationToken),
				_ => throw new InvalidOperationException($"Movement type '{Editor.MovementType}' is not supported.")
			};
			if (PageNumber == 1)
			{
				Items.Insert(0, new MovementOverviewItemViewModel(movement));
				if (Items.Count > PageSize) Items.RemoveAt(Items.Count - 1);
			}
			TotalCount++;
			Editor.Clear();
			SelectedInventory = null;
			OnPropertyChanged(nameof(HasItems));
			OnPropertyChanged(nameof(HasNoItems));
			CompleteOperation(false, "Movement saved");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			ErrorMessage = exception.Message;
			FailOperation(exception, "Movement could not be saved");
		}
	}

	private async Task PreviousPageAsync(CancellationToken cancellationToken)
	{
		if (PageNumber <= 1) return;
		PageNumber--;
		await LoadMovementsAsync(cancellationToken);
	}

	private async Task NextPageAsync(CancellationToken cancellationToken)
	{
		if (!HasNextPage) return;
		PageNumber++;
		await LoadMovementsAsync(cancellationToken);
	}

	private void RaisePagingCommands()
	{
		PreviousPageCommand.RaiseCanExecuteChanged();
		NextPageCommand.RaiseCanExecuteChanged();
	}

	public void Dispose()
	{
		_searchDebouncer.Dispose();
		CreateMovementCommand.Dispose();
		PreviousPageCommand.Dispose();
		NextPageCommand.Dispose();
	}
}
