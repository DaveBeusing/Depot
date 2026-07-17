// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class WarehouseService
{
	private readonly WarehouseRepository _warehouses;
	private readonly StorageLocationRepository _storageLocations;
	private readonly AuditService _audit;

	public WarehouseService(
		WarehouseRepository warehouses,
		StorageLocationRepository storageLocations,
		AuditService audit)
	{
		_warehouses = warehouses;
		_storageLocations = storageLocations;
		_audit = audit;
	}

	public Task<IReadOnlyList<Warehouse>> SearchAsync(string? searchText, CancellationToken cancellationToken = default) =>
		_warehouses.SearchAsync(searchText, cancellationToken);

	public async Task<Warehouse> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
	{
		var normalized = name.Trim();
		var existing = await _warehouses.GetByNameAsync(normalized, cancellationToken);
		return existing ?? await SaveAsync(0, 0, normalized, null, cancellationToken);
	}

	public async Task<Warehouse> SaveAsync(
		long id,
		long version,
		string name,
		string? description,
		CancellationToken cancellationToken = default)
	{
		(name, description) = Normalize(name, description);
		if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Warehouse name is required.", nameof(name));
		var duplicate = await _warehouses.GetByNameAsync(name, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"Warehouse '{name}' already exists.");

		if (id == 0)
		{
			var warehouse = new Warehouse { Name = name, Description = description, IsActive = true };
			warehouse.Id = await _warehouses.CreateAsync(warehouse, cancellationToken);
			await _audit.RecordCreatedAsync(warehouse.Id, warehouse, cancellationToken);
			return warehouse;
		}

		var existing = await _warehouses.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Warehouse with id '{id}' was not found.");
		if (existing.Version != version) throw new ConcurrencyConflictException("warehouse");
		var before = Copy(existing);
		existing.Name = name;
		existing.Description = description;
		if (!await _warehouses.UpdateAsync(existing, cancellationToken)) throw new ConcurrencyConflictException("warehouse");
		existing.Version++;
		await _audit.RecordUpdatedAsync(existing.Id, before, existing, cancellationToken);
		return existing;
	}

	public async Task<Warehouse> SetActiveAsync(
		long id,
		long version,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		var warehouse = await _warehouses.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"Warehouse with id '{id}' was not found.");
		if (!isActive && await _storageLocations.HasActiveForWarehouseAsync(id, cancellationToken))
			throw new InvalidOperationException("Deactivate all storage locations in this warehouse first.");
		if (warehouse.Version != version || !await _warehouses.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException("warehouse");
		var before = Copy(warehouse);
		warehouse.IsActive = isActive;
		warehouse.Version++;
		await _audit.RecordUpdatedAsync(warehouse.Id, before, warehouse, cancellationToken);
		return warehouse;
	}

	private static Warehouse Copy(Warehouse source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		Description = source.Description,
		IsActive = source.IsActive,
		Version = source.Version
	};

	private static (string Name, string? Description) Normalize(string name, string? description) =>
		(name.Trim(), string.IsNullOrWhiteSpace(description) ? null : description.Trim());
}
