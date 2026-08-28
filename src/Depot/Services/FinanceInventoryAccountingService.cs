// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Depot.Data;
using Depot.Models;
using Depot.Repositories;

namespace Depot.Services;

public sealed class FinanceInventoryAccountingService
{
	private const string SourceType = "InventoryAccounting";
	private const string GoodsReceiptEvent = "GoodsReceipt";
	private const string SalesShipmentEvent = "SalesShipment";
	private const string CostAmountKey = "Cost";

	private readonly IDatabaseTransactionRunner _transactions;
	private readonly FinanceInventoryAccountingRepository _inventoryAccounting;
	private readonly FinanceGeneralLedgerService _generalLedger;
	private readonly AuditRepository _auditEntries;
	private readonly AuditService _audit;
	private readonly IAuthorizationService _authorization;

	public FinanceInventoryAccountingService(IDatabaseTransactionRunner transactions, FinanceInventoryAccountingRepository inventoryAccounting, FinanceGeneralLedgerService generalLedger, AuditRepository auditEntries, AuditService audit, IAuthorizationService authorization)
	{
		_transactions = transactions;
		_inventoryAccounting = inventoryAccounting;
		_generalLedger = generalLedger;
		_auditEntries = auditEntries;
		_audit = audit;
		_authorization = authorization;
	}

	public Task<FinanceInventoryAccountingConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default)
	{
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingView);
		return _inventoryAccounting.GetConfigurationAsync(cancellationToken);
	}

	public async Task<FinanceInventoryAccountingConfiguration> SaveConfigurationAsync(FinanceInventoryAccountingConfiguration configuration, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		_authorization.RequirePermission(ApplicationPermission.FinanceInventoryAccountingManage);
		RequireUser();
		ValidateConfiguration(configuration);
		return await _transactions.ExecuteAsync(async (transaction, token) =>
		{
			await ValidateProfilesAsync(transaction, configuration, token);
			var before = await _inventoryAccounting.GetConfigurationAsync(transaction, token);
			if (configuration.Id == 0)
			{
				if (before is not null) throw new InvalidOperationException("Inventory Accounting configuration already exists.");
				var id = await _inventoryAccounting.CreateConfigurationAsync(transaction, configuration, token);
				var created = configuration with { Id = id, Version = 1 };
				await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), token);
				return created;
			}
			if (before is null || before.Id != configuration.Id) throw new InvalidOperationException("Inventory Accounting configuration was not found.");
			if (before.Version != configuration.Version) throw new ConcurrencyConflictException("inventory accounting configuration");
			if (await _inventoryAccounting.UpdateConfigurationAsync(transaction, configuration, before.Version, token) != 1) throw new ConcurrencyConflictException("inventory accounting configuration");
			var after = configuration with { Version = before.Version + 1 };
			await _auditEntries.CreateAsync(transaction, _audit.CreateUpdatedEntry(after.Id, before, after), token);
			return after;
		}, cancellationToken);
	}

	internal async Task<FinanceInventoryAccountingEvent?> RecordGoodsReceiptAsync(DatabaseTransactionContext transaction, StockMovement movement, long itemId, decimal unitCost, DateOnly postingDate, string? sourceReference, long userId, CancellationToken cancellationToken)
	{
		if (movement.Id <= 0 || movement.MovementType != StockMovementType.Purchase || movement.Quantity <= 0) throw new InvalidOperationException("A posted purchase stock movement is required for inventory receipt accounting.");
		if (unitCost < 0m) throw new InvalidOperationException("Inventory receipt unit cost cannot be negative.");
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		if (configuration is null) return null;
		var existing = await _inventoryAccounting.GetEventAsync(transaction, movement.Id, cancellationToken);
		if (existing is not null) return existing;
		var profile = await RequireProfileAsync(transaction, configuration.GoodsReceiptPostingProfileId, configuration, GoodsReceiptEvent, cancellationToken);
		var amount = checked(unitCost * movement.Quantity);
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var exchangeRateId = await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, configuration.PurchaseOrderPriceCurrency, postingDate, cancellationToken);
		var operationId = MovementOperationId(movement.Id, GoodsReceiptEvent);
		var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
		{
			OperationId = operationId,
			PostingProfileId = profile.Id,
			AccountingPeriodId = periodId,
			PostingDate = postingDate,
			Description = $"Inventory receipt for stock movement {movement.Id}",
			SourceId = movement.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			SourceReference = sourceReference,
			TransactionCurrency = configuration.PurchaseOrderPriceCurrency,
			ExchangeRateId = exchangeRateId,
			Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal) { [CostAmountKey] = amount }
		}, userId, cancellationToken);
		var now = DateTime.UtcNow;
		await _inventoryAccounting.CreateLayerAsync(transaction, new FinanceInventoryValuationLayer { AccountingBookId = profile.AccountingBookId, ItemId = itemId, SourceMovementId = movement.Id, AcquiredDate = postingDate, Currency = configuration.PurchaseOrderPriceCurrency, OriginalQuantity = movement.Quantity, RemainingQuantity = movement.Quantity, UnitCost = unitCost, CreatedAtUtc = now, CreatedByUserId = userId }, cancellationToken);
		var value = new FinanceInventoryAccountingEvent { MovementId = movement.Id, Kind = FinanceInventoryAccountingEventKind.GoodsReceipt, AccountingBookId = profile.AccountingBookId, ItemId = itemId, Quantity = movement.Quantity, Currency = configuration.PurchaseOrderPriceCurrency, Amount = amount, JournalEntryId = journal.Id, OperationId = operationId, CreatedAtUtc = now, CreatedByUserId = userId };
		var id = await _inventoryAccounting.CreateEventAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		return created;
	}

	internal async Task<FinanceInventoryAccountingEvent?> RecordSalesShipmentAsync(DatabaseTransactionContext transaction, StockMovement movement, long itemId, DateOnly postingDate, string? sourceReference, long userId, CancellationToken cancellationToken)
	{
		if (movement.Id <= 0 || movement.MovementType != StockMovementType.SalesShipment || movement.Quantity >= 0) throw new InvalidOperationException("A posted sales-shipment stock movement is required for inventory issue accounting.");
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		if (configuration is null) return null;
		var existing = await _inventoryAccounting.GetEventAsync(transaction, movement.Id, cancellationToken);
		if (existing is not null) return existing;
		var profile = await RequireProfileAsync(transaction, configuration.SalesIssuePostingProfileId, configuration, SalesShipmentEvent, cancellationToken);
		if (configuration.ValuationMethod != FinanceInventoryValuationMethod.Fifo) throw new InvalidOperationException("Only FIFO inventory valuation is implemented in the current F4 slice.");
		var required = checked(-movement.Quantity);
		var layers = await _inventoryAccounting.LockAvailableLayersAsync(transaction, profile.AccountingBookId, itemId, cancellationToken);
		if (layers.Any(layer => layer.Currency != configuration.PurchaseOrderPriceCurrency)) throw new InvalidOperationException("Available valuation layers use a currency outside the active Inventory Accounting configuration.");
		if (layers.Sum(layer => (long)layer.RemainingQuantity) < required) throw new InvalidOperationException("Insufficient valued inventory quantity. Inventory Accounting will not create a negative valuation balance.");
		var remaining = required;
		var now = DateTime.UtcNow;
		var amount = 0m;
		foreach (var layer in layers)
		{
			if (remaining == 0) break;
			var consumed = Math.Min(remaining, layer.RemainingQuantity);
			var consumptionAmount = checked(consumed * layer.UnitCost);
			if (await _inventoryAccounting.SetRemainingQuantityAsync(transaction, layer.Id, layer.RemainingQuantity, layer.RemainingQuantity - consumed, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory valuation layer");
			await _inventoryAccounting.CreateConsumptionAsync(transaction, new FinanceInventoryValuationConsumption { MovementId = movement.Id, LayerId = layer.Id, Quantity = consumed, UnitCost = layer.UnitCost, Amount = consumptionAmount, CreatedAtUtc = now, CreatedByUserId = userId }, cancellationToken);
			amount += consumptionAmount;
			remaining -= consumed;
		}
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var exchangeRateId = await _generalLedger.ResolveExchangeRateIdForProfileAsync(transaction, profile.Id, configuration.PurchaseOrderPriceCurrency, postingDate, cancellationToken);
		var operationId = MovementOperationId(movement.Id, SalesShipmentEvent);
		var journal = await _generalLedger.PostFromProfileInTransactionAsync(transaction, new FinanceProfilePostingRequest
		{
			OperationId = operationId,
			PostingProfileId = profile.Id,
			AccountingPeriodId = periodId,
			PostingDate = postingDate,
			Description = $"Inventory cost issue for stock movement {movement.Id}",
			SourceId = movement.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			SourceReference = sourceReference,
			TransactionCurrency = configuration.PurchaseOrderPriceCurrency,
			ExchangeRateId = exchangeRateId,
			Amounts = new Dictionary<string, decimal>(StringComparer.Ordinal) { [CostAmountKey] = amount }
		}, userId, cancellationToken);
		var value = new FinanceInventoryAccountingEvent { MovementId = movement.Id, Kind = FinanceInventoryAccountingEventKind.SalesShipment, AccountingBookId = profile.AccountingBookId, ItemId = itemId, Quantity = movement.Quantity, Currency = configuration.PurchaseOrderPriceCurrency, Amount = amount, JournalEntryId = journal.Id, OperationId = operationId, CreatedAtUtc = now, CreatedByUserId = userId };
		var id = await _inventoryAccounting.CreateEventAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		return created;
	}

	internal async Task<FinanceInventoryAccountingEvent?> ReverseMovementAsync(DatabaseTransactionContext transaction, StockMovement original, StockMovement reversal, DateOnly postingDate, string reason, long userId, CancellationToken cancellationToken)
	{
		if (reversal.ReversalOfMovementId != original.Id) throw new InvalidOperationException("Inventory Accounting reversal does not reference the original movement.");
		var configuration = await GetActiveConfigurationAsync(transaction, cancellationToken);
		if (configuration is null) return null;
		var originalEvent = await _inventoryAccounting.GetEventAsync(transaction, original.Id, cancellationToken);
		if (originalEvent is null) return null;
		var existing = await _inventoryAccounting.GetEventAsync(transaction, reversal.Id, cancellationToken);
		if (existing is not null) return existing;
		var profileId = originalEvent.Kind == FinanceInventoryAccountingEventKind.GoodsReceipt ? configuration.GoodsReceiptPostingProfileId : configuration.SalesIssuePostingProfileId;
		var expectedEvent = originalEvent.Kind == FinanceInventoryAccountingEventKind.GoodsReceipt ? GoodsReceiptEvent : SalesShipmentEvent;
		var profile = await RequireProfileAsync(transaction, profileId, configuration, expectedEvent, cancellationToken);
		if (profile.AccountingBookId != originalEvent.AccountingBookId) throw new InvalidOperationException("The reversal profile no longer belongs to the original accounting book.");
		var periodId = await ResolvePeriodAsync(transaction, configuration.FiscalCalendarId, postingDate, cancellationToken);
		var operationId = MovementOperationId(reversal.Id, $"Reverse:{expectedEvent}");
		var journal = await _generalLedger.ReverseInTransactionAsync(transaction, originalEvent.JournalEntryId, operationId, periodId, postingDate, profile.NumberSequenceCode, reason, userId, cancellationToken);
		var now = DateTime.UtcNow;
		if (originalEvent.Kind == FinanceInventoryAccountingEventKind.GoodsReceipt)
		{
			var layer = await _inventoryAccounting.GetLayerBySourceAsync(transaction, original.Id, cancellationToken) ?? throw new InvalidOperationException("The original goods-receipt valuation layer was not found.");
			if (layer.ReversedAtUtc is not null || layer.RemainingQuantity != layer.OriginalQuantity) throw new InvalidOperationException("A goods receipt cannot be reversed after any of its valuation layer has been consumed. Reverse downstream valued issues first.");
			if (await _inventoryAccounting.MarkLayerReversedAsync(transaction, layer.Id, layer.RemainingQuantity, now, userId, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory valuation layer reversal");
		}
		else if (originalEvent.Kind == FinanceInventoryAccountingEventKind.SalesShipment)
		{
			var consumptions = await _inventoryAccounting.GetActiveConsumptionsAsync(transaction, original.Id, cancellationToken);
			if (consumptions.Count == 0) throw new InvalidOperationException("The sales-shipment valuation consumptions were not found.");
			foreach (var consumption in consumptions)
			{
				var layer = await _inventoryAccounting.LockLayerAsync(transaction, consumption.LayerId, cancellationToken) ?? throw new InvalidOperationException("A consumed valuation layer was not found.");
				var restored = checked(layer.RemainingQuantity + consumption.Quantity);
				if (restored > layer.OriginalQuantity || layer.ReversedAtUtc is not null) throw new InvalidOperationException("The valuation layer cannot accept the shipment reversal quantity.");
				if (await _inventoryAccounting.SetRemainingQuantityAsync(transaction, layer.Id, layer.RemainingQuantity, restored, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory valuation layer restoration");
				if (await _inventoryAccounting.MarkConsumptionReversedAsync(transaction, consumption.Id, now, userId, cancellationToken) != 1) throw new ConcurrencyConflictException("inventory valuation consumption reversal");
			}
		}
		else throw new InvalidOperationException("The original inventory accounting event cannot be reversed by this F4 slice.");
		var value = new FinanceInventoryAccountingEvent { MovementId = reversal.Id, Kind = originalEvent.Kind == FinanceInventoryAccountingEventKind.GoodsReceipt ? FinanceInventoryAccountingEventKind.GoodsReceiptReversal : FinanceInventoryAccountingEventKind.SalesShipmentReversal, AccountingBookId = originalEvent.AccountingBookId, ItemId = originalEvent.ItemId, Quantity = reversal.Quantity, Currency = originalEvent.Currency, Amount = originalEvent.Amount, JournalEntryId = journal.Id, OperationId = operationId, ReversalOfMovementId = original.Id, CreatedAtUtc = now, CreatedByUserId = userId };
		var id = await _inventoryAccounting.CreateEventAsync(transaction, value, cancellationToken);
		var created = value with { Id = id };
		await _auditEntries.CreateAsync(transaction, _audit.CreateCreatedEntry(created.Id, created), cancellationToken);
		return created;
	}

	private async Task<FinanceInventoryAccountingConfiguration?> GetActiveConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken)
	{
		var configuration = await _inventoryAccounting.GetConfigurationAsync(transaction, cancellationToken);
		return configuration is { IsActive: true } ? configuration : null;
	}

	private async Task<FinancePostingProfile> RequireProfileAsync(DatabaseTransactionContext transaction, long profileId, FinanceInventoryAccountingConfiguration configuration, string expectedEvent, CancellationToken cancellationToken)
	{
		var profile = await _generalLedger.GetPostingProfileInTransactionAsync(transaction, profileId, cancellationToken) ?? throw new InvalidOperationException("Inventory Accounting posting profile was not found.");
		if (!profile.IsActive) throw new InvalidOperationException("Inventory Accounting posting profile is inactive.");
		if (profile.LegalEntityId != configuration.LegalEntityId) throw new InvalidOperationException("Inventory Accounting posting profile belongs to another legal entity.");
		if (!string.Equals(profile.SourceType, SourceType, StringComparison.Ordinal) || !string.Equals(profile.SourceEvent, expectedEvent, StringComparison.Ordinal)) throw new InvalidOperationException($"Inventory Accounting profile '{profile.Code}' must use source '{SourceType}/{expectedEvent}'.");
		if (!profile.Lines.Any(line => string.Equals(line.AmountKey, CostAmountKey, StringComparison.Ordinal))) throw new InvalidOperationException($"Inventory Accounting profile '{profile.Code}' must consume the '{CostAmountKey}' amount key.");
		return profile;
	}

	private async Task ValidateProfilesAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingConfiguration configuration, CancellationToken cancellationToken)
	{
		var receipt = await RequireProfileAsync(transaction, configuration.GoodsReceiptPostingProfileId, configuration, GoodsReceiptEvent, cancellationToken);
		var issue = await RequireProfileAsync(transaction, configuration.SalesIssuePostingProfileId, configuration, SalesShipmentEvent, cancellationToken);
		if (receipt.AccountingBookId != issue.AccountingBookId) throw new InvalidOperationException("Inventory Accounting receipt and issue profiles must use the same accounting book.");
	}

	private async Task<Guid> ResolvePeriodAsync(DatabaseTransactionContext transaction, Guid fiscalCalendarId, DateOnly postingDate, CancellationToken cancellationToken)
	{
		var periods = await _inventoryAccounting.FindOpenPeriodsAsync(transaction, fiscalCalendarId, postingDate, cancellationToken);
		if (periods.Count != 1) throw new InvalidOperationException($"Exactly one open accounting period is required for {postingDate:yyyy-MM-dd}.");
		return periods[0].Id;
	}

	private static void ValidateConfiguration(FinanceInventoryAccountingConfiguration configuration)
	{
		if (configuration.LegalEntityId == Guid.Empty) throw new ArgumentException("A legal entity is required.", nameof(configuration));
		if (configuration.FiscalCalendarId == Guid.Empty) throw new ArgumentException("A fiscal calendar is required.", nameof(configuration));
		if (configuration.ValuationMethod != FinanceInventoryValuationMethod.Fifo) throw new ArgumentException("Only FIFO valuation is implemented in the current F4 slice.", nameof(configuration));
		if (configuration.GoodsReceiptPostingProfileId <= 0 || configuration.SalesIssuePostingProfileId <= 0) throw new ArgumentException("Receipt and sales-issue posting profiles are required.", nameof(configuration));
	}

	private long RequireUser() => _audit.CurrentUserId ?? throw new InvalidOperationException("A signed-in user is required for Inventory Accounting configuration.");

	private static Guid MovementOperationId(long movementId, string eventCode)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"Depot:FinanceInventoryAccounting:{eventCode}:{movementId}"));
		return new Guid(bytes.AsSpan(0, 16));
	}
}
