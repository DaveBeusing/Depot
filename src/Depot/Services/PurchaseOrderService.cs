// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class PurchaseOrderService
{
	private readonly PurchaseOrderRepository _orders;
	private readonly SupplierRepository _suppliers;
	private readonly ItemRepository _items;
	private readonly AuditService _audit;

	public PurchaseOrderService(PurchaseOrderRepository orders, SupplierRepository suppliers, ItemRepository items, AuditService audit)
	{
		_orders = orders;
		_suppliers = suppliers;
		_items = items;
		_audit = audit;
	}

	public Task<PageResult<PurchaseOrder>> SearchAsync(string? searchText, PurchaseOrderStatus? status, int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
		_orders.SearchAsync(searchText, status, pageNumber, pageSize, cancellationToken);

	public Task<PurchaseOrder?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => _orders.GetByIdAsync(id, cancellationToken);

	public async Task<PurchaseOrder> SaveDraftAsync(PurchaseOrder draft, CancellationToken cancellationToken = default)
	{
		draft.Notes = Normalize(draft.Notes);
		if (draft.Id != 0 && draft.Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft purchase orders can be edited.");
		var supplier = await _suppliers.GetByIdAsync(draft.SupplierId, cancellationToken) ?? throw new InvalidOperationException("The selected supplier was not found.");
		if (!supplier.IsActive) throw new InvalidOperationException("The selected supplier is inactive.");
		if (draft.ExpectedDeliveryDate is not null && draft.ExpectedDeliveryDate.Value.Date < draft.OrderDate.Date) throw new ArgumentException("Expected delivery date cannot be earlier than the order date.");
		if (draft.Notes?.Length > 4000) throw new ArgumentException("Notes must not exceed 4000 characters.");
		if (draft.Lines.Count == 0) throw new InvalidOperationException("A purchase order requires at least one line.");
		if (draft.Lines.Select(line => line.ItemId).Distinct().Count() != draft.Lines.Count) throw new InvalidOperationException("An item can only occur once per purchase order.");
		foreach (var line in draft.Lines)
		{
			if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(line.Quantity), "Quantity must be greater than zero.");
			if (line.UnitPrice < 0) throw new ArgumentOutOfRangeException(nameof(line.UnitPrice), "Unit price cannot be negative.");
			var item = await _items.GetByIdAsync(line.ItemId, cancellationToken) ?? throw new InvalidOperationException("An ordered item was not found.");
			if (!item.IsActive) throw new InvalidOperationException($"Item '{item.PartNumber}' is inactive.");
		}
		var isNew = draft.Id == 0;
		var before = isNew ? null : await _orders.GetByIdAsync(draft.Id, cancellationToken);
		return await _orders.SaveDraftAsync(
			draft,
			after => isNew
				? _audit.CreateCreatedEntry(after.Id, after)
				: _audit.CreateUpdatedEntry(after.Id, before ?? throw new InvalidOperationException("Purchase order was not found before saving."), after),
			cancellationToken);
	}

	public async Task<PurchaseOrder> MarkOrderedAsync(long id, long version, CancellationToken cancellationToken = default) =>
		await ChangeStatusAsync(id, version, PurchaseOrderStatus.Draft, PurchaseOrderStatus.Ordered, cancellationToken);

	public async Task<PurchaseOrder> CancelAsync(long id, long version, CancellationToken cancellationToken = default)
	{
		var order = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered)) throw new InvalidOperationException("This purchase order can no longer be cancelled.");
		return await ChangeStatusAsync(id, version, order.Status, PurchaseOrderStatus.Cancelled, cancellationToken);
	}

	private async Task<PurchaseOrder> ChangeStatusAsync(long id, long version, PurchaseOrderStatus expected, PurchaseOrderStatus status, CancellationToken cancellationToken)
	{
		var before = await _orders.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Purchase order was not found.");
		return await _orders.SetStatusAsync(
			id,
			version,
			expected,
			status,
			after => _audit.CreateUpdatedEntry(id, before, after),
			cancellationToken);
	}

	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
