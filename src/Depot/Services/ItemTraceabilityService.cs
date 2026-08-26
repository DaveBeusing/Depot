// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class ItemTraceabilityService
{
	private readonly ItemTraceabilityRepository _repository;
	private readonly AuditService _audit;

	public ItemTraceabilityService(ItemTraceabilityRepository repository, AuditService audit)
	{
		_repository = repository;
		_audit = audit;
	}

	public bool CanManage => _audit.HasPermission(ApplicationPermission.ItemsManage);

	public Task<PageResult<ItemTraceabilityBalance>> SearchBalancesAsync(string? searchText, long? itemId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_audit.RequirePermission(ApplicationPermission.ItemsView);
		return _repository.SearchBalancesAsync(searchText, itemId, pageNumber, pageSize, cancellationToken);
	}

	public Task<PageResult<ItemTraceabilityHistoryEntry>> SearchHistoryAsync(string? searchText, long? trackingUnitId, int pageNumber, int pageSize, CancellationToken cancellationToken)
	{
		_audit.RequirePermission(ApplicationPermission.ItemsView);
		return _repository.SearchHistoryAsync(searchText, trackingUnitId, pageNumber, pageSize, cancellationToken);
	}

	public async Task SetBlockedAsync(ItemTraceabilityBalance unit, bool isBlocked, string? reason, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(unit);
		_audit.RequirePermission(ApplicationPermission.ItemsManage);
		reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
		if (isBlocked && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A block reason is required.", nameof(reason));
		if (reason?.Length > 500) throw new ArgumentException("Block reason must not exceed 500 characters.", nameof(reason));
		var before = new { unit.TrackingUnitId, unit.PartNumber, unit.Code, unit.IsBlocked, unit.BlockReason, unit.Version };
		if (!await _repository.SetBlockedAsync(unit.TrackingUnitId, unit.Version, isBlocked, reason, cancellationToken)) throw new ConcurrencyConflictException("tracking unit");
		var after = new { unit.TrackingUnitId, unit.PartNumber, unit.Code, IsBlocked = isBlocked, BlockReason = isBlocked ? reason : null, Version = unit.Version + 1 };
		await _audit.RecordUpdatedAsync(unit.TrackingUnitId, before, after, cancellationToken);
	}

	public async Task<IReadOnlyList<MovementTrackingAllocation>> AttachMovementAsync(
		DatabaseTransactionContext transaction,
		StockMovement movement,
		IReadOnlyList<TrackingAllocationInput>? allocations,
		CancellationToken cancellationToken)
	{
		var policy = await _repository.GetInventoryPolicyAsync(transaction, movement.InventoryId, cancellationToken)
			?? throw new InvalidOperationException($"Inventory '{movement.InventoryId}' was not found for traceability validation.");
		EnsurePhysicalStockItem(policy, "stock movement");
		var normalized = NormalizeAllocations(allocations);
		if (policy.TrackingMode == ItemTrackingMode.None)
		{
			if (normalized.Count > 0) throw new InvalidOperationException($"Item '{policy.PartNumber}' is not configured for serial or lot tracking.");
			return [];
		}
		if (normalized.Count == 0 && TryGetCaptureScope(movement, out var captureScope))
		{
			normalized = NormalizeAllocations(TrackingCaptureSession.ResolveForInventory(captureScope, movement.InventoryId, movement.Quantity));
		}
		if (movement.Quantity == 0) throw new InvalidOperationException("Tracked stock movements must have a non-zero quantity.");
		ValidateAllocationShape(policy, movement, normalized);

		var inbound = movement.Quantity > 0;
		var resolved = new List<MovementTrackingAllocation>(normalized.Count);
		foreach (var input in normalized.OrderBy(value => value.Code, StringComparer.Ordinal))
		{
			var unit = await _repository.GetUnitByCodeAsync(transaction, policy.ItemId, policy.TrackingMode, input.Code, cancellationToken);
			if (inbound) unit = await ResolveInboundUnitAsync(transaction, policy, unit, input, cancellationToken);
			else unit = await ResolveOutboundUnitAsync(transaction, policy, unit, input, movement.InventoryId, cancellationToken);
			var signedQuantity = inbound ? input.Quantity : -input.Quantity;
			await _repository.AddMovementAllocationAsync(transaction, movement.Id, unit.TrackingUnitId, signedQuantity, cancellationToken);
			resolved.Add(new MovementTrackingAllocation
			{
				TrackingUnitId = unit.TrackingUnitId,
				Code = unit.Code,
				Quantity = signedQuantity,
				ExpiryDate = unit.ExpiryDate ?? input.ExpiryDate,
				IsBlocked = unit.IsBlocked,
				BlockReason = unit.BlockReason,
				Version = unit.Version
			});
		}
		return resolved;
	}

	public async Task AttachReversalAsync(DatabaseTransactionContext transaction, StockMovement original, StockMovement reversal, CancellationToken cancellationToken)
	{
		var allocations = await _repository.ListMovementAllocationsAsync(transaction, original.Id, cancellationToken);
		var policy = await _repository.GetInventoryPolicyAsync(transaction, original.InventoryId, cancellationToken)
			?? throw new InvalidOperationException($"Inventory '{original.InventoryId}' was not found for traceability reversal.");
		if (policy.TrackingMode == ItemTrackingMode.None)
		{
			if (allocations.Count != 0) throw new InvalidOperationException("Untracked item has unexpected traceability allocations.");
			return;
		}
		if (allocations.Count == 0) throw new InvalidOperationException($"Tracked movement '{original.Id}' has no serial/lot allocation and cannot be reversed safely.");
		foreach (var allocation in allocations.OrderBy(value => value.Code, StringComparer.Ordinal))
		{
			var reversedQuantity = -allocation.Quantity;
			if (reversedQuantity < 0)
			{
				var available = await _repository.GetInventoryBalanceAsync(transaction, allocation.TrackingUnitId, original.InventoryId, cancellationToken);
				if (available < -reversedQuantity) throw new InvalidOperationException($"Tracking unit '{allocation.Code}' is no longer available at the original location, so movement '{original.Id}' cannot be reversed.");
			}
			await _repository.AddMovementAllocationAsync(transaction, reversal.Id, allocation.TrackingUnitId, reversedQuantity, cancellationToken);
		}
	}

	public async Task<InventoryItemPolicy> GetPolicyAsync(DatabaseTransactionContext transaction, long inventoryId, CancellationToken cancellationToken) =>
		await _repository.GetInventoryPolicyAsync(transaction, inventoryId, cancellationToken)
			?? throw new InvalidOperationException($"Inventory '{inventoryId}' was not found.");

	public static void EnsurePhysicalStockItem(InventoryItemPolicy policy, string operation)
	{
		if (policy.ItemType != ItemType.StockItem)
			throw new InvalidOperationException($"Item '{policy.PartNumber}' is {policy.ItemType} and cannot participate in physical {operation}.");
	}

	public static string? GetLifecycleWarning(InventoryItemPolicy policy)
	{
		var replacement = policy.ReplacementPartNumber is null ? string.Empty : $" Replacement: {policy.ReplacementPartNumber}.";
		if (policy.LifecycleStatus == ItemLifecycleStatus.EndOfLife) return $"Item '{policy.PartNumber}' is end-of-life.{replacement}";
		if (policy.EndOfSupportDate is DateTime support && support.Date < DateTime.Today) return $"Support for item '{policy.PartNumber}' ended on {support:yyyy-MM-dd}.{replacement}";
		return null;
	}

	public static void EnsurePurchasable(InventoryItemPolicy policy, DateTime orderDate)
	{
		if (policy.LifecycleStatus is ItemLifecycleStatus.Discontinued or ItemLifecycleStatus.Obsolete) throw LifecycleBlocked(policy, "purchasing");
		if (policy.LastBuyDate is DateTime lastBuy && orderDate.Date > lastBuy.Date)
			throw new InvalidOperationException($"Item '{policy.PartNumber}' passed its last-buy date ({lastBuy:yyyy-MM-dd}).{ReplacementText(policy)}");
	}

	public static void EnsureSellable(InventoryItemPolicy policy)
	{
		if (policy.LifecycleStatus is ItemLifecycleStatus.Discontinued or ItemLifecycleStatus.Obsolete) throw LifecycleBlocked(policy, "sales");
	}

	private async Task<MovementTrackingAllocation> ResolveInboundUnitAsync(DatabaseTransactionContext transaction, InventoryItemPolicy policy, MovementTrackingAllocation? unit, TrackingAllocationInput input, CancellationToken cancellationToken)
	{
		if (unit is null)
		{
			var id = await _repository.CreateUnitAsync(transaction, policy.ItemId, policy.TrackingMode, input.Code, input.ExpiryDate, cancellationToken);
			return new MovementTrackingAllocation { TrackingUnitId = id, Code = input.Code, ExpiryDate = input.ExpiryDate, Version = 1 };
		}
		if (policy.TrackingMode == ItemTrackingMode.SerialNumber)
		{
			var globalBalance = await _repository.GetGlobalBalanceAsync(transaction, unit.TrackingUnitId, cancellationToken);
			if (globalBalance != 0) throw new InvalidOperationException($"Serial number '{input.Code}' for item '{policy.PartNumber}' is already in stock.");
		}
		if (unit.ExpiryDate is null && input.ExpiryDate is not null)
		{
			await _repository.SetExpiryIfMissingAsync(transaction, unit.TrackingUnitId, input.ExpiryDate.Value, cancellationToken);
			unit.ExpiryDate = input.ExpiryDate;
		}
		else if (unit.ExpiryDate is DateTime existing && input.ExpiryDate is DateTime supplied && existing.Date != supplied.Date)
		{
			throw new InvalidOperationException($"Tracking unit '{input.Code}' already has expiry date {existing:yyyy-MM-dd}.");
		}
		return unit;
	}

	private async Task<MovementTrackingAllocation> ResolveOutboundUnitAsync(DatabaseTransactionContext transaction, InventoryItemPolicy policy, MovementTrackingAllocation? unit, TrackingAllocationInput input, long inventoryId, CancellationToken cancellationToken)
	{
		if (unit is null) throw new InvalidOperationException($"Tracking unit '{input.Code}' does not exist for item '{policy.PartNumber}'.");
		if (unit.IsBlocked) throw new InvalidOperationException($"Tracking unit '{input.Code}' is blocked: {unit.BlockReason ?? "no reason supplied"}.");
		if (unit.ExpiryDate is DateTime expiry && expiry.Date < DateTime.Today) throw new InvalidOperationException($"Tracking unit '{input.Code}' expired on {expiry:yyyy-MM-dd}.");
		var available = await _repository.GetInventoryBalanceAsync(transaction, unit.TrackingUnitId, inventoryId, cancellationToken);
		if (available < input.Quantity) throw new InsufficientStockException();
		return unit;
	}

	private static IReadOnlyList<TrackingAllocationInput> NormalizeAllocations(IReadOnlyList<TrackingAllocationInput>? allocations)
	{
		if (allocations is null || allocations.Count == 0) return [];
		var result = new List<TrackingAllocationInput>(allocations.Count);
		foreach (var allocation in allocations)
		{
			var code = allocation.Code?.Trim().ToUpperInvariant() ?? string.Empty;
			if (code.Length == 0) throw new ArgumentException("Serial/lot code is required.");
			if (code.Length > 128) throw new ArgumentException("Serial/lot code must not exceed 128 characters.");
			if (allocation.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(allocation.Quantity), "Tracking quantity must be positive.");
			result.Add(new TrackingAllocationInput { Code = code, Quantity = allocation.Quantity, ExpiryDate = allocation.ExpiryDate?.Date });
		}
		if (result.Select(value => value.Code).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new ArgumentException("Each serial/lot code may appear only once per movement.");
		return result;
	}

	private static void ValidateAllocationShape(InventoryItemPolicy policy, StockMovement movement, IReadOnlyList<TrackingAllocationInput> allocations)
	{
		if (allocations.Count == 0) throw new InvalidOperationException($"Item '{policy.PartNumber}' requires {policy.TrackingMode} tracking data.");
		if (allocations.Sum(value => value.Quantity) != Math.Abs(movement.Quantity)) throw new InvalidOperationException($"Tracking quantities for item '{policy.PartNumber}' must equal movement quantity {Math.Abs(movement.Quantity)}.");
		if (policy.TrackingMode == ItemTrackingMode.SerialNumber && allocations.Any(value => value.Quantity != 1)) throw new InvalidOperationException("Each serial number must represent exactly one unit.");
	}

	private static bool TryGetCaptureScope(StockMovement movement, out string scope)
	{
		var reference = movement.Reference?.Trim() ?? string.Empty;
		scope = movement.MovementType switch
		{
			StockMovementType.Purchase when reference.StartsWith("GR-", StringComparison.OrdinalIgnoreCase) => "goods-receipt",
			StockMovementType.Withdrawal when reference.StartsWith("Material Issue ", StringComparison.OrdinalIgnoreCase) => "material-issue",
			StockMovementType.Correction when reference.StartsWith("IC-", StringComparison.OrdinalIgnoreCase) => "inventory-count",
			StockMovementType.TransferOut or StockMovementType.TransferIn when reference.StartsWith("Stock Transfer ", StringComparison.OrdinalIgnoreCase) => "stock-transfer",
			StockMovementType.MaterialReturn when reference.StartsWith("Material Return ", StringComparison.OrdinalIgnoreCase) => "material-return",
			StockMovementType.SupplierReturn when reference.StartsWith("Supplier Return ", StringComparison.OrdinalIgnoreCase) => "supplier-return",
			StockMovementType.SalesShipment when reference.StartsWith("Shipment ", StringComparison.OrdinalIgnoreCase) => "shipment",
			StockMovementType.CustomerReturn when reference.StartsWith("Customer Return ", StringComparison.OrdinalIgnoreCase) => "customer-return",
			_ => string.Empty
		};
		return scope.Length > 0;
	}

	private static InvalidOperationException LifecycleBlocked(InventoryItemPolicy policy, string operation) =>
		new($"Item '{policy.PartNumber}' is {policy.LifecycleStatus} and is blocked for new {operation}.{ReplacementText(policy)}");

	private static string ReplacementText(InventoryItemPolicy policy) => policy.ReplacementPartNumber is null ? string.Empty : $" Use replacement item '{policy.ReplacementPartNumber}'.";
}
