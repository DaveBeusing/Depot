// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;

using Depot.Commands;
using Depot.Models;
using Depot.Services;

namespace Depot.ViewModels.Warehouses;

public sealed class WarehouseStructureViewModel : BaseViewModel, IDisposable
{
	private readonly WarehouseService _warehouseService;
	private readonly StorageLocationService _storageLocationService;
	private readonly AsyncDebouncer _warehouseSearchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private readonly AsyncDebouncer _locationSearchDebouncer = new(TimeSpan.FromMilliseconds(300));
	private Warehouse? _selectedWarehouse;
	private StorageLocation? _selectedStorageLocation;
	private string _warehouseSearchText = string.Empty;
	private string _locationSearchText = string.Empty;
	private long _warehouseEditorId;
	private long _warehouseEditorVersion;
	private string _warehouseName = string.Empty;
	private string? _warehouseDescription;
	private long _locationEditorId;
	private long _locationEditorVersion;
	private string _locationName = string.Empty;
	private string? _locationDescription;

	public WarehouseStructureViewModel(
		WarehouseService warehouseService,
		StorageLocationService storageLocationService)
	{
		_warehouseService = warehouseService;
		_storageLocationService = storageLocationService;
		NewWarehouseCommand = new RelayCommand(NewWarehouse);
		SaveWarehouseCommand = new AsyncRelayCommand(SaveWarehouseAsync);
		ToggleWarehouseCommand = new AsyncRelayCommand(ToggleWarehouseAsync, () => SelectedWarehouse is not null);
		NewStorageLocationCommand = new RelayCommand(NewStorageLocation, () => SelectedWarehouse is not null && SelectedWarehouse.IsActive);
		SaveStorageLocationCommand = new AsyncRelayCommand(SaveStorageLocationAsync, () => SelectedWarehouse is not null && SelectedWarehouse.IsActive);
		ToggleStorageLocationCommand = new AsyncRelayCommand(ToggleStorageLocationAsync, () => SelectedStorageLocation is not null);
	}

	public ObservableCollection<Warehouse> Warehouses { get; } = new();
	public ObservableCollection<StorageLocation> StorageLocations { get; } = new();
	public RelayCommand NewWarehouseCommand { get; }
	public AsyncRelayCommand SaveWarehouseCommand { get; }
	public AsyncRelayCommand ToggleWarehouseCommand { get; }
	public RelayCommand NewStorageLocationCommand { get; }
	public AsyncRelayCommand SaveStorageLocationCommand { get; }
	public AsyncRelayCommand ToggleStorageLocationCommand { get; }

	public string WarehouseSearchText
	{
		get => _warehouseSearchText;
		set
		{
			if (_warehouseSearchText == value) return;
			_warehouseSearchText = value;
			OnPropertyChanged();
			_ = _warehouseSearchDebouncer.DebounceAsync(LoadAsync);
		}
	}

	public string LocationSearchText
	{
		get => _locationSearchText;
		set
		{
			if (_locationSearchText == value) return;
			_locationSearchText = value;
			OnPropertyChanged();
			_ = _locationSearchDebouncer.DebounceAsync(LoadStorageLocationsAsync);
		}
	}

