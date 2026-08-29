// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;
using Depot.Data;
using Depot.Models;

namespace Depot.Repositories;

public sealed class FinanceInventoryAccountingRepository : DatabaseRepository
{
	public FinanceInventoryAccountingRepository(DatabaseAccess database) : base(database) { }

	public Task<FinanceInventoryAccountingConfiguration?> GetConfigurationAsync(CancellationToken cancellationToken = default) =>
		Database.QuerySingleOrDefaultAsync("SELECT Id,Version,LegalEntityId,FiscalCalendarId,PurchaseOrderPriceCurrency,ValuationMethod,GoodsReceiptPostingProfileId,SalesIssuePostingProfileId,IsActive FROM FinanceInventoryAccountingConfigurations ORDER BY Id;", ReadConfiguration, cancellationToken);

	internal Task<FinanceInventoryAccountingConfiguration?> GetConfigurationAsync(DatabaseTransactionContext transaction, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,Version,LegalEntityId,FiscalCalendarId,PurchaseOrderPriceCurrency,ValuationMethod,GoodsReceiptPostingProfileId,SalesIssuePostingProfileId,IsActive FROM FinanceInventoryAccountingConfigurations ORDER BY Id;", ReadConfiguration, cancellationToken);

	internal Task<long> CreateConfigurationAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingConfiguration value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryAccountingConfigurations (Version,LegalEntityId,FiscalCalendarId,PurchaseOrderPriceCurrency,ValuationMethod,GoodsReceiptPostingProfileId,SalesIssuePostingProfileId,IsActive) VALUES (1,$LegalEntityId,$FiscalCalendarId,$Currency,$Method,$ReceiptProfile,$IssueProfile,$IsActive);", cancellationToken, ConfigurationParameters(value));

	internal Task<int> UpdateConfigurationAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingConfiguration value, long expectedVersion, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryAccountingConfigurations SET Version=Version+1,LegalEntityId=$LegalEntityId,FiscalCalendarId=$FiscalCalendarId,PurchaseOrderPriceCurrency=$Currency,ValuationMethod=$Method,GoodsReceiptPostingProfileId=$ReceiptProfile,SalesIssuePostingProfileId=$IssueProfile,IsActive=$IsActive WHERE Id=$Id AND Version=$ExpectedVersion;", cancellationToken, ConfigurationParameters(value).Append(Parameter("$Id", value.Id)).Append(Parameter("$ExpectedVersion", expectedVersion)).ToArray());

	internal Task<FinanceInventoryAccountingEvent?> GetEventAsync(DatabaseTransactionContext transaction, long movementId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,MovementId,Kind,AccountingBookId,ItemId,Quantity,CurrencyCode,Amount,JournalEntryId,OperationId,ReversalOfMovementId,CreatedAtUtc,CreatedByUserId FROM FinanceInventoryAccountingEvents WHERE MovementId=$MovementId;", ReadEvent, cancellationToken, Parameter("$MovementId", movementId));

	internal Task<long> CreateEventAsync(DatabaseTransactionContext transaction, FinanceInventoryAccountingEvent value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryAccountingEvents (MovementId,Kind,AccountingBookId,ItemId,Quantity,CurrencyCode,Amount,JournalEntryId,OperationId,ReversalOfMovementId,CreatedAtUtc,CreatedByUserId) VALUES ($MovementId,$Kind,$BookId,$ItemId,$Quantity,$Currency,$Amount,$JournalEntryId,$OperationId,$ReversalOfMovementId,$CreatedAtUtc,$CreatedByUserId);", cancellationToken,
			Parameter("$MovementId", value.MovementId), Parameter("$Kind", (int)value.Kind), Parameter("$BookId", value.AccountingBookId.ToString("D")), Parameter("$ItemId", value.ItemId), Parameter("$Quantity", value.Quantity), Parameter("$Currency", value.Currency.Value), Parameter("$Amount", value.Amount), Parameter("$JournalEntryId", value.JournalEntryId), Parameter("$OperationId", value.OperationId.ToString("D")), Parameter("$ReversalOfMovementId", value.ReversalOfMovementId), Parameter("$CreatedAtUtc", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$CreatedByUserId", value.CreatedByUserId));

	internal Task<long> CreateLayerAsync(DatabaseTransactionContext transaction, FinanceInventoryValuationLayer value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryValuationLayers (AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId) VALUES ($BookId,$ItemId,$MovementId,$AcquiredDate,$Currency,$OriginalQuantity,$RemainingQuantity,$UnitCost,$CreatedAtUtc,$CreatedByUserId);", cancellationToken,
			Parameter("$BookId", value.AccountingBookId.ToString("D")), Parameter("$ItemId", value.ItemId), Parameter("$MovementId", value.SourceMovementId), Parameter("$AcquiredDate", value.AcquiredDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Parameter("$Currency", value.Currency.Value), Parameter("$OriginalQuantity", value.OriginalQuantity), Parameter("$RemainingQuantity", value.RemainingQuantity), Parameter("$UnitCost", value.UnitCost), Parameter("$CreatedAtUtc", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$CreatedByUserId", value.CreatedByUserId));

	internal Task<FinanceInventoryValuationLayer?> GetLayerBySourceAsync(DatabaseTransactionContext transaction, long movementId, CancellationToken cancellationToken) =>
		transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryValuationLayers WHERE SourceMovementId=$MovementId;", ReadLayer, cancellationToken, Parameter("$MovementId", movementId));

	internal async Task<IReadOnlyList<FinanceInventoryValuationLayer>> LockAvailableLayersAsync(DatabaseTransactionContext transaction, Guid bookId, long itemId, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET RemainingQuantity=RemainingQuantity WHERE AccountingBookId=$BookId AND ItemId=$ItemId AND ReversedAtUtc IS NULL AND RemainingQuantity>0;", cancellationToken, Parameter("$BookId", bookId.ToString("D")), Parameter("$ItemId", itemId));
		return await transaction.Session.QueryAsync("SELECT Id,AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryValuationLayers WHERE AccountingBookId=$BookId AND ItemId=$ItemId AND ReversedAtUtc IS NULL AND RemainingQuantity>0 ORDER BY AcquiredDate,Id;", ReadLayer, cancellationToken, Parameter("$BookId", bookId.ToString("D")), Parameter("$ItemId", itemId));
	}

	internal Task<int> SetRemainingQuantityAsync(DatabaseTransactionContext transaction, long id, int expectedQuantity, int newQuantity, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET RemainingQuantity=$NewQuantity WHERE Id=$Id AND RemainingQuantity=$ExpectedQuantity AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$NewQuantity", newQuantity), Parameter("$Id", id), Parameter("$ExpectedQuantity", expectedQuantity));

	internal Task<int> MarkLayerReversedAsync(DatabaseTransactionContext transaction, long id, int expectedQuantity, DateTime reversedAtUtc, long userId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET RemainingQuantity=0,ReversedAtUtc=$At,ReversedByUserId=$UserId WHERE Id=$Id AND RemainingQuantity=$ExpectedQuantity AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$At", reversedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$UserId", userId), Parameter("$Id", id), Parameter("$ExpectedQuantity", expectedQuantity));

	internal Task<long> CreateConsumptionAsync(DatabaseTransactionContext transaction, FinanceInventoryValuationConsumption value, CancellationToken cancellationToken) =>
		transaction.Session.InsertAsync("INSERT INTO FinanceInventoryValuationConsumptions (MovementId,LayerId,Quantity,UnitCost,Amount,CreatedAtUtc,CreatedByUserId) VALUES ($MovementId,$LayerId,$Quantity,$UnitCost,$Amount,$CreatedAtUtc,$CreatedByUserId);", cancellationToken, Parameter("$MovementId", value.MovementId), Parameter("$LayerId", value.LayerId), Parameter("$Quantity", value.Quantity), Parameter("$UnitCost", value.UnitCost), Parameter("$Amount", value.Amount), Parameter("$CreatedAtUtc", value.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$CreatedByUserId", value.CreatedByUserId));

	internal Task<IReadOnlyList<FinanceInventoryValuationConsumption>> GetActiveConsumptionsAsync(DatabaseTransactionContext transaction, long movementId, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT Id,MovementId,LayerId,Quantity,UnitCost,Amount,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryValuationConsumptions WHERE MovementId=$MovementId AND ReversedAtUtc IS NULL ORDER BY Id;", ReadConsumption, cancellationToken, Parameter("$MovementId", movementId));

	internal Task<int> MarkConsumptionReversedAsync(DatabaseTransactionContext transaction, long id, DateTime atUtc, long userId, CancellationToken cancellationToken) =>
		transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationConsumptions SET ReversedAtUtc=$At,ReversedByUserId=$UserId WHERE Id=$Id AND ReversedAtUtc IS NULL;", cancellationToken, Parameter("$At", atUtc.ToString("O", CultureInfo.InvariantCulture)), Parameter("$UserId", userId), Parameter("$Id", id));

	internal Task<FinanceInventoryValuationLayer?> LockLayerAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken) =>
		LockLayerCoreAsync(transaction, id, cancellationToken);

	private async Task<FinanceInventoryValuationLayer?> LockLayerCoreAsync(DatabaseTransactionContext transaction, long id, CancellationToken cancellationToken)
	{
		await transaction.Session.ExecuteAsync("UPDATE FinanceInventoryValuationLayers SET RemainingQuantity=RemainingQuantity WHERE Id=$Id;", cancellationToken, Parameter("$Id", id));
		return await transaction.Session.QuerySingleOrDefaultAsync("SELECT Id,AccountingBookId,ItemId,SourceMovementId,AcquiredDate,CurrencyCode,OriginalQuantity,RemainingQuantity,UnitCost,CreatedAtUtc,CreatedByUserId,ReversedAtUtc,ReversedByUserId FROM FinanceInventoryValuationLayers WHERE Id=$Id;", ReadLayer, cancellationToken, Parameter("$Id", id));
	}

	internal Task<IReadOnlyList<FinanceAccountingPeriodRecord>> FindOpenPeriodsAsync(DatabaseTransactionContext transaction, Guid calendarId, DateOnly date, CancellationToken cancellationToken) =>
		transaction.Session.QueryAsync("SELECT Id,StartDate,EndDate,Status FROM FinanceAccountingPeriods WHERE FiscalCalendarId=$CalendarId AND StartDate<=$Date AND EndDate>=$Date AND Status=0 ORDER BY StartDate,Id;", reader => new FinanceAccountingPeriodRecord(Guid.Parse(reader.GetString(0)), ReadDateOnly(reader, 1), ReadDateOnly(reader, 2), (AccountingPeriodStatus)Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)), cancellationToken, Parameter("$CalendarId", calendarId.ToString("D")), Parameter("$Date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

	private static DatabaseParameter[] ConfigurationParameters(FinanceInventoryAccountingConfiguration value) =>
	[
		Parameter("$LegalEntityId", value.LegalEntityId.ToString("D")), Parameter("$FiscalCalendarId", value.FiscalCalendarId.ToString("D")), Parameter("$Currency", value.PurchaseOrderPriceCurrency.Value), Parameter("$Method", (int)value.ValuationMethod), Parameter("$ReceiptProfile", value.GoodsReceiptPostingProfileId), Parameter("$IssueProfile", value.SalesIssuePostingProfileId), Parameter("$IsActive", value.IsActive)
	];

	private static FinanceInventoryAccountingConfiguration ReadConfiguration(DbDataReader reader) => new() { Id = reader.GetInt64(0), Version = reader.GetInt64(1), LegalEntityId = Guid.Parse(reader.GetString(2)), FiscalCalendarId = Guid.Parse(reader.GetString(3)), PurchaseOrderPriceCurrency = new CurrencyCode(reader.GetString(4)), ValuationMethod = (FinanceInventoryValuationMethod)Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), GoodsReceiptPostingProfileId = reader.GetInt64(6), SalesIssuePostingProfileId = reader.GetInt64(7), IsActive = ReadBool(reader, 8) };
	private static FinanceInventoryValuationLayer ReadLayer(DbDataReader reader) => new() { Id = reader.GetInt64(0), AccountingBookId = Guid.Parse(reader.GetString(1)), ItemId = reader.GetInt64(2), SourceMovementId = reader.GetInt64(3), AcquiredDate = ReadDateOnly(reader, 4), Currency = new CurrencyCode(reader.GetString(5)), OriginalQuantity = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture), RemainingQuantity = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture), UnitCost = ReadDecimal(reader, 8), CreatedAtUtc = ReadDateTime(reader, 9), CreatedByUserId = reader.IsDBNull(10) ? null : reader.GetInt64(10), ReversedAtUtc = reader.IsDBNull(11) ? null : ReadDateTime(reader, 11), ReversedByUserId = reader.IsDBNull(12) ? null : reader.GetInt64(12) };
	private static FinanceInventoryValuationConsumption ReadConsumption(DbDataReader reader) => new() { Id = reader.GetInt64(0), MovementId = reader.GetInt64(1), LayerId = reader.GetInt64(2), Quantity = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), UnitCost = ReadDecimal(reader, 4), Amount = ReadDecimal(reader, 5), CreatedAtUtc = ReadDateTime(reader, 6), CreatedByUserId = reader.IsDBNull(7) ? null : reader.GetInt64(7), ReversedAtUtc = reader.IsDBNull(8) ? null : ReadDateTime(reader, 8), ReversedByUserId = reader.IsDBNull(9) ? null : reader.GetInt64(9) };
	private static FinanceInventoryAccountingEvent ReadEvent(DbDataReader reader) => new() { Id = reader.GetInt64(0), MovementId = reader.GetInt64(1), Kind = (FinanceInventoryAccountingEventKind)Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture), AccountingBookId = Guid.Parse(reader.GetString(3)), ItemId = reader.GetInt64(4), Quantity = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture), Currency = new CurrencyCode(reader.GetString(6)), Amount = ReadDecimal(reader, 7), JournalEntryId = reader.GetInt64(8), OperationId = Guid.Parse(reader.GetString(9)), ReversalOfMovementId = reader.IsDBNull(10) ? null : reader.GetInt64(10), CreatedAtUtc = ReadDateTime(reader, 11), CreatedByUserId = reader.IsDBNull(12) ? null : reader.GetInt64(12) };
	private static bool ReadBool(DbDataReader reader, int ordinal) => Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static decimal ReadDecimal(DbDataReader reader, int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static DateTime ReadDateTime(DbDataReader reader, int ordinal) => Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
	private static DateOnly ReadDateOnly(DbDataReader reader, int ordinal) => DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture));
}
