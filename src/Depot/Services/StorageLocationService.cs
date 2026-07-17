// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class StorageLocationService
{
	private readonly StorageLocationRepository _locations;
	private readonly WarehouseRepository _warehouses;
	private readonly AuditService _audit;
	private readonly AsyncCache<IReadOnlyList<StorageLocation>> _activeCache = new(TimeSpan.FromMinutes(5));

	public StorageLocationService(
		StorageLocationRepository locations,
		WarehouseRepository warehouses,
		AuditService audit)
	{
		_locations = locations;
		_warehouses = warehouses;
		_audit = audit;
	}

	public Task<IReadOnlyList<StorageLocation>> SearchAsync(
		long? warehouseId,
		string? searchText,
		CancellationToken cancellationToken = default) =>
		_locations.SearchAsync(warehouseId, searchText, cancellationToken);

	public Task<IReadOnlyList<StorageLocation>> GetActiveAsync(CancellationToken cancellationToken = default) =>
		_activeCache.GetAsync(_locations.ListActiveAsync, cancellationToken);

	public async Task<StorageLocation> GetOrCreateAsync(
		long warehouseId,
		string name,
		CancellationToken cancellationToken = default)
	{
		var normalized = name.Trim();
		var existing = await _locations.GetByNameAsync(warehouseId, normalized, cancellationToken);
		return existing ?? await SaveAsync(0, 0, warehouseId, normalized, null, cancellationToken);
	}

	public async Task<StorageLocation> SaveAsync(
		long id,
		long version,
		long warehouseId,
		string name,
		string? description,
		CancellationToken cancellationToken = default)
	{
		name = name.Trim();
		description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		if (warehouseId <= 0) throw new ArgumentException("Warehouse is required.", nameof(warehouseId));
		if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Storage location name is required.", nameof(name));
		var warehouse = await _warehouses.GetByIdAsync(warehouseId, cancellationToken)
			?? throw new InvalidOperationException($"Warehouse with id '{warehouseId}' was not found.");
		if (!warehouse.IsActive) throw new InvalidOperationException("Storage locations can be added only to an active warehouse.");
		var duplicate = await _locations.GetByNameAsync(warehouseId, name, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"Storage location '{name}' already exists in this warehouse.");

		if (id == 0)
		{
			var location = new StorageLocation
			{
				WarehouseId = warehouseId,
				Name = name,
				Description = description,
				IsActive = true
			};
			location.Id = await _locations.CreateAsync(location, cancellationToken);
			await _audit.RecordCreatedAsync(location.Id, location, cancellationToken);
			_activeCache.Invalidate();
			return location;
		}

		var existing = await _locations.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Storage location with id '{id}' was not found.");
		if (existing.Version != version) throw new ConcurrencyConflictException("storage location");
		var before = Copy(existing);
		existing.WarehouseId = warehouseId;
		existing.Name = name;
		existing.Description = description;
		if (!await _locations.UpdateAsync(existing, cancellationToken)) throw new ConcurrencyConflictException("storage location");
		existing.Version++;
		await _audit.RecordUpdatedAsync(existing.Id, before, existing, cancellationToken);
		_activeCache.Invalidate();
		return existing;
	}

	public async Task<StorageLocation> SetActiveAsync(
		long id,
		long version,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		var location = await _locations.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Storage location with id '{id}' was not found.");
		if (isActive)
		{
			var warehouse = await _warehouses.GetByIdAsync(location.WarehouseId, cancellationToken)
				?? throw new InvalidOperationException("The warehouse no longer exists.");
			if (!warehouse.IsActive) throw new InvalidOperationException("Activate the warehouse first.");
		}
		if (location.Version != version || !await _locations.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException("storage location");
		var before = Copy(location);
		location.IsActive = isActive;
		location.Version++;
		await _audit.RecordUpdatedAsync(location.Id, before, location, cancellationToken);
		_activeCache.Invalidate();
		return location;
	}

	private static StorageLocation Copy(StorageLocation source) => new()
	{
		Id = source.Id,
		WarehouseId = source.WarehouseId,
		Name = source.Name,
		Description = source.Description,
		IsActive = source.IsActive,
		Version = source.Version
	};
}