	public Warehouse? SelectedWarehouse
	{
		get => _selectedWarehouse;
		set
		{
			if (_selectedWarehouse == value) return;
			_selectedWarehouse = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(WarehouseActionText));
			LoadWarehouseEditor(value);
			NewStorageLocationCommand.RaiseCanExecuteChanged();
			SaveStorageLocationCommand.RaiseCanExecuteChanged();
			ToggleWarehouseCommand.RaiseCanExecuteChanged();
			_ = LoadStorageLocationsAsync();
		}
	}

	public StorageLocation? SelectedStorageLocation
	{
		get => _selectedStorageLocation;
		set
		{
			if (_selectedStorageLocation == value) return;
			_selectedStorageLocation = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(StorageLocationActionText));
			LoadStorageLocationEditor(value);
			ToggleStorageLocationCommand.RaiseCanExecuteChanged();
		}
	}

	public string WarehouseName { get => _warehouseName; set => SetField(ref _warehouseName, value); }
	public string? WarehouseDescription { get => _warehouseDescription; set => SetField(ref _warehouseDescription, value); }
	public string LocationName { get => _locationName; set => SetField(ref _locationName, value); }
	public string? LocationDescription { get => _locationDescription; set => SetField(ref _locationDescription, value); }
	public string WarehouseActionText => SelectedWarehouse?.IsActive == true ? "Deactivate" : "Activate";
	public string StorageLocationActionText => SelectedStorageLocation?.IsActive == true ? "Deactivate" : "Activate";

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		BeginOperation("Loading warehouse structure...");
		try
		{
			var selectedId = SelectedWarehouse?.Id;
			var warehouses = await _warehouseService.SearchAsync(WarehouseSearchText, cancellationToken);
			Warehouses.Clear();
			foreach (var warehouse in warehouses) Warehouses.Add(warehouse);
			SelectedWarehouse = Warehouses.FirstOrDefault(item => item.Id == selectedId) ?? Warehouses.FirstOrDefault();
			CompleteOperation(Warehouses.Count == 0, $"{Warehouses.Count:N0} warehouses");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Warehouse structure could not be loaded."); }
	}

	private async Task LoadStorageLocationsAsync(CancellationToken cancellationToken = default)
	{
		var warehouseId = SelectedWarehouse?.Id;
		var selectedId = SelectedStorageLocation?.Id;
		try
		{
			var locations = warehouseId is null
				? Array.Empty<StorageLocation>()
				: await _storageLocationService.SearchAsync(warehouseId, LocationSearchText, cancellationToken);
			StorageLocations.Clear();
			foreach (var location in locations) StorageLocations.Add(location);
			SelectedStorageLocation = StorageLocations.FirstOrDefault(item => item.Id == selectedId) ?? StorageLocations.FirstOrDefault();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		catch (Exception exception) { FailOperation(exception, "Storage locations could not be loaded."); }
	}

	private void NewWarehouse()
	{
		SelectedWarehouse = null;
		_warehouseEditorId = 0;
		_warehouseEditorVersion = 0;
		WarehouseName = string.Empty;
		WarehouseDescription = null;
	}

	private async Task SaveWarehouseAsync(CancellationToken cancellationToken)
	{
		BeginOperation("Saving warehouse...");
		try
		{
			var warehouse = await _warehouseService.SaveAsync(
				_warehouseEditorId,
				_warehouseEditorVersion,
				WarehouseName,
				WarehouseDescription,
				cancellationToken);
			ReplaceWarehouse(warehouse);
			SelectedWarehouse = warehouse;
			CompleteOperation(statusText: "Warehouse saved.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Warehouse could not be saved."); }
	}

	private async Task ToggleWarehouseAsync(CancellationToken cancellationToken)
	{
		if (SelectedWarehouse is null) return;
		BeginOperation(SelectedWarehouse.IsActive ? "Deactivating warehouse..." : "Activating warehouse...");
		try
		{
			var warehouse = await _warehouseService.SetActiveAsync(
				SelectedWarehouse.Id,
				SelectedWarehouse.Version,
				!SelectedWarehouse.IsActive,
				cancellationToken);
			ReplaceWarehouse(warehouse);
			SelectedWarehouse = warehouse;
			CompleteOperation(statusText: warehouse.IsActive ? "Warehouse activated." : "Warehouse deactivated.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Warehouse status could not be changed."); }
	}

	private void NewStorageLocation()
	{
		SelectedStorageLocation = null;
		_locationEditorId = 0;
		_locationEditorVersion = 0;
		LocationName = string.Empty;
		LocationDescription = null;
	}

	private async Task SaveStorageLocationAsync(CancellationToken cancellationToken)
	{
		if (SelectedWarehouse is null) return;
		BeginOperation("Saving storage location...");
		try
		{
			var location = await _storageLocationService.SaveAsync(
				_locationEditorId,
				_locationEditorVersion,
				SelectedWarehouse.Id,
				LocationName,
				LocationDescription,
				cancellationToken);
			ReplaceStorageLocation(location);
			SelectedStorageLocation = location;
			CompleteOperation(statusText: "Storage location saved.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Storage location could not be saved."); }
	}

	private async Task ToggleStorageLocationAsync(CancellationToken cancellationToken)
	{
		if (SelectedStorageLocation is null) return;
		BeginOperation(SelectedStorageLocation.IsActive ? "Deactivating storage location..." : "Activating storage location...");
		try
		{
			var location = await _storageLocationService.SetActiveAsync(
				SelectedStorageLocation.Id,
				SelectedStorageLocation.Version,
				!SelectedStorageLocation.IsActive,
				cancellationToken);
			ReplaceStorageLocation(location);
			SelectedStorageLocation = location;
			CompleteOperation(statusText: location.IsActive ? "Storage location activated." : "Storage location deactivated.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException) { FailOperation(exception, "Storage location status could not be changed."); }
	}

	private void LoadWarehouseEditor(Warehouse? warehouse)
	{
		_warehouseEditorId = warehouse?.Id ?? 0;
		_warehouseEditorVersion = warehouse?.Version ?? 0;
		WarehouseName = warehouse?.Name ?? string.Empty;
		WarehouseDescription = warehouse?.Description;
	}

	private void LoadStorageLocationEditor(StorageLocation? location)
	{
		_locationEditorId = location?.Id ?? 0;
		_locationEditorVersion = location?.Version ?? 0;
		LocationName = location?.Name ?? string.Empty;
		LocationDescription = location?.Description;
	}

	private void ReplaceWarehouse(Warehouse warehouse)
	{
		var existing = Warehouses.FirstOrDefault(item => item.Id == warehouse.Id);
		if (existing is null) Warehouses.Add(warehouse);
		else Warehouses[Warehouses.IndexOf(existing)] = warehouse;
	}

	private void ReplaceStorageLocation(StorageLocation location)
	{
		var existing = StorageLocations.FirstOrDefault(item => item.Id == location.Id);
		if (existing is null) StorageLocations.Add(location);
		else StorageLocations[StorageLocations.IndexOf(existing)] = location;
	}

	private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		OnPropertyChanged(propertyName);
	}

	public void Dispose()
	{
		_warehouseSearchDebouncer.Dispose();
		_locationSearchDebouncer.Dispose();
		SaveWarehouseCommand.Dispose();
		ToggleWarehouseCommand.Dispose();
		SaveStorageLocationCommand.Dispose();
		ToggleStorageLocationCommand.Dispose();
	}
}
