// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceInventoryCostingService
{
	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceInventoryAccountingRepository _accounting;
	private readonly FinanceInventoryCostingRepository _costing;
	private readonly FinanceAccountsPayableRepository? _payables;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceInventoryCostingService(IDatabaseTransactionRunner transactions, FinanceInventoryAccountingRepository accounting, FinanceInventoryCostingRepository costing, FinanceGeneralLedgerService generalLedger, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization, FinanceAccountsPayableRepository? payables = null)
	{
		_transactions = transactions;
		_accounting = accounting;
		_costing = costing;
		_generalLedger = generalLedger;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
		_payables = payables;
	}

	public Task<FinanceInventoryAccountingPolicy?> GetPolicyAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetPolicyAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceInventoryValuationSummary>> GetValuationSummaryAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetValuationSummaryAsync(cancellationToken);
	}

	public Task<IReadOnlyList<FinanceInventoryReconciliationRun>> GetRecentReconciliationsAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _costing.GetRecentReconciliationsAsync(20, cancellationToken);
	}

	public async Task<FinanceInventoryAccountingPolicy> SavePolicyAsync(FinanceInventoryAccountingPolicy policy, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(policy);
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		RequireUser();
		if (policy.InventoryControlAccountId == Guid.Empty || policy.InventoryAdjustmentPostingProfileId <= 0 || policy.PurchaseVariancePostingProfileId <= 0 || policy.LandedCostPostingProfileId <= 0) throw new ArgumentException("Inventory control account and all F4 posting profiles are required.", nameof(policy));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var configuration = await RequireConfigurationAsync(transaction, token);
			var receipt = await RequireProfileAsync(transaction, configuration.GoodsReceiptPostingProfileId, configuration, FinanceInventoryAccountingEvents.GoodsReceipt, [FinanceInventoryAccountingAmountKeys.Cost], token);
			var adjustment = await RequireProfileAsync(transaction, policy.InventoryAdjustmentPostingProfileId, configuration, FinanceInventoryAccountingEvents.InventoryAdjustment, [FinanceInventoryAccountingAmountKeys.AdjustmentDebit, FinanceInventoryAccountingAmountKeys.AdjustmentCredit], token);
			var variance = await RequireProfileAsync(transaction, policy.PurchaseVariancePostingProfileId, configuration, FinanceInventoryAccountingEvents.PurchaseVariance, [FinanceInventoryAccountingAmountKeys.VarianceDebit, FinanceInventoryAccountingAmountKeys.VarianceCredit], token);
			var landed = await RequireProfileAsync(transaction, policy.LandedCostPostingProfileId, configuration, FinanceInventoryAccountingEvents.LandedCost, [FinanceInventoryAccountingAmountKeys.Cost], token);
			if (adjustment.AccountingBookId != receipt.AccountingBookId || variance.AccountingBookId != receipt.AccountingBookId || landed.AccountingBookId != receipt.AccountingBookId) throw new InvalidOperationException("All Inventory Accounting profiles must use the same accounting book.");
			if (!await _costing.AccountBelongsToBookAsync(transaction, receipt.AccountingBookId, policy.InventoryControlAccountId, token)) throw new InvalidOperationException("Inventory control account does not belong to the configured accounting book.");
			var before = await _costing.GetPolicyAsync(transaction, token);
			if (policy.Id == 0)
			{
				if (before is not null) throw new InvalidOperationException("Inventory Accounting policy already exists.");
				var id = await _costing.CreatePolicyAsync(transaction, policy, token);
				var created = policy with { Id = id, Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
				return created;
			}
			if (before is null || before.Id != policy.Id || before.Version != policy.Version) throw new ConcurrencyConflictException("inventory accounting policy");
			if (await _costing.UpdatePolicyAsync(transaction, policy, before.Version, token) != 1) throw new ConcurrencyConflictException("inventory accounting policy");
			var after = policy with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceInventoryPurchaseVariance?> ProcessPurchaseVarianceAsync(long supplierDocumentId, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user = RequireUser();
		if (_payables is null) throw new InvalidOperationException("Accounts Payable integration is unavailable.");
		if (supplierDocumentId <= 0) throw new ArgumentOutOfRangeException(nameof(supplierDocumentId));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var document = await _payables.GetDocumentAsync(transaction, supplierDocumentId, token) ?? throw new InvalidOperationException("Supplier document was not found.");
			if (document.Status != FinancePayableDocumentStatus.Posted) throw new InvalidOperationException("Purchase variance can only be posted for a posted supplier document.");
			return await RecordPurchaseVarianceAsync(transaction, document, user.Id, token);
		}, cancellationToken);
	}

	public async Task ReversePurchaseVarianceAsync(long supplierDocumentId, Guid operationId, DateOnly postingDate, string reason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user = RequireUser();
		if (supplierDocumentId <= 0 || operationId == Guid.Empty || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Supplier document, operation ID and reversal reason are required.");
		await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await ReversePurchaseVarianceAsync(transaction, supplierDocumentId, operationId, postingDate, reason.Trim(), user.Id, token);
		}, cancellationToken);
	}

	public async Task<FinanceInventoryLandedCostOperation> AllocateLandedCostAsync(FinanceInventoryLandedCostRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty || request.Amount <= 0m || request.LayerIds.Count == 0) throw new ArgumentException("Operation ID, positive amount and valuation layers are required.", nameof(request));
		var normalized = request with { LayerIds = request.LayerIds.Distinct().OrderBy(value => value).ToArray(), Reference = Normalize(request.Reference) };
		var requestHash = HashLandedCost(normalized);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _costing.FindLandedCostByOperationAsync(transaction, normalized.OperationId, token);
			if (existing is not null)
			{
				if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)) throw new InvalidOperationException("Landed-cost operation ID is already assigned to different content.");
				return existing with { Allocations = await _costing.GetLandedCostAllocationsAsync(transaction, existing.Id, token) };
			}
			var configuration = await RequireConfigurationAsync(transaction, token);
			var policy = await RequirePolicyAsync(transaction, token);
			var profile = await RequireProfileAsync(transaction, policy.LandedCostPostingProfileId, configuration, FinanceInventoryAccountingEvents.LandedCost, [FinanceInventoryAccountingAmountKeys.Cost], token);
			var layers = await _costing.LockLayersAsync(transaction, normalized.LayerIds, token);
			if (layers.Any(layer => layer.ReversedAtUtc is not null || layer.RemainingQuantity != layer.OriginalQuantity)) throw new InvalidOperationException("Landed cost can only be allocated to fully unconsumed valuation layers.");
			if (layers.Any(layer => layer.AccountingBookId != profile.AccountingBookId)) throw new InvalidOperationException("Selected valuation layers belong to another accounting book.");
			if (layers.Any(layer => layer.Currency != normalized.Currency)) throw new InvalidOperationException("Cross-currency landed-cost allocation is not permitted. Convert the landed cost to the valuation-layer currency first.");
			var weights = layers.Select(layer => normalized.AllocationMethod == FinanceLandedCostAllocationMethod.Quantity ? (decimal)layer.OriginalQuantity : layer.OriginalQuantity * layer.UnitCost).ToArray();
			var totalWeight = weights.Sum();
			if (totalWeight <= 0m) throw new InvalidOperationException("Selected layers do not provide a positive allocation basis.");
			var allocations = new List<FinanceInventoryLandedCostAllocation>();
			decimal allocated = 0m;
			for (var index = 0; index < layers.Count; index++)
			{
				var amount = index == layers.Count - 1 ? normalized.Amount - allocated : decimal.Round(normalized.Amount * weights[index] / totalWeight, 9, MidpointRounding.ToEven);
				allocated += amount;
				var increase = amount / layers[index].OriginalQuantity;
				if (await _costing.UpdateLayerUnitCostAsync(transaction, layers[index].Id, layers[index].UnitCost, layers[index].UnitCost + increase, token) != 1) throw new ConcurrencyConflictException("inventory valuation landed cost");
				allocations.Add(new FinanceInventoryLandedCostAllocation { LayerId = layers[index].Id, Amount = amount, UnitCostIncrease = increase });
			}
			var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, normalized.PostingDate, token);
			var exchangeRateId = normalized.ExchangeRateId ?? await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, normalized.Currency, normalized.PostingDate, token);
			var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
			{
				OperationId = normalized.OperationId,
				PostingProfileId = profile.Id,
				AccountingPeriodId = periodId,
				PostingDate = normalized.PostingDate,
				Description = "Inventory landed cost",
				SourceId = normalized.OperationId.ToString("D"),
				SourceReference = normalized.Reference,
				TransactionCurrency = normalized.Currency,
				ExchangeRateId = exchangeRateId,
				Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal) { [FinanceInventoryAccountingAmountKeys.Cost] = normalized.Amount }
			}, user.Id, token);
			var value = new FinanceInventoryLandedCostOperation { OperationId = normalized.OperationId, RequestHash = requestHash, PostingDate = normalized.PostingDate, Currency = normalized.Currency, Amount = normalized.Amount, AllocationMethod = normalized.AllocationMethod, Reference = normalized.Reference, JournalEntryId = journal.Id, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id };
			var id = await _costing.CreateLandedCostAsync(transaction, value, token);
			foreach (var allocation in allocations) await _costing.CreateLandedCostAllocationAsync(transaction, allocation with { OperationId = id }, token);
			var created = value with { Id = id, Allocations = allocations.Select(allocation => allocation with { OperationId = id }).ToArray() };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	public async Task<FinanceInventoryLandedCostOperation> ReverseLandedCostAsync(long id, Guid operationId, DateOnly postingDate, string reason, CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		var user = RequireUser();
		if (id <= 0 || operationId == Guid.Empty || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Landed cost, operation ID and reversal reason are required.");
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var value = await _costing.GetLandedCostAsync(transaction, id, token) ?? throw new InvalidOperationException("Landed-cost operation was not found.");
			if (value.ReversedAtUtc is not null)
			{
				if (value.ReversalOperationId == operationId) return value;
				throw new InvalidOperationException("Landed-cost operation has already been reversed.");
			}
			var configuration = await RequireConfigurationAsync(transaction, token);
			var policy = await RequirePolicyAsync(transaction, token);
			var profile = await RequireProfileAsync(transaction, policy.LandedCostPostingProfileId, configuration, FinanceInventoryAccountingEvents.LandedCost, [FinanceInventoryAccountingAmountKeys.Cost], token);
			var allocations = await _costing.GetLandedCostAllocationsAsync(transaction, id, token);
			var layers = await _costing.LockLayersAsync(transaction, allocations.Select(allocation => allocation.LayerId).ToArray(), token);
			foreach (var allocation in allocations)
			{
				var layer = layers.Single(value => value.Id == allocation.LayerId);
				if (layer.RemainingQuantity != layer.OriginalQuantity || layer.ReversedAtUtc is not null) throw new InvalidOperationException("Landed cost cannot be reversed after a selected layer has been consumed or reversed.");
				if (await _costing.UpdateLayerUnitCostAsync(transaction, layer.Id, layer.UnitCost, layer.UnitCost - allocation.UnitCostIncrease, token) != 1) throw new ConcurrencyConflictException("landed-cost reversal");
			}
			var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, token);
			var journal = await _generalLedger.ReverseInTransactionAsync(transaction, value.JournalEntryId, operationId, periodId, postingDate, profile.NumberSequenceCode, reason.Trim(), user.Id, token);
			var now = DateTime.UtcNow;
			if (await _costing.MarkLandedCostReversedAsync(transaction, id, operationId, journal.Id, now, user.Id, token) != 1) throw new ConcurrencyConflictException("landed-cost reversal");
			var after = value with { ReversalOperationId = operationId, ReversalJournalEntryId = journal.Id, ReversedAtUtc = now, ReversedByUserId = user.Id, Allocations = allocations };
			await _auditEntries.CreateAsync(transaction, _audit.CreateActionEntry(id, "Reversed", value, after), token);
			return after;
		}, cancellationToken);
	}

	public async Task<FinanceInventoryReconciliationRun> ReconcileAsync(FinanceInventoryReconciliationRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		var user = RequireUser();
		if (request.OperationId == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(request));
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			var existing = await _costing.FindRunAsync(transaction, request.OperationId, token);
			if (existing is not null) return existing;
			var configuration = await RequireConfigurationAsync(transaction, token);
			var policy = await RequirePolicyAsync(transaction, token);
			var profile = await RequireProfileAsync(transaction, configuration.GoodsReceiptPostingProfileId, configuration, FinanceInventoryAccountingEvents.GoodsReceipt, [FinanceInventoryAccountingAmountKeys.Cost], token);
			var reportingCurrency = await _costing.GetBookReportingCurrencyAsync(transaction, profile.AccountingBookId, token);
			var rows = await _costing.GetValuationReportingRowsAsync(transaction, profile.AccountingBookId, request.AsOfDate, token);
			var currentActiveLanded = await _costing.GetCurrentActiveLandedRowsAsync(transaction, profile.AccountingBookId, token);
			var historicalLanded = await _costing.GetLandedReportingRowsAsync(transaction, profile.AccountingBookId, request.AsOfDate, token);
			var currentByLayer = currentActiveLanded.GroupBy(value => value.LayerId).ToDictionary(group => group.Key, group => group.ToArray());
			var historicalByLayer = historicalLanded.GroupBy(value => value.LayerId).ToDictionary(group => group.Key, group => group.ToArray());
			var lines = new List<FinanceInventoryReconciliationLine>();
			foreach (var group in rows.Where(row => row.Quantity > 0).GroupBy(row => row.ItemId))
			{
				decimal itemValue = 0m;
				var quantity = 0;
				foreach (var row in group)
				{
					var activeNow = currentByLayer.GetValueOrDefault(row.LayerId) ?? [];
					var activeAtDate = historicalByLayer.GetValueOrDefault(row.LayerId) ?? [];
					var baseUnitCost = row.CurrentUnitCost - activeNow.Sum(value => value.UnitCostIncrease);
					var reportingUnitCost = baseUnitCost * row.ReceiptExchangeRate + activeAtDate.Sum(value => value.UnitCostIncrease * value.ExchangeRate);
					itemValue += row.Quantity * reportingUnitCost;
					quantity += row.Quantity;
				}
				lines.Add(new FinanceInventoryReconciliationLine { ItemId = group.Key, Quantity = quantity, ReportingValue = decimal.Round(itemValue, 9, MidpointRounding.ToEven) });
			}
			var valuation = lines.Sum(line => line.ReportingValue);
			var generalLedger = await _costing.GetGlBalanceAsync(transaction, profile.AccountingBookId, policy.InventoryControlAccountId, request.AsOfDate, token);
			var value = new FinanceInventoryReconciliationRun { OperationId = request.OperationId, AccountingBookId = profile.AccountingBookId, InventoryControlAccountId = policy.InventoryControlAccountId, AsOfDate = request.AsOfDate, ReportingCurrency = reportingCurrency, ValuationAmount = valuation, GeneralLedgerAmount = generalLedger, Difference = valuation - generalLedger, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = user.Id, Lines = lines };
			var id = await _costing.CreateRunAsync(transaction, value, token);
			foreach (var line in lines) await _costing.CreateRunLineAsync(transaction, id, line, token);
			var created = value with { Id = id };
			await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), token);
			return created;
		}, cancellationToken);
	}

	internal async Task<FinanceInventoryAccountingEvent?> RecordInventoryAdjustmentAsync(DatabaseTransactionContext transaction, StockMovement movement, long itemId, DateOnly postingDate, string? sourceReference, long userId, CancellationToken cancellationToken)
	{
		if (movement.Id <= 0 || movement.MovementType != StockMovementType.Correction || movement.Quantity == 0) throw new InvalidOperationException("A non-zero posted inventory correction is required for Inventory Accounting.");
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		var policy = await GetActivePolicyAsync(transaction, cancellationToken);
		if (configuration is null || policy is null) return null;
		var existing = await _accounting.GetEventAsync(transaction, movement.Id, cancellationToken);
		if (existing is not null) return existing;
		var profile = await RequireProfileAsync(transaction, policy.InventoryAdjustmentPostingProfileId, configuration, FinanceInventoryAccountingEvents.InventoryAdjustment, [FinanceInventoryAccountingAmountKeys.AdjustmentDebit, FinanceInventoryAccountingAmountKeys.AdjustmentCredit], cancellationToken);
		var now = DateTime.UtcNow;
		decimal amount;
		if (movement.Quantity > 0)
		{
			var available = await _accounting.LockAvailableLayersAsync(transaction, profile.AccountingBookId, itemId, cancellationToken);
			if (available.Count == 0 || available.Sum(layer => (long)layer.RemainingQuantity) <= 0) throw new InvalidOperationException("A positive inventory correction requires an existing valued FIFO balance for the item.");
			if (available.Any(layer => layer.Currency != configuration.PurchaseOrderPriceCurrency)) throw new InvalidOperationException("Valuation layers use a currency outside the active Inventory Accounting configuration.");
			var totalQuantity = available.Sum(layer => (decimal)layer.RemainingQuantity);
			var unitCost = available.Sum(layer => layer.RemainingQuantity * layer.UnitCost) / totalQuantity;
			amount = checked(unitCost * movement.Quantity);
			await _accounting.CreateLayerAsync(transaction, new FinanceInventoryValuationLayer { AccountingBookId = profile.AccountingBookId, ItemId = itemId, SourceMovementId = movement.Id, AcquiredDate = postingDate, Currency = configuration.PurchaseOrderPriceCurrency, OriginalQuantity = movement.Quantity, RemainingQuantity = movement.Quantity, UnitCost = unitCost, CreatedAtUtc = now, CreatedByUserId = userId }, cancellationToken);
		}
		else
		{
			var required = checked(-movement.Quantity);
			var layers = await _accounting.LockAvailableLayersAsync(transaction, profile.AccountingBookId, itemId, cancellationToken);
			if (layers.Any(layer => layer.Currency != configuration.PurchaseOrderPriceCurrency)) throw new InvalidOperationException("Valuation layers use a currency outside the active Inventory Accounting configuration.");
			if (layers.Sum(layer => (long)layer.RemainingQuantity) < required) throw new InvalidOperationException("Inventory correction would create a negative valued inventory quantity.");
			var remaining = required;
			amount = 0m;
			foreach (var layer in layers)
			{
				if (remaining == 0) break;
				var consumed = Math.Min(remaining, layer.RemainingQuantity);
				var consumptionAmount = checked(consumed * layer.UnitCost);
				if (await _accounting.SetRemainingQuantityAsync(transaction, layer.Id, layer.RemainingQuantity, layer.RemainingQuantity - consumed, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory valuation layer");
				await _accounting.CreateConsumptionAsync(transaction, new FinanceInventoryValuationConsumption { MovementId = movement.Id, LayerId = layer.Id, Quantity = consumed, UnitCost = layer.UnitCost, Amount = consumptionAmount, CreatedAtUtc = now, CreatedByUserId = userId }, cancellationToken);
				amount += consumptionAmount;
				remaining -= consumed;
			}
		}
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var exchangeRateId = await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, configuration.PurchaseOrderPriceCurrency, postingDate, cancellationToken);
		var operationId = DeterministicGuid($"InventoryMovement:{movement.Id}:Adjustment");
		var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
		{
			OperationId = operationId,
			PostingProfileId = profile.Id,
			AccountingPeriodId = periodId,
			PostingDate = postingDate,
			Description = $"Inventory correction for stock movement {movement.Id}",
			SourceId = movement.Id.ToString(CultureInfo.InvariantCulture),
			SourceReference = sourceReference,
			TransactionCurrency = configuration.PurchaseOrderPriceCurrency,
			ExchangeRateId = exchangeRateId,
			Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal)
			{
				[FinanceInventoryAccountingAmountKeys.AdjustmentDebit] = movement.Quantity > 0 ? amount : 0m,
				[FinanceInventoryAccountingAmountKeys.AdjustmentCredit] = movement.Quantity < 0 ? amount : 0m
			}
		}, userId, cancellationToken);
		var value = new FinanceInventoryAccountingEvent { MovementId = movement.Id, Kind = FinanceInventoryAccountingEventKind.InventoryAdjustment, AccountingBookId = profile.AccountingBookId, ItemId = itemId, Quantity = movement.Quantity, Currency = configuration.PurchaseOrderPriceCurrency, Amount = amount, JournalEntryId = journal.Id, OperationId = operationId, CreatedAtUtc = now, CreatedByUserId = userId };
		var id = await _accounting.CreateEventAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), cancellationToken);
		return created;
	}

	internal async Task<FinanceInventoryAccountingEvent?> ReverseInventoryAdjustmentAsync(DatabaseTransactionContext transaction, StockMovement original, StockMovement reversal, DateOnly postingDate, string reason, long userId, CancellationToken cancellationToken)
	{
		if (reversal.ReversalOfMovementId != original.Id) throw new InvalidOperationException("Inventory adjustment reversal does not reference the original movement.");
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		var policy = await GetActivePolicyAsync(transaction, cancellationToken);
		if (configuration is null || policy is null) return null;
		var originalEvent = await _accounting.GetEventAsync(transaction, original.Id, cancellationToken);
		if (originalEvent is null) return null;
		if (originalEvent.Kind != FinanceInventoryAccountingEventKind.InventoryAdjustment) throw new InvalidOperationException("The original accounting event is not an inventory adjustment.");
		var existing = await _accounting.GetEventAsync(transaction, reversal.Id, cancellationToken);
		if (existing is not null) return existing;
		var profile = await RequireProfileAsync(transaction, policy.InventoryAdjustmentPostingProfileId, configuration, FinanceInventoryAccountingEvents.InventoryAdjustment, [FinanceInventoryAccountingAmountKeys.AdjustmentDebit, FinanceInventoryAccountingAmountKeys.AdjustmentCredit], cancellationToken);
		if (profile.AccountingBookId != originalEvent.AccountingBookId) throw new InvalidOperationException("Inventory adjustment reversal profile no longer belongs to the original accounting book.");
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var operationId = DeterministicGuid($"InventoryMovement:{reversal.Id}:AdjustmentReversal");
		var journal = await _generalLedger.ReverseInTransactionAsync(transaction, originalEvent.JournalEntryId, operationId, periodId, postingDate, profile.NumberSequenceCode, reason.Trim(), userId, cancellationToken);
		var now = DateTime.UtcNow;
		if (original.Quantity > 0)
		{
			var layer = await _accounting.GetLayerBySourceAsync(transaction, original.Id, cancellationToken) ?? throw new InvalidOperationException("Inventory-adjustment valuation layer was not found.");
			if (layer.ReversedAtUtc is not null || layer.RemainingQuantity != layer.OriginalQuantity) throw new InvalidOperationException("A positive inventory adjustment cannot be reversed after its valuation layer has been consumed.");
			if (await _accounting.MarkLayerReversedAsync(transaction, layer.Id, layer.RemainingQuantity, now, userId, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory adjustment layer reversal");
		}
		else
		{
			var consumptions = await _accounting.GetActiveConsumptionsAsync(transaction, original.Id, cancellationToken);
			if (consumptions.Count == 0) throw new InvalidOperationException("Inventory-adjustment valuation consumptions were not found.");
			foreach (var consumption in consumptions)
			{
				var layer = await _accounting.LockLayerAsync(transaction, consumption.LayerId, cancellationToken) ?? throw new InvalidOperationException("A consumed valuation layer was not found.");
				var restored = checked(layer.RemainingQuantity + consumption.Quantity);
				if (layer.ReversedAtUtc is not null || restored > layer.OriginalQuantity) throw new InvalidOperationException("Valuation layer cannot accept the inventory-adjustment reversal quantity.");
				if (await _accounting.SetRemainingQuantityAsync(transaction, layer.Id, layer.RemainingQuantity, restored, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory adjustment layer restoration");
				if (await _accounting.MarkConsumptionReversedAsync(transaction, consumption.Id, now, userId, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory adjustment consumption reversal");
			}
		}
		var value = new FinanceInventoryAccountingEvent { MovementId = reversal.Id, Kind = FinanceInventoryAccountingEventKind.InventoryAdjustmentReversal, AccountingBookId = originalEvent.AccountingBookId, ItemId = originalEvent.ItemId, Quantity = reversal.Quantity, Currency = originalEvent.Currency, Amount = originalEvent.Amount, JournalEntryId = journal.Id, OperationId = operationId, ReversalOfMovementId = original.Id, CreatedAtUtc = now, CreatedByUserId = userId };
		var id = await _accounting.CreateEventAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), cancellationToken);
		return created;
	}

	internal async Task<FinanceInventoryPurchaseVariance?> RecordPurchaseVarianceAsync(DatabaseTransactionContext transaction, FinanceSupplierDocument document, long userId, CancellationToken cancellationToken)
	{
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		var policy = await GetActivePolicyAsync(transaction, cancellationToken);
		if (configuration is null || policy is null) return null;
		var linkedLines = document.Lines.Where(line => line.PurchaseOrderLineId.HasValue && line.OrderedUnitPrice.HasValue).ToArray();
		if (linkedLines.Length == 0) return null;
		if (document.Currency != configuration.PurchaseOrderPriceCurrency) throw new InvalidOperationException("Purchase variance requires supplier-document currency to match the configured purchase-order price currency.");
		var existing = await _costing.GetPurchaseVarianceAsync(transaction, document.Id, cancellationToken);
		if (existing is not null) return existing;
		var expected = linkedLines.Sum(line => line.Quantity * line.OrderedUnitPrice!.Value);
		var actual = linkedLines.Sum(line => line.NetAmount);
		var signedVariance = (actual - expected) * (document.Kind == FinancePayableDocumentKind.Invoice ? 1m : -1m);
		if (signedVariance == 0m) return null;
		var profile = await RequireProfileAsync(transaction, policy.PurchaseVariancePostingProfileId, configuration, FinanceInventoryAccountingEvents.PurchaseVariance, [FinanceInventoryAccountingAmountKeys.VarianceDebit, FinanceInventoryAccountingAmountKeys.VarianceCredit], cancellationToken);
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, document.DocumentDate, cancellationToken);
		var exchangeRateId = await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, document.Currency, document.DocumentDate, cancellationToken);
		var operationId = DeterministicGuid($"SupplierDocument:{document.Id}:PurchaseVariance");
		var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
		{
			OperationId = operationId,
			PostingProfileId = profile.Id,
			AccountingPeriodId = periodId,
			PostingDate = document.DocumentDate,
			Description = $"Purchase variance {document.SupplierDocumentNumber}",
			SourceId = document.Id.ToString(CultureInfo.InvariantCulture),
			SourceReference = document.SupplierDocumentNumber,
			TransactionCurrency = document.Currency,
			ExchangeRateId = exchangeRateId,
			Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal)
			{
				[FinanceInventoryAccountingAmountKeys.VarianceDebit] = Math.Max(signedVariance, 0m),
				[FinanceInventoryAccountingAmountKeys.VarianceCredit] = Math.Max(-signedVariance, 0m)
			}
		}, userId, cancellationToken);
		var value = new FinanceInventoryPurchaseVariance { SupplierDocumentId = document.Id, OperationId = operationId, Currency = document.Currency, ExpectedNetAmount = expected, ActualNetAmount = actual, SignedVarianceAmount = signedVariance, JournalEntryId = journal.Id, CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = userId };
		var id = await _costing.CreatePurchaseVarianceAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(id, created), cancellationToken);
		return created;
	}

	internal async Task ReversePurchaseVarianceAsync(DatabaseTransactionContext transaction, long supplierDocumentId, Guid parentOperationId, DateOnly postingDate, string reason, long userId, CancellationToken cancellationToken)
	{
		var value = await _costing.GetPurchaseVarianceAsync(transaction, supplierDocumentId, cancellationToken);
		if (value is null || value.ReversedAtUtc is not null) return;
		var configuration = await RequireConfigurationAsync(transaction, cancellationToken);
		var policy = await RequirePolicyAsync(transaction, cancellationToken);
		var profile = await RequireProfileAsync(transaction, policy.PurchaseVariancePostingProfileId, configuration, FinanceInventoryAccountingEvents.PurchaseVariance, [FinanceInventoryAccountingAmountKeys.VarianceDebit, FinanceInventoryAccountingAmountKeys.VarianceCredit], cancellationToken);
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var operationId = DeterministicGuid($"{parentOperationId:D}:PurchaseVarianceReversal");
		var journal = await _generalLedger.ReverseInTransactionAsync(transaction, value.JournalEntryId, operationId, periodId, postingDate, profile.NumberSequenceCode, reason, userId, cancellationToken);
		var now = DateTime.UtcNow;
		if (await _costing.MarkPurchaseVarianceReversedAsync(transaction, value.Id, operationId, journal.Id, now, userId, cancellationToken) != 1) throw new ConcurrencyConflictException("purchase variance reversal");
	}

	private async Task<FinanceInventoryAccountingConfiguration> RequireConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		await GetActiveConfigurationAsync(transaction, cancellationToken) ?? throw new InvalidOperationException("Active Inventory Accounting configuration is required.");

	private async Task<FinanceInventoryAccountingConfiguration?> GetActiveConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken)
	{
		var value = await _accounting.GetConfigurationAsync(transaction, cancellationToken);
		return value is { IsActive: true } ? value : null;
	}

	private async Task<FinanceInventoryAccountingPolicy> RequirePolicyAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		await GetActivePolicyAsync(transaction, cancellationToken) ?? throw new InvalidOperationException("Active Inventory Accounting policy is required.");

	private async Task<FinanceInventoryAccountingPolicy?> GetActivePolicyAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken)
	{
		var value = await _costing.GetPolicyAsync(transaction, cancellationToken);
		return value is { IsActive: true } ? value : null;
	}

	private async Task<FinancePostingProfile> RequireProfileAsync(DatabaseTransactionContext transaction, long id, FinanceInventoryAccountingConfiguration configuration, string eventName, IReadOnlyCollection<string> requiredAmountKeys, CancellationToken cancellationToken)
	{
		var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, id, cancellationToken) ?? throw new InvalidOperationException("Inventory Accounting posting profile was not found.");
		if (!profile.IsActive || profile.LegalEntityId != configuration.LegalEntityId || !string.Equals(profile.SourceType, FinanceInventoryAccountingEvents.SourceType, StringComparison.Ordinal) || !string.Equals(profile.SourceEvent, eventName, StringComparison.Ordinal)) throw new InvalidOperationException($"Posting profile must use source InventoryAccounting/{eventName} for the active legal entity.");
		foreach (var key in requiredAmountKeys)
		{
			if (!profile.Lines.Any(line => string.Equals(line.AmountKey, key, StringComparison.Ordinal))) throw new InvalidOperationException($"Posting profile '{profile.Code}' must consume '{key}'.");
		}
		return profile;
	}

	private async Task<Guid> ResolvePeriodAsync(DatabaseTransactionContext transaction, Guid calendarId, DateOnly date, CancellationToken cancellationToken)
	{
		var periods = await _accounting.FindOpenPeriodsAsync(transaction, calendarId, date, cancellationToken);
		if (periods.Count != 1) throw new InvalidOperationException("Posting date must resolve to exactly one open accounting period.");
		return periods[0].Id;
	}

	private User RequireUser() => _authorization.CurrentUser is { IsActive: true } user ? user : throw new UnauthorizedAccessException("An active signed-in user is required for Inventory Accounting.");
	private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static string HashLandedCost(FinanceInventoryLandedCostRequest value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", value.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value.Currency.Value, value.Amount.ToString("G29", CultureInfo.InvariantCulture), (int)value.AllocationMethod, string.Join(",", value.LayerIds), value.Reference ?? string.Empty))));
	private static Guid DeterministicGuid(string value)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		Span<byte> bytes = stackalloc byte[16];
		hash.AsSpan(0, 16).CopyTo(bytes);
		return new Guid(bytes);
	}
}
