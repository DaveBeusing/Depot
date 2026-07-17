// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public interface IItemReferenceDataService
{
	string SingularName { get; }
	string PluralName { get; }
	Task<IReadOnlyList<ItemReferenceData>> SearchAsync(string? searchText, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<ItemReferenceData>> GetActiveAsync(CancellationToken cancellationToken = default);
	Task<ItemReferenceData> SaveAsync(long id, long version, string name, string? description, CancellationToken cancellationToken = default);
	Task<ItemReferenceData> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken = default);
	Task<ItemReferenceData?> GetOrCreateAsync(string? name, CancellationToken cancellationToken = default);
	Task ValidateSelectionAsync(long? id, CancellationToken cancellationToken = default);
}

public abstract class ItemReferenceDataService<T> : IItemReferenceDataService
	where T : ItemReferenceData, new()
{
	private readonly ItemReferenceDataRepository<T> _repository;
	private readonly AuditService _auditService;
	private readonly AsyncCache<IReadOnlyList<T>> _activeCache = new(TimeSpan.FromMinutes(5));

	protected ItemReferenceDataService(
		ItemReferenceDataRepository<T> repository,
		AuditService auditService,
		string singularName,
		string pluralName)
	{
		_repository = repository;
		_auditService = auditService;
		SingularName = singularName;
		PluralName = pluralName;
	}

	public string SingularName { get; }
	public string PluralName { get; }

	public async Task<IReadOnlyList<ItemReferenceData>> SearchAsync(string? searchText, CancellationToken cancellationToken = default) =>
		[.. await _repository.SearchAsync(searchText, cancellationToken)];

	public async Task<IReadOnlyList<ItemReferenceData>> GetActiveAsync(CancellationToken cancellationToken = default) =>
		[.. await _activeCache.GetAsync(_repository.ListActiveAsync, cancellationToken)];

	public async Task<ItemReferenceData> SaveAsync(
		long id,
		long version,
		string name,
		string? description,
		CancellationToken cancellationToken = default)
	{
		name = name.Trim();
		description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException($"{SingularName} name is required.", nameof(name));
		if (name.Length > 200)
			throw new ArgumentException($"{SingularName} name must not exceed 200 characters.", nameof(name));
		if (description?.Length > 500)
			throw new ArgumentException("Description must not exceed 500 characters.", nameof(description));

		var duplicate = await _repository.GetByNameAsync(name, cancellationToken);
		if (duplicate is not null && duplicate.Id != id)
			throw new InvalidOperationException($"{SingularName} '{name}' already exists.");

		if (id == 0)
		{
			var created = new T { Name = name, Description = description, IsActive = true };
			created.Id = await _repository.CreateAsync(created, cancellationToken);
			await _auditService.RecordCreatedAsync(created.Id, created, cancellationToken);
			_activeCache.Invalidate();
			return created;
		}

		var existing = await _repository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"{SingularName} with id '{id}' was not found.");
		if (existing.Version != version) throw new ConcurrencyConflictException(SingularName.ToLowerInvariant());
		var before = Copy(existing);
		existing.Name = name;
		existing.Description = description;
		if (!await _repository.UpdateAsync(existing, cancellationToken))
			throw new ConcurrencyConflictException(SingularName.ToLowerInvariant());
		existing.Version++;
		await _auditService.RecordUpdatedAsync(existing.Id, before, existing, cancellationToken);
		_activeCache.Invalidate();
		return existing;
	}

	public async Task<ItemReferenceData> SetActiveAsync(
		long id,
		long version,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		var value = await _repository.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException($"{SingularName} with id '{id}' was not found.");
		if (!isActive && await _repository.IsReferencedAsync(id, cancellationToken))
			throw new InvalidOperationException($"{SingularName} '{value.Name}' is referenced by one or more items and cannot be deactivated.");
		if (value.Version != version || !await _repository.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException(SingularName.ToLowerInvariant());
		var before = Copy(value);
		value.IsActive = isActive;
		value.Version++;
		await _auditService.RecordUpdatedAsync(value.Id, before, value, cancellationToken);
		_activeCache.Invalidate();
		return value;
	}

	public async Task<ItemReferenceData?> GetOrCreateAsync(string? name, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		name = name.Trim();
		var existing = await _repository.GetByNameAsync(name, cancellationToken);
		return existing ?? await SaveAsync(0, 0, name, null, cancellationToken);
	}

	public async Task ValidateSelectionAsync(long? id, CancellationToken cancellationToken = default)
	{
		if (id is null) return;
		var value = await _repository.GetByIdAsync(id.Value, cancellationToken)
			?? throw new InvalidOperationException($"Selected {SingularName.ToLowerInvariant()} was not found.");
		if (!value.IsActive)
			throw new InvalidOperationException($"{SingularName} '{value.Name}' is inactive and cannot be assigned.");
	}

	private static T Copy(T source) => new()
	{
		Id = source.Id,
		Name = source.Name,
		Description = source.Description,
		IsActive = source.IsActive,
		Version = source.Version
	};
}

public sealed class ManufacturerService(ManufacturerRepository repository, AuditService auditService)
	: ItemReferenceDataService<Manufacturer>(repository, auditService, "Manufacturer", "Manufacturers");

public sealed class CategoryService(CategoryRepository repository, AuditService auditService)
	: ItemReferenceDataService<Category>(repository, auditService, "Category", "Categories");

public sealed class UnitOfMeasureService(UnitOfMeasureRepository repository, AuditService auditService)
	: ItemReferenceDataService<UnitOfMeasure>(repository, auditService, "Unit of Measure", "Units of Measure");

public sealed class PackagingService(PackagingRepository repository, AuditService auditService)
	: ItemReferenceDataService<Packaging>(repository, auditService, "Packaging", "Packaging");

public sealed class SupplierService(SupplierRepository repository, AuditService auditService)
	: ItemReferenceDataService<Supplier>(repository, auditService, "Supplier", "Suppliers");
