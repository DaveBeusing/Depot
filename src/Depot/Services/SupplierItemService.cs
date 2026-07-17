// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class SupplierItemService
{
	private readonly SupplierItemRepository _supplierItems;
	private readonly SupplierRepository _suppliers;
	private readonly ItemRepository _items;
	private readonly AuditService _audit;

	public SupplierItemService(SupplierItemRepository supplierItems, SupplierRepository suppliers, ItemRepository items, AuditService audit)
	{
		_supplierItems = supplierItems;
		_suppliers = suppliers;
		_items = items;
		_audit = audit;
	}

	public Task<IReadOnlyList<SupplierItem>> SearchAsync(long supplierId, string? searchText, CancellationToken cancellationToken = default) =>
		_supplierItems.SearchBySupplierAsync(supplierId, searchText, cancellationToken);

	public async Task<SupplierItem> SaveAsync(SupplierItem draft, CancellationToken cancellationToken = default)
	{
		draft.SupplierPartNumber = draft.SupplierPartNumber.Trim();
		if (draft.SupplierId <= 0) throw new ArgumentException("Supplier is required.", nameof(draft.SupplierId));
		if (draft.ItemId <= 0) throw new ArgumentException("Item is required.", nameof(draft.ItemId));
		if (string.IsNullOrWhiteSpace(draft.SupplierPartNumber)) throw new ArgumentException("Supplier part number is required.", nameof(draft.SupplierPartNumber));
		if (draft.SupplierPartNumber.Length > 200) throw new ArgumentException("Supplier part number must not exceed 200 characters.", nameof(draft.SupplierPartNumber));
		if (draft.PurchasePrice < 0) throw new ArgumentOutOfRangeException(nameof(draft.PurchasePrice), "Purchase price cannot be negative.");
		if (draft.PurchasePrice > 9_999_999_999_999_999.99m) throw new ArgumentOutOfRangeException(nameof(draft.PurchasePrice), "Purchase price is too large.");
		if (draft.LeadTimeDays is < 0 or > 36_500) throw new ArgumentOutOfRangeException(nameof(draft.LeadTimeDays), "Lead time must be between 0 and 36,500 days.");
		if (draft.MinimumOrderQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(draft.MinimumOrderQuantity), "Minimum order quantity must be greater than zero.");
		draft.PurchasePrice = decimal.Round(draft.PurchasePrice, 2, MidpointRounding.AwayFromZero);
		draft.MinimumOrderQuantity = decimal.Round(draft.MinimumOrderQuantity, 3, MidpointRounding.AwayFromZero);
		var supplier = await _suppliers.GetByIdAsync(draft.SupplierId, cancellationToken)
			?? throw new InvalidOperationException("Supplier was not found.");
		if (!supplier.IsActive) throw new InvalidOperationException("Supplier is inactive.");
		var item = await _items.GetByIdAsync(draft.ItemId, cancellationToken)
			?? throw new InvalidOperationException("Item was not found.");
		if (!item.IsActive) throw new InvalidOperationException("Item is inactive.");
		var duplicate = await _supplierItems.GetByContextAsync(draft.SupplierId, draft.ItemId, cancellationToken);
		if (duplicate is not null && duplicate.Id != draft.Id) throw new InvalidOperationException("This item is already assigned to the supplier.");

		SupplierItem? before = null;
		if (draft.Id > 0)
		{
			before = await _supplierItems.GetByIdAsync(draft.Id, cancellationToken)
				?? throw new InvalidOperationException("Supplier item was not found.");
			if (before.Version != draft.Version) throw new ConcurrencyConflictException("supplier item");
			draft.IsActive = before.IsActive;
			if (!draft.IsActive && draft.IsPreferredSupplier) throw new InvalidOperationException("Activate the supplier item before making it preferred.");
		}
		else draft.IsActive = true;

		var id = await _supplierItems.SaveAsync(draft, cancellationToken);
		if (id == 0) throw new ConcurrencyConflictException("supplier item");
		draft.Id = id;
		if (before is null) await _audit.RecordCreatedAsync(id, draft, cancellationToken);
		else { draft.Version++; await _audit.RecordUpdatedAsync(id, before, draft, cancellationToken); }
		return await _supplierItems.GetByIdAsync(id, cancellationToken) ?? draft;
	}

	public async Task<SupplierItem> SetActiveAsync(long id, long version, bool isActive, CancellationToken cancellationToken = default)
	{
		var value = await _supplierItems.GetByIdAsync(id, cancellationToken)
			?? throw new InvalidOperationException("Supplier item was not found.");
		if (isActive)
		{
			var supplier = await _suppliers.GetByIdAsync(value.SupplierId, cancellationToken);
			var item = await _items.GetByIdAsync(value.ItemId, cancellationToken);
			if (supplier?.IsActive != true || item?.IsActive != true) throw new InvalidOperationException("Supplier and item must be active before this assignment can be activated.");
		}
		if (value.Version != version || !await _supplierItems.SetActiveAsync(id, version, isActive, cancellationToken))
			throw new ConcurrencyConflictException("supplier item");
		var before = Copy(value);
		value.IsActive = isActive;
		if (!isActive) value.IsPreferredSupplier = false;
		value.Version++;
		await _audit.RecordUpdatedAsync(id, before, value, cancellationToken);
		return value;
	}

	private static SupplierItem Copy(SupplierItem source) => new()
	{
		Id = source.Id, SupplierId = source.SupplierId, ItemId = source.ItemId, ItemPartNumber = source.ItemPartNumber,
		ItemDescription = source.ItemDescription, SupplierPartNumber = source.SupplierPartNumber, PurchasePrice = source.PurchasePrice,
		LeadTimeDays = source.LeadTimeDays, MinimumOrderQuantity = source.MinimumOrderQuantity,
		IsPreferredSupplier = source.IsPreferredSupplier, IsActive = source.IsActive, Version = source.Version
	};
}
